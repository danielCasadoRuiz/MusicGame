using UnityEngine;

/// <summary>
/// Single source of truth for "which window of the (always fully-analyzed) song actually gets
/// played" — shared by GameplayManager (audio start/stop, player spawn distance, song-end/fade/
/// farewell timing) and MusicWorldManager (terrain fade-to-flat near the end) so the two can never
/// resolve a different start/end than each other. See MusicRunnerCoreConfig.useManualPlayRange's
/// own doc for what the range actually means.
/// </summary>
public static class PlayRangeResolver
{
    public readonly struct Range
    {
        public readonly float Start;
        public readonly float End;
        public Range(float start, float end) { Start = start; End = end; }
    }

    public static Range Resolve(MusicRunnerCoreConfig core, float duration)
    {
        if (core == null || !core.useManualPlayRange) return new Range(0f, duration);

        float start = Mathf.Clamp(core.manualPlayRangeStartSeconds, 0f, duration);
        float end   = core.manualPlayRangeEndSeconds > 0f
            ? Mathf.Clamp(core.manualPlayRangeEndSeconds, start + 1f, duration)
            : duration;
        return new Range(start, end);
    }
}
