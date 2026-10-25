using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// INSTRUMENTATION of the open "pulse executed 128 ms late" issue (P2B run 1, not reproduced since). Records,
/// for every musical pulse GameplayManager fires: the expected musical time (eventTime − pulseLeadTime), the
/// actual SongTime when it fired, the frame's real duration, the clock's own state/correction (snap or
/// eased drift), and the concurrent operations at that moment (scene loads, Addressables downloads, an
/// analysis running, deferred Sentis disposals, GC collections in that frame, time since playback started).
/// A pulse later than <see cref="OutlierSeconds"/> is logged with that context.
///
/// Diagnostics ONLY: it changes no timing, adds no offset and does not move any test limit. The lateness of
/// a frame-driven pulse is bounded below by nothing and above by the frame that processed it — this records
/// which of those frames were long and what was running then, so the cause can be attributed with evidence.
/// </summary>
public static class PulseTimingDiagnostics
{
    public const float OutlierSeconds = 0.05f;
    private const int Capacity = 4096;

    public struct Sample
    {
        public int    EventIndex;
        public float  ExpectedSongTime;   // eventTime − lead
        public float  ActualSongTime;     // clock SongTime when fired
        public float  LateSeconds;        // actual − expected (≥ 0 by construction of the trigger)
        public float  FrameSeconds;       // Time.unscaledDeltaTime of that frame
        public float  ClockCorrection;    // MusicClock.LastCorrection of that frame
        public bool   ClockSnapped;
        public MusicClockState ClockState;
        public float  SincePlaybackSeconds;
        public int    GcCollectionsInFrame;
        public string Concurrent;         // non-empty only for outliers (allocation-free on the normal path)
    }

    private static readonly List<Sample> s_samples = new(Capacity);
    private static double s_playbackStartedAt = -1;
    private static int    s_lastGcCount, s_lastFrame = -1, s_gcInFrame;

    public static IReadOnlyList<Sample> Samples => s_samples;
    public static int OutlierCount { get; private set; }

    /// <summary>A new run's pulses start (GameStartedEvent): clears the previous run.</summary>
    public static void BeginRun()
    {
        s_samples.Clear();
        OutlierCount = 0;
        s_playbackStartedAt = -1;
        s_lastGcCount = System.GC.CollectionCount(0);
    }

    public static void MarkPlaybackStarted()
    {
        s_playbackStartedAt = Time.realtimeSinceStartupAsDouble;
        Debug.Log($"[PulseTiming] Playback started — concurrent at that instant: {Concurrent()}");
    }

    public static void Record(int eventIndex, float expectedSongTime, MusicClock clock)
    {
        if (clock == null) return;
        if (s_lastFrame != Time.frameCount)
        {
            int gc = System.GC.CollectionCount(0);
            s_gcInFrame = gc - s_lastGcCount;
            s_lastGcCount = gc;
            s_lastFrame = Time.frameCount;
        }
        var sample = new Sample
        {
            EventIndex = eventIndex,
            ExpectedSongTime = expectedSongTime,
            ActualSongTime = clock.SongTime,
            LateSeconds = clock.SongTime - expectedSongTime,
            FrameSeconds = Time.unscaledDeltaTime,
            ClockCorrection = clock.LastCorrection,
            ClockSnapped = clock.LastTickSnapped,
            ClockState = clock.State,
            SincePlaybackSeconds = s_playbackStartedAt >= 0 ? (float)(Time.realtimeSinceStartupAsDouble - s_playbackStartedAt) : -1f,
            GcCollectionsInFrame = s_gcInFrame,
        };
        if (sample.LateSeconds > OutlierSeconds)
        {
            sample.Concurrent = Concurrent();
            OutlierCount++;
            Debug.LogWarning($"[PulseTiming] LATE pulse #{eventIndex}: expected {expectedSongTime:F3}, fired at {sample.ActualSongTime:F3} " +
                             $"(+{sample.LateSeconds * 1000f:F0} ms) | frame {sample.FrameSeconds * 1000f:F1} ms | clock {sample.ClockState} " +
                             $"correction {sample.ClockCorrection * 1000f:F1} ms{(sample.ClockSnapped ? " SNAP" : "")} | " +
                             $"{sample.SincePlaybackSeconds:F2} s after playback start | GC0 in frame {sample.GcCollectionsInFrame} | {sample.Concurrent}");
        }
        if (s_samples.Count < Capacity) s_samples.Add(sample);
    }

    /// <summary>What else was in flight (cheap snapshot, only built for outliers / milestones).</summary>
    public static string Concurrent()
    {
        var sb = new StringBuilder();
        var scenes = UnityEngine.SceneManagement.SceneManager.sceneCount;
        sb.Append($"scenes {scenes}");
        if (SongPreparation.Current != SongPreparationStage.Ready && SongPreparation.Current != SongPreparationStage.None)
            sb.Append($", preparation {SongPreparation.Current}");
        var cdm = ContentDownloadManager.Instance;
        if (cdm != null && cdm.InFlightCount > 0) sb.Append($", downloads {cdm.InFlightCount}");
        if (SentisMusicTagger.DeferredCount > 0) sb.Append($", sentis deferred {SentisMusicTagger.DeferredCount}");
        if (SongAnalysisJob.ActiveCount > 0) sb.Append($", analyses {SongAnalysisJob.ActiveCount}");
        sb.Append($", gc0 total {System.GC.CollectionCount(0)}");
        return sb.ToString();
    }

    public static string Summary()
    {
        if (s_samples.Count == 0) return "no pulses recorded";
        float worst = 0f, sumLate = 0f, worstFrame = 0f;
        int worstIdx = -1;
        foreach (var s in s_samples)
        {
            sumLate += s.LateSeconds;
            if (s.LateSeconds > worst) { worst = s.LateSeconds; worstIdx = s.EventIndex; }
            if (s.FrameSeconds > worstFrame) worstFrame = s.FrameSeconds;
        }
        return $"{s_samples.Count} pulses, mean late {sumLate / s_samples.Count * 1000f:F1} ms, worst {worst * 1000f:F0} ms (event #{worstIdx}), " +
               $"worst frame {worstFrame * 1000f:F1} ms, outliers > {OutlierSeconds * 1000f:F0} ms: {OutlierCount}";
    }
}
