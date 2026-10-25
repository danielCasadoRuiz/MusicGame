using UnityEngine;

/// <summary>
/// The ONE place the game's time domains are converted into each other. Every consumer that needs
/// to go from one domain to another calls these helpers instead of hand-writing "± warmupTime".
///
///   Clip time  (c) — seconds into the AudioClip, no warmup. The domain of SongProfile (every
///                    envelope, onsetTimes, impactTimes…) and of AudioSource.timeSamples/frequency.
///                    Negative during a countdown that starts at clip 0 (the song hasn't started).
///   Song time  (S) — MusicClock.SongTime: warmupTime + clip time once the audio plays; during the
///                    warmup countdown it runs from the run's start offset up to warmup + start.
///   Event time     — TimelineEvent/MacroEvent.eventTime, the SongTime at which a musical moment
///                    happens: warmupTime + the moment's clip onset (same domain as S).
///   Distance       — path arc-length: SongTime × unitsPerSecond (MusicClock.MusicDistance).
///
/// Pure + static (no Unity state), so edit-mode tests cover the conversions directly.
/// </summary>
public static class MusicTime
{
    /// <summary>Song time → clip time (S − warmup).</summary>
    public static float SongToClip(float songTime, float warmupTime) => songTime - warmupTime;

    /// <summary>Clip time → song time (warmup + c).</summary>
    public static float ClipToSong(float clipTime, float warmupTime) => warmupTime + clipTime;

    /// <summary>A SongProfile onset (clip time) → its TimelineEvent/MacroEvent eventTime. Same
    /// formula as ClipToSong, named for what the caller means.</summary>
    public static float OnsetToEventTime(float onsetClipTime, float warmupTime) => warmupTime + onsetClipTime;

    /// <summary>Song time → path distance.</summary>
    public static float SongToDistance(float songTime, float unitsPerSecond) => songTime * unitsPerSecond;

    /// <summary>Path distance → song time (0 when the speed is not positive).</summary>
    public static float DistanceToSong(float distance, float unitsPerSecond) =>
        unitsPerSecond > 0f ? distance / unitsPerSecond : 0f;

    /// <summary>AudioSource.timeSamples → clip seconds, in double precision (a float loses
    /// sample resolution after a few minutes at 48 kHz).</summary>
    public static double SamplesToSeconds(long samples, int frequency) =>
        frequency > 0 ? (double)samples / frequency : 0.0;

    /// <summary>Clip seconds → a sample index clamped inside the clip (0 .. totalSamples − 1).</summary>
    public static int SecondsToSampleIndex(double seconds, int frequency, int totalSamples)
    {
        if (frequency <= 0 || totalSamples <= 0) return 0;
        long s = (long)System.Math.Round(seconds * frequency);
        return (int)System.Math.Max(0L, System.Math.Min(totalSamples - 1L, s));
    }

    // ── Cursor helpers (seek / respawn / restart / mid-song start) ────────────────────────────
    // A cursor placed with these never fires a moment that lies before `songTime`: the activation
    // loops in GameplayManager only move cursors forward, so fast-forwarding them here is what
    // keeps a mid-song start or a seek from bursting every earlier event in one frame.

    /// <summary>First timeline event whose eventTime is at or after songTime (events.Length if none).</summary>
    public static int FirstEventAtOrAfter(TimelineEvent[] events, float songTime)
    {
        if (events == null) return 0;
        for (int i = 0; i < events.Length; i++)
            if (events[i].eventTime >= songTime) return i;
        return events.Length;
    }

    /// <summary>First macro event whose eventTime is at or after songTime (events.Length if none).</summary>
    public static int FirstMacroAtOrAfter(MacroEvent[] events, float songTime)
    {
        if (events == null) return 0;
        for (int i = 0; i < events.Length; i++)
            if (events[i].eventTime >= songTime) return i;
        return events.Length;
    }

    /// <summary>Clamps a requested clip position into [0, clipLength] (clipLength ≤ 0 = unknown, only
    /// the lower bound applies).</summary>
    public static float ClampClipTime(float clipTime, float clipLength) =>
        clipLength > 0f ? Mathf.Clamp(clipTime, 0f, clipLength) : Mathf.Max(0f, clipTime);
}
