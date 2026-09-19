using UnityEngine;

/// <summary>
/// First functional Fight camera — frames BOTH fighters (never just the Player), unlike Runner's
/// own CameraFollow. Added at runtime, directly onto Fight.unity's existing arenaCamera GameObject,
/// by FightSceneBootstrap (see that class's own doc) — this is not a prefab component.
///
/// ORBITS the live line between the two fighters instead of sitting at a fixed world-space back
/// offset — now that fighters can occupy a small depth range (see FightArenaConfig.minDepth/
/// maxDepth), that line is no longer always aligned with world X. Conceptually: the camera watches
/// combat from a point perpendicular to the fighters' own line, always framing both of them,
/// re-orbiting smoothly (NOT snapping) as that line's angle changes — see _smoothedLineDir's own
/// doc. This is still a fixed, non-free camera: it only ever reacts to the two Transforms, with two
/// deliberately separate smoothing stages (position/rotation vs. orbit angle) so a quick sidestep
/// never visibly whips the view around — see FightCameraConfig.orbitSmoothSpeed's own doc.
///
/// Keeps running regardless of FightFlowState (see this phase's own scope note: "càmera pot
/// continuar funcionant" even before Fighting) — it only ever reads the two FighterActors'
/// Transforms, never gameplay state, so there's nothing to gate.
///
/// Config-driven (FightCameraConfig via AppConfig.fightCamera) — falls back to hardcoded defaults
/// if unconfigured, same convention as every other Fight controller.
/// </summary>
public class FightCameraController : MonoBehaviour
{
    /// <summary>Lets RealFightFacingProvider ask "what does the camera currently render as screen-
    /// right" without needing a direct reference wired in at construction time (fighters are wired
    /// before this component even exists — see FightSceneBootstrap's own call order). Null between
    /// scenes/before this exists; every reader tolerates that and falls back to world +X (see
    /// RealFightFacingProvider's own doc).</summary>
    public static FightCameraController Instance { get; private set; }

    private FightCameraConfig _config;
    private Transform _player;
    private Transform _opponent;

    private Vector3 _positionVelocity;
    private Vector3 _smoothedLineDir = Vector3.right;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Initialize(FightCameraConfig config, Transform player, Transform opponent)
    {
        _config   = config;
        _player   = player;
        _opponent = opponent;
    }

    private void LateUpdate()
    {
        if (_player == null || _opponent == null) return;

        Vector3 offset            = _config != null ? _config.offset             : new Vector3(0f, 2.2f, -6f);
        float   minDistance       = _config != null ? _config.minDistance        : 4f;
        float   maxDistance       = _config != null ? _config.maxDistance        : 9f;
        float   separationPadding = _config != null ? _config.separationPadding  : 0.6f;
        float   posSmoothTime     = _config != null ? _config.positionSmoothTime : 0.15f;
        float   rotSmoothSpeed    = _config != null ? _config.rotationSmoothSpeed : 6f;
        float   orbitSmoothSpeed  = _config != null ? Mathf.Max(0.01f, _config.orbitSmoothSpeed) : 3f;

        Vector3 midpoint = (_player.position + _opponent.position) * 0.5f;

        // The real line between the fighters, on the XZ plane — never assumed to be world X (see
        // FighterActor.ForwardXZ's own doc, the same principle applied here for the camera).
        Vector3 playerXZ   = new Vector3(_player.position.x, 0f, _player.position.z);
        Vector3 opponentXZ = new Vector3(_opponent.position.x, 0f, _opponent.position.z);
        Vector3 lineXZ     = opponentXZ - playerXZ;
        float   separation = lineXZ.magnitude;
        Vector3 rawLineDir = separation > 0.0001f ? lineXZ / separation : _smoothedLineDir;

        // Orbit smoothing — its OWN pace, separate from the position/rotation SmoothDamp/Slerp
        // below, so the camera's angular position around the arena eases gently even when the
        // fighters' instantaneous line changes quickly (a sidestep, a cross-up) — see
        // FightCameraConfig.orbitSmoothSpeed's own doc. Never a hard snap, never mareig.
        _smoothedLineDir = Vector3.Slerp(_smoothedLineDir, rawLineDir, Mathf.Clamp01(orbitSmoothSpeed * Time.deltaTime));
        if (_smoothedLineDir.sqrMagnitude > 0.0001f) _smoothedLineDir.Normalize();

        // Perpendicular to the (smoothed) fighters' line — "the camera observes the line that
        // unites the two fighters from a perpendicular angle" (task's own explicit conceptual ask).
        Vector3 viewSide = Vector3.Cross(Vector3.up, _smoothedLineDir).normalized;

        float backDistance = Mathf.Clamp(Mathf.Abs(offset.z) + separation * separationPadding, minDistance, maxDistance);

        // offset.x (previously a fixed world-space sideways slide) now slides ALONG the fighters'
        // own smoothed line instead, so it stays meaningful at any orbit angle rather than only
        // when that line happens to be world-X-aligned.
        Vector3 desiredPosition = midpoint + viewSide * backDistance + _smoothedLineDir * offset.x + Vector3.up * offset.y;
        Quaternion desiredRotation = Quaternion.LookRotation((midpoint - desiredPosition).normalized, Vector3.up);

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _positionVelocity, posSmoothTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotSmoothSpeed * Time.deltaTime);
    }
}
