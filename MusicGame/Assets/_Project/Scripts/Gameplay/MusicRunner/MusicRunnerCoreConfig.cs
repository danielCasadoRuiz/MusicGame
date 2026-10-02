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

    [Header("Touch joystick — ANALOG (on-screen stick only; keyboard keeps its full ±1)")]
    [Tooltip("Stick deflection (0..1) ignored around the centre, so a resting thumb doesn't drift.")]
    [Range(0f, 0.5f)] public float joystickDeadZone = 0.12f;
    [Tooltip("Response curve exponent applied to the deflection left after the dead zone: 1 = linear, " +
             ">1 = much gentler near the centre, still full strength at the edge.")]
    [Range(1f, 3f)] public float joystickResponseExponent = 1.8f;
    [Tooltip("Gain on the curved deflection before the top-speed clamp: 1 = the edge of the stick " +
             "reaches the joystick top speed exactly; >1 reaches it earlier; <1 never quite reaches it.")]
    [Range(0.25f, 2f)] public float joystickSensitivity = 1f;
    [Tooltip("Joystick TOP lateral speed as a fraction of strafeSpeed (keyboard speed). Below 1 the " +
             "stick is calmer than the keyboard; pickup reach is computed with EffectiveStrafeSpeed, " +
             "so lowering this never creates unreachable pickups.")]
    [Range(0.3f, 1f)] public float joystickMaxLateralSpeedMultiplier = 0.85f;

    /// <summary>Raw stick X (-1..1) → steering (-1..1) that multiplies strafeSpeed:
    /// sign · maxMultiplier · clamp01(sensitivity · ((|x| − deadZone) / (1 − deadZone))^exponent).
    /// Analog: 20 % deflection gives a small fraction of the speed, never the keyboard's 100 %.</summary>
    public float ShapeJoystickLateral(float raw)
    {
        float a = Mathf.Abs(Mathf.Clamp(raw, -1f, 1f));
        if (a <= joystickDeadZone) return 0f;
        float t = (a - joystickDeadZone) / Mathf.Max(0.0001f, 1f - joystickDeadZone);
        float curved = Mathf.Clamp01(joystickSensitivity * Mathf.Pow(t, joystickResponseExponent));
        return Mathf.Sign(raw) * curved * joystickMaxLateralSpeedMultiplier;
    }

    /// <summary>The lateral speed the player can ALWAYS count on, whichever input they use (the
    /// slower of keyboard and full joystick). Pickup reachability (GameplayTimeline) uses this.</summary>
    public float EffectiveStrafeSpeed => strafeSpeed * Mathf.Min(1f, joystickMaxLateralSpeedMultiplier);
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

    // ── Manual Play Range ─────────────────────────────────────────────────────
    // A RUNNER concern, not an analysis one — the song is always analyzed in FULL regardless
    // (SongProfile/GameplayTimeline need every second of it for the level itself); this only
    // limits which window of it actually gets PLAYED (see GameplayManager.ResolvePlayRange/
    // GenerateAndStart). Applies to every song source today — catalog or uploaded alike — while
    // there's no real "interesting chunk" selection algorithm yet; once one exists, THAT decides
    // the window for catalog/automatic songs and this manual override becomes upload-only again.
    [Header("Manual Play Range (dev/testing — applies to every song until a real algorithm exists)")]
    public bool  useManualPlayRange          = true;
    [Tooltip("Seconds into the song where the played/analyzed window starts.")]
    public float manualPlayRangeStartSeconds = 0f;
    [Tooltip("Seconds into the song where the played window ends. Clamped to the song's actual " +
             "length — a value of 0 (or beyond the song's length) means 'to the end'.")]
    public float manualPlayRangeEndSeconds   = 60f;

    // ── Ending — fade out then farewell ──────────────────────────────────────────
    // The played window always ENDS with these two back-to-back phases (see
    // GameplayManager.GenerateAndStart/Update and MusicWorldManager.NormalizedBandValue's own
    // fade multiplier): first the terrain flattens AND the music fades to silence together, over
    // fadeOutSeconds, both finishing exactly at the played window's own end point; then the
    // player keeps running for farewellSeconds MORE, on now-flat ground with the song already
    // silent, before GameEndedEvent actually fires. Mirrors the countdown's own "run before you
    // can hear anything" shape, but in reverse at the finish line, instead of an abrupt cut.
    [Header("Ending (fade-out + farewell, before the end screen actually appears)")]
    [Tooltip("How many seconds BEFORE the played window's end point both the terrain (flattening " +
             "to 0 height) and the music (fading to silence) ramp down — finishing together " +
             "exactly at that end point.")]
    public float fadeOutSeconds  = 3f;
    [Tooltip("How many extra seconds the player keeps running afterward — flat ground, silent " +
             "(the song has already finished) — before the end screen actually appears.")]
    public float farewellSeconds = 5f;
}
