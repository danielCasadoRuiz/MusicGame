using System;
using System.Collections;
using System.Threading;
using UnityEngine;

/// <summary>One runtime analysis request (see AudioPreAnalyzer.Begin). Callbacks run on the main
/// thread and only ever for the CURRENT request of the current song selection.</summary>
public sealed class SongAnalysisRequest
{
    public AudioClip Clip;
    public string    SongId;              // stable catalog id (null for an imported file)
    public string    LocalFilePath;       // imported file (null for a catalog song)
    /// <summary>GameSession.SelectionGeneration this request belongs to (0 = not bound to a selection).</summary>
    public int       SelectionGeneration;
    public Action<MusicTagScore[]> OnTagsReady;   // once, before the profile (tags may be null/empty)
    public Action<SongProfile>     OnProfileReady;
    public Action<string>          OnFailed;      // explicit error (no partial profile is ever published)

    public int Id { get; internal set; }
}

/// <summary>
/// Runtime owner of the dynamic song analysis (imported songs, and catalog songs until P3 ships their
/// published profiles). Wraps the shared SongAnalysisJob (Audio/Analysis — the same pipeline the editor
/// drives synchronously) with the runtime lifecycle:
///   - request id, at most ONE active request; <see cref="Begin"/> on a new song cancels the previous
///     one, a duplicate Begin (same clip + same selection) returns the running request's id;
///   - cancellation (token) — worker stages stop within a chunk, an in-flight Sentis inference is
///     abandoned and disposed when its jobs finish (SentisMusicTagger.PumpDeferred, every frame);
///   - EVERY global event (PreAnalysisStarted / PreAnalysisProgress / SongProfileReady) and callback is
///     gated by <see cref="IsCurrent"/>: a cancelled, superseded or other-selection request is
///     invalidated BEFORE it could publish anything;
///   - progress is the job's real monotonic value, published from Update on the main thread;
///   - the complete profile is published exactly once (no fake delay: a cache hit is ready as soon as
///     the binary profile is read and its derived data rebuilt).
/// </summary>
public class AudioPreAnalyzer : MonoBehaviour
{
    private sealed class Active
    {
        public SongAnalysisRequest     Request;
        public SongAnalysisJob         Job;
        public CancellationTokenSource Cts;
        public float                   PublishedProgress = -1f;
        public bool                    Cancelled;
    }

    private static int s_nextId;
    // Every job iterator still running (incl. cancelled ones draining their workers): disposed if this
    // component goes away, so the global analysis gate and abandoned inferences are always released.
    private readonly System.Collections.Generic.List<IEnumerator> _running = new();
    private Active _active;

    public int  CurrentRequestId => _active != null ? _active.Request.Id : 0;
    public bool IsBusy => _active != null;
    /// <summary>Diagnostics: the active request's pipeline stage.</summary>
    public SongAnalysisStage CurrentStage => _active != null ? _active.Job.Stage : SongAnalysisStage.Done;

    /// <summary>Starts (or joins) the analysis of `request.Clip`. Returns the request id.</summary>
    public int Begin(SongAnalysisRequest request, AudioAnalysisConfig config)
    {
        if (request == null || request.Clip == null) throw new ArgumentException("request with a clip required");
        if (_active != null && !_active.Cancelled && _active.Request.Clip == request.Clip
            && _active.Request.SelectionGeneration == request.SelectionGeneration)
        {
            // Duplicate (e.g. the flow state re-entered while the same song is still analysing):
            // keep the running job, route its results to the newest callbacks.
            _active.Request.OnTagsReady    = request.OnTagsReady;
            _active.Request.OnProfileReady = request.OnProfileReady;
            _active.Request.OnFailed       = request.OnFailed;
            Debug.Log($"[AudioPreAnalyzer] Request #{_active.Request.Id} already running for '{request.Clip.name}' — joined.");
            return _active.Request.Id;
        }

        Cancel(); // invalidate the previous request FIRST: from here on it can publish nothing
        request.Id = ++s_nextId;

        // Main thread: immutable snapshot of everything the workers need.
        SongProfileCache.InitOnMainThread();
        var snapshot = SongAnalysisSnapshot.Create(config);
        var input = new SongAnalysisInput
        {
            Clip = request.Clip,
            IdentitySource = SongAudioIdentitySource.For(request.Clip, request.SongId, request.LocalFilePath),
        };
        var a = new Active { Request = request, Cts = new CancellationTokenSource() };
        a.Job = new SongAnalysisJob(snapshot, input, a.Cts.Token)
        {
            OnCacheProbed = hit =>
            {
                if (IsCurrent(a)) EventBus.Publish(new PreAnalysisStartedEvent { IsCacheHit = hit, RequestId = request.Id });
            },
            OnTagsReady = tags =>
            {
                if (IsCurrent(a)) a.Request.OnTagsReady?.Invoke(tags);
            },
        };
        _active = a;
        Debug.Log($"[AudioPreAnalyzer] Request #{request.Id}: '{request.Clip.name}' ({input.IdentitySource.Kind} identity, " +
                  $"{snapshot.WorkerBudget} workers, tagging {(snapshot.TaggingEnabled ? "on" : "off")}, params {snapshot.Params.Hash:x16}).");
        StartCoroutine(Drive(a));
        return request.Id;
    }

