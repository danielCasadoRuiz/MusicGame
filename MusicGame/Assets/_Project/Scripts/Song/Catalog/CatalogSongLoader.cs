using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using MusicGame.Audio.Analysis;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Debug = UnityEngine.Debug;

/// <summary>
/// Ownership of one published catalog song's PLAYBACK audio (the Addressables handle of its AudioClip).
/// Created by CatalogSongLoader, adopted by GameSession for the CURRENT selection (GameSession.SongContent)
/// and released by GameSession only — when a different song (or an imported file) becomes the selection,
/// or the session ends. Runner → Fight → Fight Again / Replay keep it. The song preview holds its own
/// handle (Addressables ref-counts), so stopping a preview never unloads the gameplay clip, and releasing
/// this lease never stops a preview. Release is idempotent.
/// </summary>
public sealed class SongContentLease
{
    public readonly string    SongId;
    public readonly AudioClip Clip;
    public readonly ProfileHeader Header;
    private AsyncOperationHandle<AudioClip> _handle;

    /// <summary>Diagnostics: live leases in the process (probes check that superseded ones are released).</summary>
    public static int LiveCount { get; private set; }

    public bool IsReleased => !_handle.IsValid();

    public SongContentLease(string songId, AsyncOperationHandle<AudioClip> handle, ProfileHeader header)
    {
        SongId = songId; _handle = handle; Clip = handle.Result; Header = header;
        LiveCount++;
    }

    public void Release()
    {
        if (!_handle.IsValid()) return;
        Addressables.Release(_handle);
        _handle = default;
        LiveCount--;
        Debug.Log($"[SongContentLease] Released the playback audio of '{SongId}'.");
    }
}

/// <summary>Separate wall times (ms) of one catalog preparation + sizes — measured where the work happens.</summary>
public sealed class CatalogLoadTimings
{
    public double indexMs, downloadSizeMs, downloadMs, loadMs, validateMs, profileBuildMs, audioPrepareMs, totalMs, maxMainThreadMs;
    public long   downloadBytes, profileBytes;
    public long   clipRuntimeBytes;
    public override string ToString() =>
        $"total {totalMs:F0} ms | index {indexMs:F0} | download {downloadMs:F0} ({downloadBytes / 1048576.0:F2} MB, size query {downloadSizeMs:F0}) | " +
        $"load {loadMs:F0} | validate {validateMs:F1} | profile build {profileBuildMs:F0} (worker, {profileBytes / 1048576.0:F2} MB) | " +
        $"audio prepare {audioPrepareMs:F0} (clip runtime {clipRuntimeBytes / 1048576.0:F2} MB) | max main-thread step {maxMainThreadMs:F1}";
}

/// <summary>
/// Prepares ONE published catalog song for the current selection — the content half of the readiness
/// barrier (SongPreparation). No FFT, no inference, no analysis cache:
///   ResolvingSelection  published index (SongCatalogService) → this song's entry (a copy);
///   Downloading         ContentDownloadManager.EnsureContentAvailableAsync("Song_&lt;id&gt;") — audio + profile
///                       bundles; real byte progress; nothing to do when cached;
///   LoadingContent      audio clip + profile TextAsset loaded IN PARALLEL;
///   ValidatingProfile   header vs index vs app versions vs loaded clip (PublishedProfileCheck) — the profile
///                       TextAsset handle is released as soon as its bytes are copied;
///   BuildingProfile     SongAnalysisPipeline.LoadProfile on a WORKER (raw arrays + the light derived pass);
///   PreparingAudio      the clip's data loaded (loadState Loaded) under its own load type.
/// Identity: bound to (songId, GameSession.SelectionGeneration); every step re-checks it, and a stale or
/// cancelled run releases ONLY the handles it acquired and publishes nothing (no profile, no theme, no
/// "ready"). On success the caller hands <see cref="Lease"/> to GameSession.AdoptCatalogContent.
/// Errors are typed (SongContentError) — download vs load vs incompatible content — and never trigger an
/// analysis. Every wait has a bound (stall/timeout), so nothing waits forever.
/// </summary>
public sealed class CatalogSongLoader
{
    public const float LoadTimeoutSeconds = 60f;
    public const float DownloadStallSeconds = 30f;

