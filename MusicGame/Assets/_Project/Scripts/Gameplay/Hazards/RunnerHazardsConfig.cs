using UnityEngine;

/// <summary>
/// Runner hazards, first playable version: block obstacles (RunnerObstacle prefabs) and ONE latent chaser
/// (RunnerChaser). Placement is deterministic per combination (HazardLayout, seeded from the bonus layout's
/// seed + difficulty) and fits between the bonuses — the bonus layouts themselves are untouched, and this
/// config is deliberately NOT part of their key (BonusLayoutProvider.LayoutConfigHash).
/// Difficulty (the same continuous 0..1 the bonuses use) interpolates every "easy → hard" pair below.
/// The player's movement area (surge ahead / hold back behind, MusicRunnerCoreConfig.maxSurge / maxHoldBack)
/// is the escape margin: nothing here widens it.
/// </summary>
[CreateAssetMenu(fileName = "RunnerHazardsConfig", menuName = "MusicGame/MusicRunner/Hazards Config")]
public class RunnerHazardsConfig : ScriptableObject
{
    [Tooltip("Master switch: no obstacle or chaser at all when off.")]
    public bool enabled = true;

    [Header("Prefabs (built by Tools > MusicGame > Runner > Build Hazard Prefabs)")]
    public RunnerObstacle lowBlockPrefab;
    public RunnerObstacle highBlockPrefab;
    public RunnerObstacle portalPrefab;
    public RunnerObstacle windowWallPrefab;
    public RunnerObstacle crawlWallPrefab;
    public RunnerObstacle uShapePrefab;
    public RunnerChaser   chaserPrefab;

    [Header("Placement")]
    [Tooltip("Seconds of play (after the countdown) before the first hazard.")]
    [Min(0f)] public float firstHazardAfterSeconds = 8f;
    [Tooltip("No hazard in the last seconds of the played range.")]
    [Min(0f)] public float endClearSeconds = 4f;
    [Tooltip("Minimum seconds between two obstacles at difficulty 0 / 1.")]
    [Min(1f)] public float easyObstacleGapSeconds = 11f;
    [Min(1f)] public float hardObstacleGapSeconds = 4f;
    [Tooltip("Extra random part of each gap (fraction of it).")]
    [Range(0f, 1f)] public float gapJitter = 0.5f;
    [Tooltip("No bonus (musical, Life, Special) closer than this to an obstacle's front/back faces (metres along the path).")]
    [Min(0f)] public float bonusClearance = 0.8f;
    [Tooltip("Obstacles need at least this track width (metres) where they stand; narrower spots are skipped.")]
    [Min(2f)] public float minTrackWidth = 4.5f;
    [Tooltip("Metres AHEAD of the musical front at which a hazard is built: the visible reaction time is this / playerSpeed.")]
    [Min(10f)] public float spawnLookAhead = 45f;
    [Tooltip("Metres behind the player at which a passed obstacle is recycled.")]
    [Min(2f)] public float recycleBehind = 15f;

    [Header("Obstacle kinds — difficulty from which each appears")]
    [Range(0f, 1f)] public float lowBlockFrom   = 0f;
    [Range(0f, 1f)] public float highBlockFrom  = 0f;
    [Range(0f, 1f)] public float uShapeFrom     = 0.1f;
    [Range(0f, 1f)] public float portalFrom     = 0.3f;
    [Range(0f, 1f)] public float crawlWallFrom  = 0.45f;
    [Tooltip("Jump + crouch through an elevated window — the most demanding: never in the first matches.")]
    [Range(0f, 1f)] public float windowWallFrom = 0.6f;

