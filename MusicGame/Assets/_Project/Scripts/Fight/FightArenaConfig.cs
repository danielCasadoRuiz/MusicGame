using UnityEngine;

/// <summary>
/// Every physical/tunable fact about the Fight arena itself — spawn points, the main combat axis'
/// bounds, a small depth range, minimum separation between fighters, the flat base locomotion speed
/// (see FighterMovement's own doc on why no FighterStats.Speed scaling exists yet), and the Player's
/// configurable fighter prefab (no Player avatar/progression system exists yet — see FighterActor's
/// own doc). Same "no numbers hardcoded in a controller" rule as every other Fight*Config —
/// FightSceneBootstrap/FighterMovement only ever read these values, never invent their own.
///
/// The main combat axis is world X (matches the existing placeholder layout — player at negative X,
/// opponent at positive X) with a SMALL, bounded depth range on Z (see minDepth/maxDepth) — a limited
/// 3D combat plane, deliberately never free 8-way movement (see FighterMovement's own doc). Forward/
/// Back/Side are all resolved RELATIVE to the live line between the two fighters (see
/// RealFightFacingProvider/FighterActor.ForwardXZ/SideXZ) — never assumed to be world X/Z directly.
/// </summary>
[CreateAssetMenu(fileName = "FightArenaConfig", menuName = "MusicGame/Fight/Arena Config")]
public class FightArenaConfig : ScriptableObject
{
    [Header("Spawn points")]
    public Vector3 playerSpawnPosition = new Vector3(-1.5f, 1f, 0f);
    public Vector3 opponentSpawnPosition = new Vector3(1.5f, 1f, 0f);

    [Header("Bounds — main combat axis (world X)")]
    public float minBoundX = -4f;
    public float maxBoundX = 4f;

    [Header("Depth (world Z) — small on purpose, never free 3D roaming")]
    [Tooltip("Kept small relative to minBoundX/maxBoundX (see class doc) — this is a limited combat " +
             "plane, not a beat-'em-up arena.")]
    public float minDepth = -1.5f;
    public float maxDepth = 1.5f;

    [Tooltip("How close the two fighters can get (on the XZ plane) before movement/lunges/sidesteps " +
             "stop pushing them any closer — a simple, deterministic clamp (see FightMovementUtility's " +
             "own doc), not physics. Sidestepping past each other in depth is still allowed; this only " +
             "stops a straight-on overlap.")]
    public float minimumFighterSeparation = 1f;

    [Header("Sidestep — a short perpendicular dodge (see FighterMovement's own doc), not a teleport")]
    public float sidestepDistance = 1.2f;
    [Tooltip("How long the dodge displacement itself takes to complete.")]
    public float sidestepDuration = 0.18f;
    [Tooltip("Extra time after sidestepDuration before another sidestep/sidewalk can start.")]
    public float sidestepRecovery = 0.12f;

    [Header("SideWalk — continuous lateral movement while held (see FighterMovement's own doc)")]
    [Tooltip("Slower than baseMovementSpeed on purpose — a repositioning tool, not a second running speed.")]
    public float sidewalkSpeed = 2.2f;

    [Header("Tap vs Hold — Up/Down: Sidestep vs Jump/Crouch (see FighterMovement's own doc)")]
    [Tooltip("How long Up/Down must be held before it resolves as Jump/Crouch instead of a Sidestep " +
             "tap. Kept short — this is a fighting game, not a menu — see this phase's own explicit " +
             "\"no vull que Jump/Crouch se sentin lents\" requirement.")]
    public float directionHoldThreshold = 0.15f;
    [Tooltip("Max seconds between releasing a Sidestep tap and pressing the SAME direction again for " +
             "it to count as the second half of a SideWalk double-tap, instead of a fresh, unrelated " +
             "tap.")]
    public float doubleTapWindow = 0.3f;

    [Header("Movement")]
    [Tooltip("Flat locomotion speed (units/second) — not yet scaled by FighterStats.Speed.")]
    public float baseMovementSpeed = 4f;
    [Tooltip("Multiplies baseMovementSpeed while FighterMovementState is Run (see FighterMovement's " +
             "own doc on how Forward-Forward + held Forward enters Run).")]
    public float runSpeedMultiplier = 1.6f;

    [Header("Jump — a simple velocity/gravity arc, not real physics (see FighterMovement's own doc)")]
    public float jumpVelocity = 6f;
    public float gravity = 20f;
    [Tooltip("Horizontal movement multiplier while Airborne — 1 = full ground speed, 0 = no air control.")]
    [Range(0f, 1f)] public float airControlMultiplier = 0.5f;

    [Header("Player visual")]
    [Tooltip("No Player avatar/progression system exists yet (see FighterActor's own doc) — null " +
             "is tolerated, FighterActor falls back to a debug capsule.")]
    public GameObject playerFighterPrefab;

    [Header("Debug visuals — used only for the capsule fallback")]
    public Color playerDebugColor = new Color(0.2f, 0.6f, 1f);
    public Color opponentDebugColor = new Color(0.9f, 0.25f, 0.2f);
}
