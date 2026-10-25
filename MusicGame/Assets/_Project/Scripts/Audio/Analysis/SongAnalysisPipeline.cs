using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MusicGame.Audio.Analysis;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Something the pipeline waits for: polled once per frame at runtime, or waited for
/// synchronously by the editor's blocking entry point.</summary>
public interface IPipelineWait
{
    /// <summary>MAIN THREAD, never blocks.</summary>
    bool IsDone { get; }
    /// <summary>Blocking wait (editor / tools only).</summary>
    void WaitBlocking();
}

/// <summary>Pure work on a thread-pool thread (Task.Run). Exceptions (incl. cancellation) surface in
/// <see cref="ThrowIfFailed"/> on the main thread.</summary>
public sealed class WorkItem : IPipelineWait
{
    private readonly Task _task;
    private WorkItem(Task t) { _task = t; }
    public static WorkItem Run(Action work) => new(Task.Run(work));
    public bool IsDone => _task.IsCompleted;
    public void WaitBlocking() { try { _task.Wait(); } catch { /* surfaced by ThrowIfFailed */ } }
    public void ThrowIfFailed() => _task.GetAwaiter().GetResult();
}

public enum SongAnalysisStage { Queued, Identity, CacheProbe, Capture, TagPreprocess, Inference, Rhythm, Compose, Done }

public enum SongAnalysisCacheMode { ReadWrite, ReadOnly, WriteOnly, None }

/// <summary>What to analyse. Either a Clip (PCM captured on the main thread by the pipeline) or
/// pre-decoded Mono PCM (e.g. a master WAV read by the P3 bake), plus how to identify the audio.</summary>
public sealed class SongAnalysisInput
{
    public AudioClip Clip;
    public float[]   Mono;          // alternative to Clip (mono, already mixed)
    public int       SampleRate;    // with Mono
    public int       Channels = 1;  // with Mono: channel count of the source (header/identity only)
    /// <summary>Known identity (P3 bake: source/master hash). Otherwise resolved from IdentitySource.</summary>
    public SongAudioIdentity? Identity;
    public SongAudioIdentitySource IdentitySource;
    public SongAnalysisCacheMode Cache = SongAnalysisCacheMode.ReadWrite;
}

/// <summary>Per-stage wall times (ms) of one request, measured where the work runs.</summary>
public sealed class SongAnalysisTimings
{
    public double gateWaitMs, identityMs, cacheProbeMs, captureMs, captureMaxSliceMs, pcmIdentityMs,
                  tagPreprocessMs, inferenceScheduleMs, inferenceMaxScheduleMs, inferenceWallMs, rhythmWallMs, composeMs, derivedMs,
                  serializeMs, cacheWriteMs, totalMs;
    public int tagChunk;
    public AnalysisTimings core; // the core's own stage breakdown (rhythm branch)

    public override string ToString() =>
        $"total {totalMs:F0} | gate {gateWaitMs:F0} | identity {identityMs:F0} | cacheProbe {cacheProbeMs:F0} | capture {captureMs:F0} (max main-thread slice {captureMaxSliceMs:F1}) | " +
        $"pcmId {pcmIdentityMs:F0} | tagPrep {tagPreprocessMs:F0} | inference (chunks of {tagChunk}) main-thread schedule {inferenceScheduleMs:F1} total, max {inferenceMaxScheduleMs:F1} per frame, wall {inferenceWallMs:F0} | rhythm {rhythmWallMs:F0} | " +
        $"compose {composeMs:F0} (derived {derivedMs:F0}) | serialize {serializeMs:F0} | cacheWrite {cacheWriteMs:F0} ms" +
        (core != null ? $"\n    core: {core}" : "");
}