    public readonly string SongId;
    public readonly int    Generation;
    private readonly AudioAnalysisConfig _config;
    private bool _cancelled;

    private AsyncOperationHandle<AudioClip> _audio;
    private AsyncOperationHandle<TextAsset> _profileAsset;

    public SongContentError Error { get; private set; }
    public string           ErrorDetail { get; private set; }
    public SongContentLease Lease { get; private set; }
    public SongProfile      Profile { get; private set; }
    public ProfileHeader    Header { get; private set; }
    public SongCatalogIndexEntry Entry { get; private set; }
    public CatalogLoadTimings Timings { get; } = new();
    public bool Succeeded => Lease != null && Profile != null && Error == SongContentError.None;

    /// <summary>Diagnostics: how many loaders ran their content stages (probes compare with analysis counters).</summary>
    public static int RunCount { get; private set; }

    public CatalogSongLoader(string songId, int generation, AudioAnalysisConfig config)
    {
        SongId = songId; Generation = generation; _config = config;
    }

    /// <summary>The request may still act: not cancelled, and still the current selection.</summary>
    public bool IsCurrent => !_cancelled && GameSession.Instance != null && GameSession.Instance.SelectionGeneration == Generation;

    public void Cancel() => _cancelled = true;

    /// <summary>Cancel + release now what this request acquired and nobody adopted (used when the driving
    /// coroutine is stopped, so <see cref="Run"/>'s own cleanup will never execute). Never touches content
    /// GameSession owns.</summary>
    public void Abort()
    {
        _cancelled = true;
        if (_profileAsset.IsValid()) { Addressables.Release(_profileAsset); _profileAsset = default; }
        if (_audio.IsValid()) { Addressables.Release(_audio); _audio = default; }
        if (Lease != null && (GameSession.Instance == null || GameSession.Instance.SongContent != Lease)) Lease.Release();
    }

