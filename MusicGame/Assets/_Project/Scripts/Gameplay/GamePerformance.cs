using System.Collections.Generic;

/// <summary>
/// Per-type breakdown of how a run went, built strictly from the actual PLAYABLE Timeline for
/// this song (GameplayTimeline.Events) — never from the raw SongProfile. A type only appears
/// in ByType if this song's generated timeline actually contains at least one collectible of
/// that type (Available > 0); Drop/BuildupStart (pure MacroEvents, never collectibles) can
/// never appear here at all, since they have no RingType and never enter Events.
///
/// CollectionRate and NormalizedScore are deliberately different numbers: 10/10 collected with
/// mediocre timing gives CollectionRate = 1.0 but NormalizedScore well below 1.0 — the former
/// measures "did you reach it", the latter "how well did you play it".
/// </summary>
public class TypePerformance
{
    public RingType Type;

    public int Available;  // how many of this type this song's ACTUAL timeline generated
    public int Collected;  // physically picked up over the run — NEVER decremented by fall penalty
                            // (that's a score mechanic; this is "did you reach it")
    public int Missed => Available - Collected;

    // Collected / Available. 0 if Available == 0 (see HasData).
    public float CollectionRate;

    public int   EarnedScore;       // sum of points actually earned from collected pickups of this type
    public int   MaxPossibleScore;  // sum of ScoreFor(type, perfectTiming) over every Available event
    public float NormalizedScore;   // EarnedScore / MaxPossibleScore, 0 if MaxPossibleScore == 0

    public float AverageTimingError; // seconds, mean |actual-expected| over COLLECTED pickups of this type
    public float TimingAccuracy;     // mean TimingMultiplier actually achieved over collected pickups, 0..1

    /// <summary>False means Available == 0 — this song simply never generated this type, which
    /// is NOT the same as "the player failed every one". Never read CollectionRate/
    /// NormalizedScore as "0 = bad" when HasData is false.</summary>
    public bool HasData => Available > 0;
}

/// <summary>
/// The full performance profile for one run, carried in GameEndedEvent so the next system can
/// consume it without reaching back into GameplayManager.
/// </summary>
public class GamePerformance
{
    public float OverallNormalizedScore;
    public int   OverallEarnedScore;
    public int   OverallMaxPossibleScore;

    public Dictionary<RingType, TypePerformance> ByType = new();
}