/// <summary>Result of one analysis: the runtime profile plus everything P3 needs to persist it.</summary>
public sealed class SongAnalysisOutcome
{
    public SongProfile         Profile;
    public AnalysisResult      Raw;           // same raw arrays the profile holds — never mutate
    public MusicTagScore[]     Tags;          // null = tagging off/unavailable
    public ProfileHeader       Header;
    public byte[]              ProfileBytes;  // the binary profile (as written, or as read on a cache hit)
    public SongAudioIdentity   Identity;
    public bool                CacheHit;      // raw + tags both reused
    public bool                RhythmReused;
    public bool                TagsReused;
    public SongAnalysisTimings Timings;
}

/// <summary>
/// ONE song analysis, the same code for runtime imports (driven per frame by AudioPreAnalyzer) and
/// for editor tools (driven synchronously by <see cref="SongAnalysisPipeline"/>). Stages:
///   identity (worker) → cache probe (worker) → PCM capture (MAIN, time-sliced, GetData result checked)
///   → [PCM identity + probe] → tag preprocessing (worker, full budget) → Sentis inference (MAIN
///   schedule, non-blocking readback) OVERLAPPING the rhythm core (worker, budget − 1) → compose +
///   derived + serialize + cache write (worker).
/// Holds the global gate (AnalysisConcurrency) for its whole duration. Progress is monotonic and
/// readable from any thread (<see cref="Progress"/>); callbacks run on the main thread (the iterator
/// always runs there). Cancellation: the token stops worker stages within a chunk; an in-flight
/// inference is abandoned (disposed when its jobs finish) — never a partial result.
/// </summary>
public sealed class SongAnalysisJob
{
    public readonly SongAnalysisSnapshot Snapshot;
    private readonly SongAnalysisInput   _in;
    private readonly CancellationToken   _ct;

    /// <summary>Main thread: tags known (possibly null/empty) — once, before the profile.</summary>
    public Action<MusicTagScore[]> OnTagsReady;
    /// <summary>Main thread: the cache probe finished (true = full cache hit, no audio processing).</summary>
    public Action<bool> OnCacheProbed;
    /// <summary>Main-thread budget per frame for the PCM capture (runtime); ignored when blocking.</summary>
    public double CaptureSliceMs = 4.0;

    /// <summary>Diagnostics: analyses started in this process / currently running (probes assert that a
    /// published catalog song never starts one; PulseTimingDiagnostics reports concurrent ones).</summary>
    public static int RunsStarted { get; private set; }
    public static int ActiveCount { get; private set; }

    public SongAnalysisStage   Stage   { get; private set; } = SongAnalysisStage.Queued;
    public SongAnalysisOutcome Outcome { get; private set; }
    public bool Blocking { get; set; }

    private readonly object _progressLock = new();
    private float _progress;
    public float Progress { get { lock (_progressLock) return _progress; } }

    // Progress shares of the stages (cache hits jump straight to the end).
    private const float PId = 0.02f, PProbe = 0.04f, PCapture = 0.14f, PTagPrep = 0.19f, PRhythmEnd = 0.92f;

