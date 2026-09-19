using UnityEngine;

/// <summary>
/// Every physical/tunable fact about the Fight arena itself — spawn points, the main combat axis'
/// bounds, minimum separation between fighters, the flat base locomotion speed (see
/// FighterMovement's own doc on why no FighterStats.Speed scaling exists yet), and the Player's
/// configurable fighter prefab (no Player avatar/progression system exists yet — see FighterActor's
/// own doc). Same "no numbers hardcoded in a controller" rule as every other Fight*Config —
/// FightSceneBootstrap/FighterMovement only ever read these values, never invent their own.
///
/// The main combat axis is always world X (matches the existing placeholder layout — player at
/// negative X, opponent at positive X — and this phase's "single-axis, no sidestep" scope).
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

    [Tooltip("How close the two fighters can get before movement/lunges stop pushing them any " +
             "closer — a simple, deterministic clamp (see FighterMovement's own doc), not physics.")]
    public float minimumFighterSeparation = 1f;

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