    private static double Ms(long t0) => (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;

    private void Stage(SongPreparationStage s, string detail = null, float progress = -1f, long bytes = 0)
    {
        if (IsCurrent) SongPreparation.Enter(Generation, s, detail, progress, bytes);
    }

    /// <summary>Main-thread coroutine. Ends with Succeeded, or Error set (Cancelled when superseded).</summary>
    public IEnumerator Run()
    {
        RunCount++;
        long tTotal = Stopwatch.GetTimestamp();
        try
        {
            // ── Selection → published entry ────────────────────────────────────────────
            Stage(SongPreparationStage.ResolvingSelection, SongId);
            long t = Stopwatch.GetTimestamp();
            yield return SongCatalogService.EnsureReady();
            Timings.indexMs = Ms(t);
            if (!IsCurrent) { Fail(SongContentError.Cancelled, "superseded"); yield break; }
            if (!SongCatalogService.IndexAvailable) { Fail(SongContentError.IndexUnavailable, SongCatalogService.LastError); yield break; }
            if (!SongCatalogService.TryGetEntry(SongId, out var entry)) { Fail(SongContentError.IncompatibleContent, $"'{SongId}' is not in the published index"); yield break; }
            Entry = entry;
            if (_config == null) { Fail(SongContentError.IncompatibleContent, "no AudioAnalysisConfig (AppConfig.audioAnalysis)"); yield break; }
            var app = AppAnalysisVersions.Current(_config);
            var derived = new DerivedAnalysisSettings(_config);

            // ── Download (audio + profile bundles of this song) ───────────────────────
            var cdm = ContentDownloadManager.Instance;
            if (cdm != null)
            {
                t = Stopwatch.GetTimestamp();
                var sizeTask = cdm.GetDownloadSizeAsync(entry.contentLabel);
                while (!sizeTask.IsCompleted) { if (!IsCurrent) { Fail(SongContentError.Cancelled, "superseded"); yield break; } yield return null; }
                Timings.downloadSizeMs = Ms(t);
                long bytes = sizeTask.Status == TaskStatus.RanToCompletion ? sizeTask.Result : -1;
                if (bytes != 0)
                {
                    Timings.downloadBytes = Math.Max(0, bytes);
                    Stage(SongPreparationStage.Downloading, $"{bytes / 1048576.0:F1} MB", 0f, bytes);
                    t = Stopwatch.GetTimestamp();
                    float progress = 0f, lastProgress = -1f;
                    double lastChange = Time.realtimeSinceStartupAsDouble;
                    // The shared download task is NOT bound to this request's cancellation (another request for
                    // the same song may join it); a cancelled request just stops waiting.
                    var download = cdm.EnsureContentAvailableAsync(entry.contentLabel, new Progress(p => progress = p));
                    while (!download.IsCompleted)
                    {
                        if (!IsCurrent) { Fail(SongContentError.Cancelled, "superseded"); yield break; }
                        if (progress > lastProgress + 1e-4f)
                        {
                            lastProgress = progress;
                            lastChange = Time.realtimeSinceStartupAsDouble;
                            Stage(SongPreparationStage.Downloading, null, progress, bytes);
                        }
                        else if (Time.realtimeSinceStartupAsDouble - lastChange > DownloadStallSeconds)
                        { Fail(SongContentError.DownloadFailed, $"no download progress for {DownloadStallSeconds:F0} s"); yield break; }
                        yield return null;
                    }
                    Timings.downloadMs = Ms(t);
                    if (download.Status != TaskStatus.RanToCompletion || !download.Result)
                    { Fail(SongContentError.DownloadFailed, $"'{entry.contentLabel}' could not be downloaded"); yield break; }
                }
            }

            // ── Load audio + profile in parallel ───────────────────────────────────────
            Stage(SongPreparationStage.LoadingContent);
            t = Stopwatch.GetTimestamp();
            _audio = Addressables.LoadAssetAsync<AudioClip>(entry.audioAddress);
            _profileAsset = Addressables.LoadAssetAsync<TextAsset>(entry.profileAddress);
            double deadline = Time.realtimeSinceStartupAsDouble + LoadTimeoutSeconds;
            while (!_audio.IsDone || !_profileAsset.IsDone)
            {
                if (!IsCurrent) { Fail(SongContentError.Cancelled, "superseded"); yield break; }
                if (Time.realtimeSinceStartupAsDouble > deadline) { Fail(SongContentError.LoadFailed, $"content not loaded within {LoadTimeoutSeconds:F0} s"); yield break; }
                yield return null;
            }
            Timings.loadMs = Ms(t);
            if (_profileAsset.Status != AsyncOperationStatus.Succeeded || _profileAsset.Result == null)
            { Fail(SongContentError.IncompatibleContent, $"published profile '{entry.profileAddress}' missing ({_profileAsset.OperationException?.Message})"); yield break; }
            if (_audio.Status != AsyncOperationStatus.Succeeded || _audio.Result == null)
            { Fail(SongContentError.LoadFailed, $"audio '{entry.audioAddress}' failed to load ({_audio.OperationException?.Message})"); yield break; }

            // ── Validate (header only, cheap) ──────────────────────────────────────────
            Stage(SongPreparationStage.ValidatingProfile);
            t = Stopwatch.GetTimestamp();
            byte[] bytesCopy = _profileAsset.Result.bytes; // a copy: the profile asset can go right away
            Addressables.Release(_profileAsset);
            _profileAsset = default;
            Timings.profileBytes = bytesCopy.Length;
            ProfileHeader header;
            try { header = ProfileBinary.ReadHeader(bytesCopy, out _); }
            catch (Exception e) { Fail(SongContentError.IncompatibleContent, "unreadable profile: " + e.Message); yield break; }
            var clip = _audio.Result;
            string problem = PublishedProfileCheck.Validate(header, entry, app)
                             ?? PublishedProfileCheck.ValidateClip(header, clip.samples, clip.channels, clip.frequency);
            Timings.validateMs = Ms(t);
            Timings.maxMainThreadMs = Math.Max(Timings.maxMainThreadMs, Timings.validateMs);
            if (problem != null) { Fail(SongContentError.IncompatibleContent, problem); yield break; }
            Header = header;

            // ── Profile (raw + derived) on a worker ────────────────────────────────────
            Stage(SongPreparationStage.BuildingProfile);
            t = Stopwatch.GetTimestamp();
            SongProfile profile = null;
            Exception buildError = null;
            var build = Task.Run(() =>
            {
                try { profile = SongAnalysisPipeline.LoadProfile(bytesCopy, derived, out _); }
                catch (Exception e) { buildError = e; }
            });
            while (!build.IsCompleted) { if (!IsCurrent) { Fail(SongContentError.Cancelled, "superseded"); yield break; } yield return null; }
            Timings.profileBuildMs = Ms(t);
            if (buildError != null || profile == null) { Fail(SongContentError.IncompatibleContent, "corrupt profile: " + buildError?.Message); yield break; }

            // ── Audio ready to play (its own load type: decompressed / compressed in memory / streamed) ──
            Stage(SongPreparationStage.PreparingAudio, clip.loadType.ToString());
            t = Stopwatch.GetTimestamp();
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            deadline = Time.realtimeSinceStartupAsDouble + LoadTimeoutSeconds;
            while (clip.loadState != AudioDataLoadState.Loaded)
            {
                if (!IsCurrent) { Fail(SongContentError.Cancelled, "superseded"); yield break; }
                if (clip.loadState == AudioDataLoadState.Failed) { Fail(SongContentError.LoadFailed, $"audio data of '{entry.audioAddress}' failed to load"); yield break; }
                if (Time.realtimeSinceStartupAsDouble > deadline) { Fail(SongContentError.LoadFailed, $"audio data not ready within {LoadTimeoutSeconds:F0} s"); yield break; }
                yield return null;
            }
            Timings.audioPrepareMs = Ms(t);
            Timings.clipRuntimeBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(clip);

            if (!IsCurrent) { Fail(SongContentError.Cancelled, "superseded"); yield break; }
            Profile = profile;
            Lease = new SongContentLease(SongId, _audio, header); // ownership moves to the lease
            _audio = default;
            Timings.totalMs = Ms(tTotal);
            Stage(SongPreparationStage.ContentReady, $"{Timings.totalMs:F0} ms");
            Debug.Log($"[CatalogSongLoader] '{SongId}' #{Generation} ready — no analysis ran. {Timings}");
        }
        finally
        {
            // Whatever was acquired and not handed over is released here (cancel, error, stale).
            if (_profileAsset.IsValid()) { Addressables.Release(_profileAsset); _profileAsset = default; }
            if (_audio.IsValid()) { Addressables.Release(_audio); _audio = default; }
        }
    }

    private void Fail(SongContentError error, string detail)
    {
        Error = error;
        ErrorDetail = detail;
        if (error == SongContentError.Cancelled) { Debug.Log($"[CatalogSongLoader] '{SongId}' #{Generation} cancelled ({detail}) — handles released, nothing published."); return; }
        // The driving controller reports the failure (SongPreparation Failed + recovery): one report only.
        Debug.LogWarning($"[CatalogSongLoader] '{SongId}' #{Generation} {error}: {detail}");
    }

    // IProgress that just stores the value (reported on the main thread by ContentDownloadManager's loop).
    private sealed class Progress : IProgress<float>
    {
        private readonly Action<float> _set;
        public Progress(Action<float> set) => _set = set;
        public void Report(float value) => _set(value);
    }
}