    public SongAnalysisJob(SongAnalysisSnapshot snapshot, SongAnalysisInput input, CancellationToken ct)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _in = input ?? throw new ArgumentNullException(nameof(input));
        _ct = ct;
        if (_in.Clip == null && _in.Mono == null) throw new ArgumentException("SongAnalysisInput needs a Clip or Mono PCM");
    }

    private void Report(float v) { lock (_progressLock) if (v > _progress) _progress = Math.Min(1f, v); }

    private AnalysisRunOptions Options(int workers, float from, float to) => new()
    {
        maxWorkers = workers,
        cancellation = _ct,
        progress = v => Report(from + (to - from) * v),
    };

    private static double Ms(long t0) => (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;

    /// <summary>The pipeline. Yields IPipelineWait (wait for it) or null (frame boundary).</summary>
    public IEnumerator Run()
    {
        var tm = new SongAnalysisTimings();
        long tTotal = Stopwatch.GetTimestamp();
        bool gate = false;
        RunsStarted++;
        ActiveCount++;
        float[] mono = _in.Mono;
        SentisMusicTagger.Inference inference = null;
        try
        {
            // ── Global gate: one heavy analysis at a time ───────────────────────────────────
            long t = Stopwatch.GetTimestamp();
            if (Blocking)
            {
                if (!AnalysisConcurrency.Gate.Wait(TimeSpan.FromSeconds(60), _ct))
                    throw new InvalidOperationException("another song analysis holds the analysis gate");
            }
            else
                while (!AnalysisConcurrency.Gate.Wait(0)) { _ct.ThrowIfCancellationRequested(); yield return null; }
            gate = true;
            tm.gateWaitMs = Ms(t);

            int sampleRate = _in.Clip != null ? _in.Clip.frequency : _in.SampleRate;
            int channels   = _in.Clip != null ? _in.Clip.channels  : Math.Max(1, _in.Channels);
            int samples    = _in.Clip != null ? _in.Clip.samples   : mono.Length;
            if (sampleRate <= 0 || samples <= 0) throw new InvalidOperationException($"invalid audio: {samples} samples @ {sampleRate} Hz");
            bool read  = _in.Cache == SongAnalysisCacheMode.ReadWrite || _in.Cache == SongAnalysisCacheMode.ReadOnly;
            bool write = _in.Cache == SongAnalysisCacheMode.ReadWrite || _in.Cache == SongAnalysisCacheMode.WriteOnly;

            // ── 1. Identity (file hash on a worker) ─────────────────────────────────────────
            Stage = SongAnalysisStage.Identity;
            SongAudioIdentity id = _in.Identity ?? default;
            var src = _in.IdentitySource;
            if (!id.IsValid && src != null && !src.NeedsPcm)
            {
                t = Stopwatch.GetTimestamp();
                var w = WorkItem.Run(() => id = src.Resolve());
                yield return w;
                w.ThrowIfFailed();
                tm.identityMs = Ms(t);
            }
            Report(PId);

            // ── 2. Cache probe ──────────────────────────────────────────────────────────────
            CacheProbe probe = null;
            if (id.IsValid && read)
            {
                Stage = SongAnalysisStage.CacheProbe;
                t = Stopwatch.GetTimestamp();
                var w = WorkItem.Run(() => probe = CacheProbe.Run(id, Snapshot, samples, sampleRate));
                yield return w;
                w.ThrowIfFailed();
                tm.cacheProbeMs = Ms(t);
            }
            Report(PProbe);
            if (id.IsValid) OnCacheProbed?.Invoke(probe != null && probe.FullHit);

            if (probe == null || !probe.FullHit)
            {
                // ── 3. PCM capture (main thread, sliced) ───────────────────────────────────────
                if (mono == null)
                {
                    Stage = SongAnalysisStage.Capture;
                    t = Stopwatch.GetTimestamp();
                    var cap = new PcmCapture(_in.Clip, Blocking ? double.MaxValue : CaptureSliceMs, _ct);
                    while (cap.Step()) { Report(PProbe + (PCapture - PProbe) * cap.Fraction); yield return null; }
                    mono = cap.Mono;
                    tm.captureMs = Ms(t);
                    tm.captureMaxSliceMs = cap.MaxSliceMs;
                }
                Report(PCapture);

                // PCM identity (fallback) + its cache probe.
                if (!id.IsValid)
                {
                    if (src == null) throw new InvalidOperationException("no audio identity and no identity source");
                    t = Stopwatch.GetTimestamp();
                    var m = mono;
                    var w = WorkItem.Run(() =>
                    {
                        id = src.ResolvePcm(m, channels, sampleRate);
                        if (read) probe = CacheProbe.Run(id, Snapshot, samples, sampleRate);
                    });
                    yield return w;
                    w.ThrowIfFailed();
                    tm.pcmIdentityMs = Ms(t);
                    OnCacheProbed?.Invoke(probe != null && probe.FullHit);
                }
            }

            MusicTagScore[] tags = probe?.Tags;
            AnalysisResult raw = probe?.Raw;
            bool fullHit = probe != null && probe.FullHit;

            if (!fullHit)
            {
                // ── 4. Tag preprocessing (worker, full budget) + inference (main thread schedule) ─
                bool tagsReused = probe != null && probe.TagsUsable;
                if (!tagsReused && Snapshot.TaggingEnabled)
                {
                    Stage = SongAnalysisStage.TagPreprocess;
                    t = Stopwatch.GetTimestamp();
                    float[] batch = null; int patches = 0;
                    var m = mono;
                    var w = WorkItem.Run(() => batch = Snapshot.TagPreprocessor.BuildBatch(m, sampleRate, Options(Snapshot.WorkerBudget, PCapture, PTagPrep), out patches));
                    yield return w;
                    w.ThrowIfFailed();
                    tm.tagPreprocessMs = Ms(t);
                    if (batch != null && SentisMusicTagger.TryGetModel(out var model))
                    {
                        inference = new SentisMusicTagger.Inference(model, batch, patches, Snapshot.TagPatchesPerChunk);
                        tm.tagChunk = inference.PatchesPerChunk;
                    }
                    else tags = Array.Empty<MusicTagScore>(); // shorter than one 3 s patch: no tags
                    batch = null;
                }
                Report(PTagPrep);
                if (inference == null) OnTagsReady?.Invoke(tags); // reused, too short, or tagging off

                // ── 5. Rhythm core (worker) overlapping the inference ──────────────────────────
                WorkItem rhythm = null;
                long tRhythm = Stopwatch.GetTimestamp(), tInf = tRhythm;
                double rhythmDoneMs = 0;
                if (raw == null)
                {
                    Stage = SongAnalysisStage.Rhythm;
                    var m = mono;
                    int workers = inference != null ? AnalysisConcurrency.OverlapWorkers(Snapshot.WorkerBudget) : Snapshot.WorkerBudget;
                    rhythm = WorkItem.Run(() =>
                    {
                        raw = MusicAnalyzer.Analyze(m, sampleRate, Snapshot.Params, Options(workers, PTagPrep, PRhythmEnd));
                        rhythmDoneMs = Ms(tRhythm);
                    });
                }
                if (inference != null)
                {
                    Stage = SongAnalysisStage.Inference;
                    yield return inference;
                    if (_ct.IsCancellationRequested)
                    {
                        inference.Abandon();
                        inference = null;
                        if (rhythm != null) yield return rhythm; // let the worker observe the token before releasing the gate
                        _ct.ThrowIfCancellationRequested();
                    }
                    tm.inferenceScheduleMs = inference.ScheduleMs;
                    tm.inferenceMaxScheduleMs = inference.MaxScheduleMs;
                    var taggram = inference.TakeTaggram();
                    int patches = inference.NumPatches;
                    inference = null;
                    tm.inferenceWallMs = Ms(tInf);
                    tags = SentisMusicTagger.SelectTags(taggram, patches, Snapshot.TagTopN, Snapshot.TagMinScore);
                    OnTagsReady?.Invoke(tags);
                }
                if (rhythm != null)
                {
                    if (Stage != SongAnalysisStage.Inference) Stage = SongAnalysisStage.Rhythm;
                    yield return rhythm;
                    rhythm.ThrowIfFailed();
                    tm.rhythmWallMs = rhythmDoneMs;
                    tm.core = raw.timings;
                }
            }
            else OnTagsReady?.Invoke(tags);
            mono = null; // the PCM copy is not needed any more (released before compose)
            Report(PRhythmEnd);
            _ct.ThrowIfCancellationRequested();

            // ── 6. Serialize + cache write + compose + derived (worker) ────────────────────
            Stage = SongAnalysisStage.Compose;
            var outcome = new SongAnalysisOutcome { Identity = id, Raw = raw, Tags = tags, CacheHit = fullHit, Timings = tm,
                                                    RhythmReused = probe?.Raw != null, TagsReused = probe != null && probe.TagsUsable };
            {
                var w = WorkItem.Run(() =>
                {
                    long t1 = Stopwatch.GetTimestamp();
                    if (fullHit)
                    {
                        outcome.Header = probe.Header;
                        outcome.ProfileBytes = probe.Bytes;
                    }
                    else
                    {
                        outcome.Header = SongProfileSerializer.MakeHeader(id, Snapshot, samples, channels, sampleRate, tags != null);
                        outcome.ProfileBytes = SongProfileSerializer.Write(outcome.Header, raw, tags); // RAW only, before any derived pass
                        tm.serializeMs = Ms(t1);
                        if (write)
                        {
                            long t2 = Stopwatch.GetTimestamp();
                            SongProfileCache.Save(id, outcome.ProfileBytes);
                            tm.cacheWriteMs = Ms(t2);
                        }
                    }
                    _ct.ThrowIfCancellationRequested();
                    long t3 = Stopwatch.GetTimestamp();
                    outcome.Profile = SongProfileFactory.Create(raw, tags, Snapshot.TagModelVersion, id, Snapshot.Params.Hash, null);
                    long t4 = Stopwatch.GetTimestamp();
                    SongProfileDerived.Compute(outcome.Profile, Snapshot.Derived);
                    tm.derivedMs = Ms(t4);
                    tm.composeMs = Ms(t3);
                });
                yield return w;
                w.ThrowIfFailed();
            }
            tm.totalMs = Ms(tTotal);
            Report(1f);
            Stage = SongAnalysisStage.Done;
            Outcome = outcome;
        }
        finally
        {
            inference?.Abandon();
            mono = null;
            if (gate) AnalysisConcurrency.Gate.Release();
            ActiveCount--;
        }
    }

    /// <summary>Main-thread PCM capture of a clip: waits for the clip data, then GetData in chunks into
    /// a reusable interleaved buffer and mixes straight into the mono array (no full interleaved copy),
    /// at most `sliceMs` of main-thread time per Step. Every GetData result is checked.</summary>
    private sealed class PcmCapture
    {
        private const int ChunkFrames = 1 << 16;
        private readonly AudioClip _clip;
        private readonly double _sliceMs;
        private readonly CancellationToken _ct;
        private readonly int _channels, _samples;
        private float[] _buf;
        private int _pos;
        private float _peak;
        private readonly long _loadStart = Stopwatch.GetTimestamp();
        public float[] Mono { get; private set; }
        public double MaxSliceMs { get; private set; }
        public float Fraction => _samples > 0 ? (float)_pos / _samples : 1f;

        public PcmCapture(AudioClip clip, double sliceMs, CancellationToken ct)
        {
            _clip = clip; _sliceMs = sliceMs; _ct = ct;
            _channels = Math.Max(1, clip.channels);
            _samples = clip.samples;
        }

        /// <summary>True while more steps are needed.</summary>
        public bool Step()
        {
            _ct.ThrowIfCancellationRequested();
            long t0 = Stopwatch.GetTimestamp();
            try
            {
                if (_clip.loadState != AudioDataLoadState.Loaded)
                {
                    if (_clip.loadState == AudioDataLoadState.Failed) throw new InvalidOperationException($"AudioClip '{_clip.name}' failed to load its audio data");
                    if (_clip.loadState == AudioDataLoadState.Unloaded) _clip.LoadAudioData();
                    if (Ms(_loadStart) > 30000) throw new TimeoutException($"AudioClip '{_clip.name}' did not load within 30 s");
                    if (_clip.loadState != AudioDataLoadState.Loaded) { Thread.Sleep(0); return true; }
                }
                if (_clip.loadType == AudioClipLoadType.Streaming)
                    throw new InvalidOperationException($"AudioClip '{_clip.name}' is Streaming: its samples cannot be read for analysis");
                Mono ??= new float[_samples];
                _buf ??= new float[ChunkFrames * _channels];
                while (_pos < _samples)
                {
                    int n = Math.Min(ChunkFrames, _samples - _pos);
                    float[] buf = n == ChunkFrames ? _buf : new float[n * _channels]; // exact tail: GetData would wrap around
                    if (!_clip.GetData(buf, _pos))
                        throw new InvalidOperationException($"AudioClip.GetData failed for '{_clip.name}' at sample {_pos} (loadType {_clip.loadType}, loadState {_clip.loadState})");
                    MixInto(buf, n);
                    _pos += n;
                    if (Ms(t0) >= _sliceMs) return _pos < _samples || Finish();
                }
                return Finish();
            }
            finally { MaxSliceMs = Math.Max(MaxSliceMs, Ms(t0)); }
        }

        private void MixInto(float[] buf, int frames)
        {
            float peak = _peak;
            if (_channels == 1)
            {
                Array.Copy(buf, 0, Mono, _pos, frames);
                for (int i = 0; i < frames; i++) { float a = Math.Abs(buf[i]); if (a > peak) peak = a; }
            }
            else
            {

                for (int i = 0; i < frames; i++)
                {
                    float s = 0f;
                    int o = i * _channels;
                    for (int c = 0; c < _channels; c++) s += buf[o + c];
                    s /= _channels; // same formula as the core MixToMono / the legacy analyzer
                    Mono[_pos + i] = s;
                    float a = Math.Abs(s); if (a > peak) peak = a;
                }
            }
            _peak = peak;
        }

        private bool Finish()
        {
            // A clip whose samples cannot be read can still "succeed" with zeros (e.g. compressed data
            // the platform cannot decode for GetData): never analyse that silently.
            if (_peak == 0f && _samples > _clip.frequency)
                throw new InvalidOperationException($"AudioClip '{_clip.name}' returned only digital silence from GetData (loadType {_clip.loadType}) — not analysed");
            _buf = null;
            return false;
        }
    }

    /// <summary>What the local cache can contribute for this audio under these params.</summary>
    private sealed class CacheProbe
    {
        public ProfileHeader   Header;
        public byte[]          Bytes;
        public AnalysisResult  Raw;     // non-null when the rhythm part is reusable
        public MusicTagScore[] Tags;    // non-null when the tags are reusable
        public bool TagsUsable;
        public bool FullHit;

        public static CacheProbe Run(SongAudioIdentity id, SongAnalysisSnapshot snap, int samples, int frequency)
        {
            var bytes = SongProfileCache.TryReadBytes(id);
            if (bytes == null) return null;
            try
            {
                var h = ProfileBinary.ReadHeader(bytes, out _);
                bool rhythmOk = SongProfileSerializer.RhythmCompatible(h, snap, id, samples, frequency);
                bool tagsOk   = SongProfileSerializer.TagsCompatible(h, snap, id);
                if (!rhythmOk && !tagsOk) return null;
                var raw = SongProfileSerializer.Read(bytes, out h, out var tags);
                var p = new CacheProbe { Header = h, Bytes = bytes, Raw = rhythmOk ? raw : null, Tags = tagsOk ? tags : null, TagsUsable = tagsOk };
                // Full hit: the rhythm part is current and the tags are either current or not wanted
                // (tagging off: whatever tags were stored are kept, as before).
                p.FullHit = rhythmOk && (tagsOk || !snap.TaggingEnabled);
                if (p.FullHit && !tagsOk) p.Tags = tags;
                return p;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SongProfileCache] Ignoring unreadable cache entry for {id}: {e.Message}");
                return null;
            }
        }
    }
}

