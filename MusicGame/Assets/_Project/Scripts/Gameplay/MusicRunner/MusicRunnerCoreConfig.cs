using UnityEngine;

/// <summary>
/// Core Music Runner gameplay rules: warm-up, player movement/physics, surge, and the general
/// "what happens when you fail" mode rules (fall detection, respawn timing, checkpoints). Split
/// out of the old monolithic GameplayConfig — see MusicRunnerGameplayConfig for the full picture.
/// </summary>
[CreateAssetMenu(fileName = "MusicRunnerCoreConfig", menuName = "MusicGame/MusicRunner/Core Config")]
public class MusicRunnerCoreConfig : ScriptableObject
{
    // ── Track ─────────────────────────────────────────────────────────────────
    [Header("Track")]
    public float warmupTime  = 3f;

    // ── Player ────────────────────────────────────────────────────────────────
    [Header("Player")]
    public float playerSpeed = 10f;
    public float strafeSpeed = 8f;
    public float strafeLerp  = 10f;
    public float jumpForce   = 9f;
    public float gravity     = -22f;

    [Header("Player Surge (W / ↑)")]
    public float maxSurge   = 5f;
    public float surgeSpeed = 8f;
    public float surgeDecay = 6f;

    [Header("Player — Air Control")]
    [Tooltip("true: full lateral control while airborne (strafe input keeps steering mid-jump, " +
             "same as it always has). false: lateral input has no effect while airborne — " +
             "whatever lateral momentum existed the instant the player left the ground carries " +
             "through unchanged (no decay, no new player-driven acceleration/direction change) " +
             "until landing, when full control returns immediately.")]
    public bool allowAirControl = false;

    // ── Fall Off Path ─────────────────────────────────────────────────────────
    [Header("Fall Off Path")]
    public bool  enableFallOffPath   = true;
    [Tooltip("How far BELOW the track's local surface plane (PlayerController.IsBelowTrackSurface, " +
             "using the path sample's own position/up at the player's distance — not a world Y) " +
             "the player must sink before a fall is confirmed. Being laterally outside the track " +
             "but still above this plane (e.g. mid-air over the void, correctable with air control " +
             "— see PlayerController.UpdateLateralOffset/config.allowAirControl) no longer counts " +
             "as a fall by itself. Small on purpose: it's just a buffer against false " +
             "positives right at the surface, not a tolerance for genuinely hanging in the air.")]
    public float fallDeathDepth      = 0.1f;
    [Tooltip("Duration of the audio fade-out on fall")]
    public float fallFadeOutDuration = 0.6f;

    // ── Respawn ───────────────────────────────────────────────────────────────
    [Header("Respawn")]
    [Tooltip("Pause between audio fade-out and repositioning")]
    public float checkpointRespawnDelay         = 0.5f;
    [Tooltip("Duration of the audio fade-in after respawn")]
    public float checkpointMusicFadeInDuration  = 0.8f;

    // ── Checkpoints ───────────────────────────────────────────────────────────
    // No longer used for respawn or scoring — CheckpointSystem still runs purely as a passive/
    // debug system (F1 debug HUD + gizmos), so it's kept rather than ripped out.
    [Header("Checkpoints (debug/gizmos only — no longer drives respawn or scoring)")]
    public bool  enableCheckpoints            = true;
    [Tooltip("Song-time interval in seconds between auto-generated checkpoints")]
    public float checkpointIntervalSeconds    = 60f;
}
