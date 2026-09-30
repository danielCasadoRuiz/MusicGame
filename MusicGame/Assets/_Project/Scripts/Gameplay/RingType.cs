/// <summary>
/// The one canonical pickup type for the Runner. Two logically separate families share it (one
/// enum, so pooling/spawning/collection/events stay a single pipeline):
///   MUSICAL  — Beat, Kick, Snare, HiHat, Onset, Peak, Impact: generated from the song analysis;
///              they score (TotalScore), count toward per-type performance and feed the combat
///              build conversion.
///   RESOURCE — Life, Special: combat resources carried into the Fight; they never score, never
///              enter musical performance and never form pickup combos (see RingTypes).
/// New values are appended (never inserted) so serialized ints stay stable.
/// </summary>
public enum RingType { Beat, Kick, Snare, HiHat, Onset, Peak, Impact, Life, Special }

/// <summary>Family helpers for RingType — the single place that decides "is this musical?".</summary>
public static class RingTypes
{
    /// <summary>Display/iteration order for musical pickups (HUD, results screen, performance).</summary>
    public static readonly RingType[] Musical =
        { RingType.Kick, RingType.Snare, RingType.HiHat, RingType.Beat, RingType.Onset, RingType.Impact, RingType.Peak };

    public static readonly RingType[] Resources = { RingType.Life, RingType.Special };

    public static bool IsResource(RingType type) => type == RingType.Life || type == RingType.Special;

    public static bool IsMusical(RingType type) => !IsResource(type);
}