/// <summary>
/// Synchronous, cancellable entry points over the SAME SongAnalysisJob the runtime uses — for editor
/// tools (the P3 catalog bake, SongSimilarityBake). MAIN THREAD only (the Sentis inference must be
/// scheduled there); workers still do the heavy work within the global budget. Progress is reported
/// on the calling thread every ~50 ms (safe for EditorUtility progress bars); cancel the token to stop
/// (OperationCanceledException, no partial result).
/// </summary>
public static class SongAnalysisPipeline
{
    /// <summary>Analyses (or loads from the local cache) an AudioClip. `songKey` names the song (catalog
    /// songId); the identity is the editor source asset (+ import settings) when available, else `identity`,
    /// else the decoded PCM.</summary>
    public static SongAnalysisOutcome AnalyzeClipBlocking(AudioClip clip, string songKey, AudioAnalysisConfig config,
                                                          CancellationToken ct, Action<float> progress = null,
                                                          SongAnalysisCacheMode cache = SongAnalysisCacheMode.ReadWrite,
                                                          SongAudioIdentity? identity = null)
    {
        if (clip == null) throw new ArgumentNullException(nameof(clip));
        SongProfileCache.InitOnMainThread();
        var input = new SongAnalysisInput
        {
            Clip = clip,
            Identity = identity,
            IdentitySource = identity.HasValue ? null : SongAudioIdentitySource.For(clip, songKey, null),
            Cache = cache,
        };
        return RunBlocking(new SongAnalysisJob(SongAnalysisSnapshot.Create(config), input, ct), progress);
    }

