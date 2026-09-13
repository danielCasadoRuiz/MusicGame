using UnityEngine;

/// <summary>
/// The Music Runner's scoring/rating system: base points, rarity weighting, timing quality,
/// fall score-protection/penalty, and the end-screen display rating curve. Split out of the old
/// monolithic GameplayConfig — see MusicRunnerGameplayConfig for the full picture.
/// </summary>
[CreateAssetMenu(fileName = "MusicRunnerScoringConfig", menuName = "MusicGame/MusicRunner/Scoring Config")]
public class MusicRunnerScoringConfig : ScriptableObject
{
    // ── Scoring ───────────────────────────────────────────────────────────────
    [Header("Scoring")]
    [Tooltip("Points awarded for collecting the single Peak ring placed at the song's most intense moment")]
    public int peakBonusPoints = 500;
    [Tooltip("Points awarded for collecting an Impact ring (strong hit/drop moment)")]
    public int impactBonusPoints = 300;

    [Tooltip("Base points for Kick/Snare/HiHat/Beat/Onset before the rarity and timing " +
             "multipliers are applied.")]
    public float baseScorePerRing = 10f;
    [Tooltip("Output range of the rarity multiplier (X = most common type in this song's " +
             "final timeline, Y = rarest). 1.0 = exactly at the median.")]
    public Vector2 rarityMultiplierRange = new Vector2(0.85f, 1.15f);
    [Tooltip("How many natural-log units of count-vs-median ratio it takes to reach the " +
             "extreme of rarityMultiplierRange. 1.5 ≈ a type needs to appear ~4.5x more or " +
             "less than the median type to hit the cap. Higher = flatter/harder to reach the extremes.")]
    public float rarityLogSpread = 1.5f;

    [Tooltip("Seconds of song-time timing error beyond which timing stops mattering any further " +
             "(the floor value on timingQualityCurve applies from here on). Also defines the " +
             "curve's x-axis scale: fraction = timingError / maxUsefulTimingWindow.")]
    public float maxUsefulTimingWindow = 0.8f;
    [Tooltip("X = |collectionSongTime - ring's eventTime| / maxUsefulTimingWindow (0..1). " +
             "Y = fraction of (rarity-adjusted) points awarded. Default: flat 100% up to ~0.12s " +
             "off, then eases down to a 55% floor by maxUsefulTimingWindow — never zero, a late/" +
             "early pickup still counts, just for less.")]
    public AnimationCurve timingQualityCurve = new AnimationCurve(
        new Keyframe(0f,    1.00f),
        new Keyframe(0.15f, 1.00f),
        new Keyframe(1f,    0.55f));

    [Tooltip("Reshapes the raw NormalizedScore (X, 0..1 — the SAME value used internally for " +
             "scoring/session accumulation, left completely unchanged) into a DISPLAY rating " +
             "(Y, 0..1) for the end screen ONLY — a presentation layer, not a new scoring system. " +
             "Given how demanding this game is, a raw ~25% can already be a genuinely good run; " +
             "this is what lets the SHOWN percentage/label reflect that instead of reading like " +
             "a school grade. Default: ~5%→POBRE, ~15%→FLUIX, ~25%→DECENT, ~35%→MOLT BO, ~40%+→BRUTAL.")]
    public AnimationCurve performanceRatingCurve = new AnimationCurve(
        new Keyframe(0f,    0f),
        new Keyframe(0.05f, 0.15f),
        new Keyframe(0.15f, 0.35f),
        new Keyframe(0.25f, 0.55f),
        new Keyframe(0.35f, 0.80f),
        new Keyframe(0.40f, 0.95f),
        new Keyframe(1f,    1f));

    // ── Fall / Respawn — Score protection & penalty ───────────────────────────
    // No discrete checkpoints anymore — a fall respawns the player at the EXACT songTime they
    // fell at (see FallRespawnSystem), and score protection is a CONTINUOUS function of song
    // progress instead of "everything since the last checkpoint". At the moment of a fall:
    //   fallProgress      = (fallSongTime - warmupTime) / song duration      — 0..1
    //   protectedProgress = fallProtectionCurve.Evaluate(fallProgress)       — 0..1, always <= fallProgress
    // Every individual pickup's own (songTime, points) is kept (GameplayManager._pickupHistory,
    // per type, never cleared mid-run) — pickups collected AFTER protectedProgress's song time
    // are "at risk"; everything before it is permanently safe. fallPenaltyCurve (below, UNCHANGED
    // mechanism) then removes a FRACTION of just that at-risk pool — never a flat percentage of
    // the whole accumulated score, so a fall near the end of a song can't wipe out a run's worth
    // of progress the way a naive "% of total" penalty would.
    [Header("Fall / Respawn — Score Protection")]
    [Tooltip("X = fallProgress (0..1, song progress at the moment of the fall). Y = " +
             "protectedProgress (0..1, always <= X) — how far into the song points are already " +
             "permanently safe. Points collected between Y and X are what the penalty below can " +
             "actually remove. Example: fallProgress=0.70 -> protectedProgress=0.55 means only " +
             "points collected between 55% and 70% of the song are at risk.")]
    public AnimationCurve fallProtectionCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.7f, 0.55f), new Keyframe(1f, 0.82f));

    [Header("Fall / Respawn — Penalty")]
    [Tooltip("X = fallProgress (0..1). Y = fraction of the AT-RISK pool (see " +
             "fallProtectionCurve above) lost — never a fraction of the whole score. Default: " +
             "~60% early in the song, ~30% near the end — a mistake late in an otherwise-good " +
             "run costs less.")]
    public AnimationCurve fallPenaltyCurve = AnimationCurve.Linear(0f, 0.6f, 1f, 0.3f);
    [Tooltip("Final score multiplier applied only if the run ends with zero falls " +
             "(fallCount == 0). 1.20 = +20% for a perfectly clean run. Not applied at all if " +
             "even one fall occurred.")]
    public float noFallScoreMultiplier = 1.2f;
}