    /// <summary>Cancels the active request (no event or callback of it will fire any more).</summary>
    public void Cancel()
    {
        if (_active == null) return;
        _active.Cancelled = true;
        _active.Cts.Cancel();
        Debug.Log($"[AudioPreAnalyzer] Request #{_active.Request.Id} cancelled.");
        _active = null;
    }

    /// <summary>The request may still publish: it is the active one, not cancelled, and still belongs to
    /// GameSession's current song selection.</summary>
    private bool IsCurrent(Active a)
    {
        if (a == null || a != _active || a.Cancelled) return false;
        var session = GameSession.Instance;
        return a.Request.SelectionGeneration == 0 || session == null || session.SelectionGeneration == a.Request.SelectionGeneration;
    }

    private void Update()
    {
        SentisMusicTagger.PumpDeferred();

        var a = _active;
        if (a == null) return;
        if (!IsCurrent(a)) { Cancel(); return; } // the selection moved on: stop wasting work
        float p = a.Job.Progress;
        if (p > a.PublishedProgress + 0.002f || (p >= 1f && a.PublishedProgress < 1f))
        {
            a.PublishedProgress = p;
            EventBus.Publish(new PreAnalysisProgressEvent { Progress = p, RequestId = a.Request.Id });
        }
    }

    private IEnumerator Drive(Active a)
    {
        var it = a.Job.Run();
        _running.Add(it);
        string error = null;
        while (true)
        {
            bool next;
            try { next = it.MoveNext(); }
            catch (OperationCanceledException) { next = false; }
            catch (Exception e) { error = e.Message; Debug.LogError($"[AudioPreAnalyzer] Request #{a.Request.Id} failed: {e}"); next = false; }
            if (!next) break;

            if (it.Current is IPipelineWait w)
                while (!w.IsDone)
                {
                    // An in-flight inference is not waited for once cancelled — the job abandons it.
                    if (a.Cts.IsCancellationRequested && w is SentisMusicTagger.Inference) break;
                    yield return null;
                }
            else yield return null;
        }
        _running.Remove(it);
        (it as IDisposable)?.Dispose();
        a.Cts.Dispose();

        if (!IsCurrent(a)) yield break; // superseded/cancelled: nothing is published
        _active = null;
        if (error != null || a.Job.Outcome == null)
        {
            a.Request.OnFailed?.Invoke(error ?? "analysis produced no result");
            yield break;
        }

        var o = a.Job.Outcome;
        Debug.Log($"[AudioPreAnalyzer] Request #{a.Request.Id} done — {(o.CacheHit ? "cache HIT" : o.RhythmReused ? "rhythm reused" : o.TagsReused ? "tags reused" : "full analysis")} " +
                  $"{o.Identity}: {o.Profile.onsetTimes.Length} onsets, BPM {o.Profile.estimatedBPM:F1} (conf {o.Profile.bpmConfidence:F2}), " +
                  $"{o.Profile.beatTimes.Length} beats.\n  {o.Timings}");
        if (a.PublishedProgress < 1f) EventBus.Publish(new PreAnalysisProgressEvent { Progress = 1f, RequestId = a.Request.Id });
        EventBus.Publish(new SongProfileReadyEvent { Profile = o.Profile, SelectionGeneration = a.Request.SelectionGeneration });
        a.Request.OnProfileReady?.Invoke(o.Profile);
    }

    // Coroutines stop with the component: run the jobs' finally blocks (gate release, inference abandon).
    private void OnDisable()
    {
        Cancel();
        foreach (var it in _running) (it as IDisposable)?.Dispose();
        _running.Clear();
        SentisMusicTagger.PumpDeferred(force: true);
    }
}