    /// <summary>Analyses pre-decoded mono PCM (e.g. a lossless master) under a caller-computed identity.</summary>
    public static SongAnalysisOutcome AnalyzePcmBlocking(float[] mono, int sampleRate, int sourceChannels, SongAudioIdentity identity,
                                                         SongAnalysisSnapshot snapshot, CancellationToken ct, Action<float> progress = null,
                                                         SongAnalysisCacheMode cache = SongAnalysisCacheMode.None)
    {
        if (mono == null) throw new ArgumentNullException(nameof(mono));
        if (!identity.IsValid) throw new ArgumentException("a valid audio identity is required for PCM input", nameof(identity));
        SongProfileCache.InitOnMainThread();
        var input = new SongAnalysisInput { Mono = mono, SampleRate = sampleRate, Channels = sourceChannels, Identity = identity, Cache = cache };
        return RunBlocking(new SongAnalysisJob(snapshot, input, ct), progress);
    }

    /// <summary>Drives a job to completion on the calling (main) thread.</summary>
    public static SongAnalysisOutcome RunBlocking(SongAnalysisJob job, Action<float> progress)
    {
        job.Blocking = true;
        var it = job.Run();
        try
        {
            while (it.MoveNext())
            {
                if (it.Current is not IPipelineWait w) continue;
                if (w is WorkItem)
                    while (!w.IsDone) { Thread.Sleep(15); progress?.Invoke(job.Progress); }
                w.WaitBlocking();
            }
        }
        finally { (it as IDisposable)?.Dispose(); }
        progress?.Invoke(1f);
        return job.Outcome;
    }

    /// <summary>Reads a binary profile and builds the runtime SongProfile (raw + derived). For published
    /// profiles (P3) and the cache. Throws InvalidDataException on a corrupt/foreign file.</summary>
    public static SongProfile LoadProfile(byte[] bytes, DerivedAnalysisSettings derived, out ProfileHeader header)
    {
        var raw = SongProfileSerializer.Read(bytes, out header, out var tags);
        var id = new SongAudioIdentity(SongAudioIdentityKind.SourceAsset, header.songId, header.audioIdentity);
        var p = SongProfileFactory.Create(raw, tags, header.tagModelVersion, id, header.paramsHash, derived);
        p.pipelineVersion = header.pipelineVersion;
        p.profileCrc = header.bodyCrc32;
        p.playbackRevision = header.playbackRevision;
        return p;
    }
}
