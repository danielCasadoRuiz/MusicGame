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

    [Header("Locomotion feel — continuous movement, never a one-frame position jump")]
    [Tooltip("Walking AWAY from the opponent (backpedal) = baseMovementSpeed × this. Kept clearly " +
             "slower than walking forward, and far slower than the charge.")]
    [Range(0.1f, 1f)] public float backwardSpeedMultiplier = 0.6f;
    [Tooltip("m/s² — how fast normal walking reaches its speed / stops (high = responsive, still continuous).")]
    [Min(0.1f)] public float walkAcceleration = 30f;
    [Tooltip("Body turn speed (°/s) when facing flips (cross-up). 0 = instant snap (old behaviour).")]
    [Min(0f)] public float turnSpeedDegrees = 720f;
    [Tooltip("Move lunges (FightMoveDefinition.lungeDistance — attacks, backdash) are travelled over " +
             "time at this speed (m/s) instead of in a single frame.")]
    [Min(0.5f)] public float lungeSpeed = 6f;
    [Tooltip("Hit knockback is travelled over time at this speed (m/s) instead of in a single frame.")]
    [Min(0.5f)] public float knockbackSpeed = 9f;

    [Header("Charge — Forward, Forward + hold Forward (the run uses runSpeedMultiplier above)")]
    [Tooltip("m/s² while charging — the run builds up from walking speed to its top speed, so it has " +
             "visible momentum. Lower = longer build-up.")]
    [Min(0.1f)] public float chargeAcceleration = 9f;
    [Tooltip("Forward speed (m/s) the charge must have reached when it meets the opponent to count as " +
             "a body-check/tackle. Below it the fighters just stop at minimumFighterSeparation.")]
    [Min(0f)] public float tackleMinSpeed = 5.6f;
    [Tooltip("Extra distance beyond minimumFighterSeparation at which a charge counts as contact.")]
    [Min(0f)] public float tackleContactPadding = 0.12f;
    [Tooltip("Tackle base damage at tackleMinSpeed (x) and at full charge speed (y) — scaled further by " +
             "the attacker's Strength / defender's defence like any hit (FightHitResolver).")]
    public Vector2 tackleDamage = new Vector2(10f, 18f);
    [Tooltip("Tackle knockback distance (m) at tackleMinSpeed (x) and at full charge speed (y).")]
    public Vector2 tackleKnockback = new Vector2(1.0f, 1.8f);
    [Tooltip("True = a landed tackle knocks the opponent down (knockdown → downed → get-up flow).")]
    public bool tackleKnocksDown = true;
    [Tooltip("Hit stun (s) when tackleKnocksDown is off, or base for the blocked case.")]
    [Min(0f)] public float tackleHitStun = 0.45f;
    [Tooltip("Seconds the charger needs to recover after landing (or being blocked on) a tackle.")]
    [Min(0f)] public float tackleRecovery = 0.35f;

    [Header("Jump — a simple velocity/gravity arc, not real physics (see FighterMovement's own doc)")]
    public float jumpVelocity = 6f;
    public float gravity = 20f;
    [Tooltip("Horizontal movement multiplier while Airborne — 1 = full ground speed, 0 = no air control.")]
    [Range(0f, 1f)] public float airControlMultiplier = 0.5f;
    [Tooltip("Jump momentum: how fast (m/s²) input can bend the horizontal take-off velocity in the air. " +
             "Low = committed arcs (no instant mid-air reversal).")]
    public float airAcceleration = 4f;
    [Tooltip("Attack momentum carry: deceleration (m/s²) of the velocity a grounded attack carries " +
             "(FightMoveDefinition.forwardCarry). Airborne carry is ballistic until landing.")]
    public float attackCarryDeceleration = 7f;

    [Header("Player visual")]
    [Tooltip("No Player avatar/progression system exists yet (see FighterActor's own doc) — null " +
             "is tolerated, FighterActor falls back to a debug capsule.")]
    public GameObject playerFighterPrefab;
    [Tooltip("PROVISIONAL Player avatar (built via AvatarFactory like the opponent's) until the real " +
             "Player avatar system exists. Null = playerFighterPrefab / capsule.")]
    public AvatarRecipeSO playerAvatarRecipe;

    [Header("Debug visuals — used only for the capsule fallback")]
    public Color playerDebugColor = new Color(0.2f, 0.6f, 1f);
    public Color opponentDebugColor = new Color(0.9f, 0.25f, 0.2f);
}