    [Header("Chaser")]
    [Tooltip("Seconds of play before the first chaser can appear (difficulty 0 / 1).")]
    [Min(0f)] public float firstChaserAfterSeconds = 25f;
    [Min(0f)] public float hardFirstChaserAfterSeconds = 8f;
    [Tooltip("How strongly the harder obstacle kinds are preferred as the difficulty rises (0 = every unlocked kind equally likely).")]
    [Min(0f)] public float hardKindPreference = 4f;
    [Tooltip("Minimum seconds between two chasers at difficulty 0 / 1 (only one is ever active).")]
    [Min(5f)] public float easyChaserGapSeconds = 35f;
    [Min(5f)] public float hardChaserGapSeconds = 15f;
    [Tooltip("Metres before the sleeping chaser at which it wakes up.")]
    [Min(2f)] public float chaserActivationDistance = 9f;
    [Tooltip("Seconds of the wake-up warning (difficulty 0 / 1).")]
    [Min(0.1f)] public float easyChaserWarning = 0.9f;
    [Min(0.1f)] public float hardChaserWarning = 0.5f;
    [Tooltip("Speed above the music's pace the chaser accelerates to (m/s, difficulty 0 / 1).")]
    [Min(0f)] public float easyChaserCatchUpSpeed = 1.2f;
    [Min(0f)] public float hardChaserCatchUpSpeed = 3.5f;
    [Tooltip("Acceleration toward that speed (m/s², difficulty 0 / 1).")]
    [Min(0.1f)] public float easyChaserAcceleration = 1.2f;
    [Min(0.1f)] public float hardChaserAcceleration = 4f;
    [Tooltip("How old the player's lateral/height position it follows is (s, difficulty 0 / 1): larger = easier to dodge.")]
    [Min(0f)] public float easyChaserFollowDelay = 0.7f;
    [Min(0f)] public float hardChaserFollowDelay = 0.25f;
    [Tooltip("Seconds it chases before giving up and recycling (difficulty 0 / 1).")]
    [Min(1f)] public float easyChaserDuration = 7f;
    [Min(1f)] public float hardChaserDuration = 13f;
    [Tooltip("Its lateral speed (m/s) — below strafeSpeed so a dodge outruns it sideways.")]
    [Min(0.5f)] public float chaserLateralSpeed = 5f;

    [Header("Penalties (1 = the same penalty as a fall)")]
    [Min(0f)] public float fallPenaltyScale = 1f;
    [Min(0f)] public float chaserPenaltyScale = 1f;
    [Min(0f)] public float trappedPenaltyScale = 1f;
    [Tooltip("Seconds blocked AT the back limit of the movement area before it counts as trapped.")]
    [Min(0f)] public float trapGraceSeconds = 0.4f;
    [Tooltip("Seconds after a respawn during which neither the chaser nor a trap can penalise again.")]
    [Min(0f)] public float respawnProtectionSeconds = 1.5f;
    [Tooltip("After a respawn, no obstacle is rebuilt closer than this ahead of the player (metres).")]
    [Min(0f)] public float respawnClearAhead = 6f;

    [Header("TEST — development builds / editor only, OFF by default")]
    [Tooltip("Replaces the generated placement by a gallery: every obstacle kind in turn, then a chaser, repeated.")]
    public bool testGallery = false;
    [Tooltip("Seconds between two gallery hazards.")]
    [Min(2f)] public float testGalleryGapSeconds = 5f;
    [Tooltip("Hotkeys while playing: F8 = next obstacle kind ahead of the player, F9 = a chaser ahead.")]
    public bool testHotkeys = false;

    public static bool TestAllowed => Application.isEditor || Debug.isDebugBuild;

    public float PenaltyScale(RunnerPenaltyCause cause) => cause switch
    {
        RunnerPenaltyCause.ChaserContact => chaserPenaltyScale,
        RunnerPenaltyCause.Trapped       => trappedPenaltyScale,
        _                                => fallPenaltyScale,
    };

    public static float Lerp(float easy, float hard, float difficulty) => Mathf.Lerp(easy, hard, Mathf.Clamp01(difficulty));

    public RunnerObstacle PrefabFor(RunnerObstacleKind kind) => kind switch
    {
        RunnerObstacleKind.LowBlock   => lowBlockPrefab,
        RunnerObstacleKind.HighBlock  => highBlockPrefab,
        RunnerObstacleKind.Portal     => portalPrefab,
        RunnerObstacleKind.WindowWall => windowWallPrefab,
        RunnerObstacleKind.CrawlWall  => crawlWallPrefab,
        RunnerObstacleKind.UShape     => uShapePrefab,
        _ => null,
    };

    public float UnlockFor(RunnerObstacleKind kind) => kind switch
    {
        RunnerObstacleKind.LowBlock   => lowBlockFrom,
        RunnerObstacleKind.HighBlock  => highBlockFrom,
        RunnerObstacleKind.Portal     => portalFrom,
        RunnerObstacleKind.WindowWall => windowWallFrom,
        RunnerObstacleKind.CrawlWall  => crawlWallFrom,
        RunnerObstacleKind.UShape     => uShapeFrom,
        _ => 2f,
    };
}
