using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Explicit states of the readiness barrier between "Play" and the first playable instant (P3). Each is a
/// REAL completion (a handle done, a worker finished, a scene object built), never a timer. Order of the
/// mandatory stages of a catalog song:
///   ResolvingSelection → Downloading → LoadingContent → ValidatingProfile → BuildingProfile → PreparingAudio
///   → (ContentReady) → ResolvingStyle → ApplyingTheme → LoadingScene → PreparingWorld → PreparingConsumers → Ready.
/// An imported file replaces the content stages by Analysing. Ready is the ONLY state that allows the
/// countdown (GameStartedEvent, MusicClock warmup) and later the music to start.
/// </summary>
public enum SongPreparationStage
{
    None,
    ResolvingSelection,
    Downloading,
    LoadingContent,
    ValidatingProfile,
    BuildingProfile,
    PreparingAudio,
    ContentReady,
    Analysing,
    ResolvingStyle,
    ApplyingTheme,
    LoadingScene,
    PreparingWorld,
    PreparingConsumers,
    Ready,
    Failed,
    Cancelled,
}

/// <summary>Why a catalog song could not be prepared — each has its own recovery (see SongAnalysisController).</summary>
public enum SongContentError
{
    None,
    /// <summary>The published index could not be loaded (offline before the first download): retry later.</summary>
    IndexUnavailable,
    /// <summary>Bundle download failed or stalled (network): retry.</summary>
    DownloadFailed,
    /// <summary>Content present but an asset/clip failed to load or decode (storage, corrupt cache): retry, then clear cache.</summary>
    LoadFailed,
    /// <summary>The published content does not match this app or itself (versions, revisions, missing profile):
    /// a content problem — never covered by a silent analysis.</summary>
    IncompatibleContent,
    Cancelled,
}

/// <summary>Published on every stage change of the CURRENT selection (truthful UI: downloading / loading /
/// preparing). Progress is a real fraction only while downloading (bytes), otherwise −1 (indeterminate).</summary>
public struct SongPreparationEvent
{
    public int                  SelectionGeneration;
    public SongPreparationStage Stage;
    public float                Progress;
    public long                 DownloadBytes;
    public SongContentError     Error;
    public string               Detail;
}

/// <summary>
/// Diagnostics of the barrier: one timestamped stage trace per selection generation (realtime seconds),
/// so download / load / prepare / start durations are measured separately (probes and logs read it).
/// Every transition is published as a SongPreparationEvent. Main thread only.
/// </summary>
public static class SongPreparation
{
    public struct Mark { public SongPreparationStage Stage; public double Time; public string Detail; }

    private static int s_generation = -1;
    private static readonly List<Mark> s_trace = new();

    public static SongPreparationStage Current { get; private set; }
    public static int CurrentGeneration => s_generation;
    public static IReadOnlyList<Mark> Trace => s_trace;

    public static void Enter(int generation, SongPreparationStage stage, string detail = null, float progress = -1f,
                             long bytes = 0, SongContentError error = SongContentError.None)
    {
        if (generation != s_generation) { s_generation = generation; s_trace.Clear(); }
        bool changed = Current != stage || s_trace.Count == 0;
        Current = stage;
        if (changed)
        {
            s_trace.Add(new Mark { Stage = stage, Time = Time.realtimeSinceStartupAsDouble, Detail = detail });
            Debug.Log($"[SongPreparation] #{generation} → {stage}{(detail != null ? " (" + detail + ")" : "")}");
        }
        EventBus.Publish(new SongPreparationEvent
        {
            SelectionGeneration = generation, Stage = stage, Progress = progress, DownloadBytes = bytes, Error = error, Detail = detail,
        });
    }

    /// <summary>Seconds between the first entry of `from` and the first entry of `to` in the current trace (−1 = missing).</summary>
    public static double Between(SongPreparationStage from, SongPreparationStage to)
    {
        double a = -1, b = -1;
        foreach (var m in s_trace)
        {
            if (a < 0 && m.Stage == from) a = m.Time;
            if (b < 0 && m.Stage == to) b = m.Time;
        }
        return a >= 0 && b >= 0 ? b - a : -1;
    }

    public static string Describe()
    {
        var sb = new System.Text.StringBuilder();
        double t0 = s_trace.Count > 0 ? s_trace[0].Time : 0;
        foreach (var m in s_trace) sb.Append($"{m.Stage}@{(m.Time - t0) * 1000:F0}ms ");
        return sb.ToString();
    }
}
