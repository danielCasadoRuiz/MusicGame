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
/// re-orbiting smoothly (NOT snapping) as that line's angle changes — see _yaw's own
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

    // Camera direction from the fighters' midpoint, as a yaw angle (degrees, XZ plane). Tracks the
    // fighters' line as an UNDIRECTED axis — when they cross, the nearer perpendicular is kept, so
    // the view never orbits 180° just because two bodies swapped places.
    private float _yaw;
    private float _yawVelocity;
    private bool _yawInitialized;
    private float _crossedTime; // how long the player has been on screen-right (see sideSwapDelay)

    /// <summary>True while the player is currently shown on screen-right (crossed, not yet swapped back).</summary>
    public bool PlayerOnScreenRight { get; private set; }

    /// <summary>Screen-right of the THIRD-PERSON framing, computed every frame whatever the camera
    /// mode. Input facing (RealFightFacingProvider) reads this — never the live camera transform —
    /// so switching to first person can never change which key means Forward.</summary>
    public Vector3 ReferenceRightXZ { get; private set; } = Vector3.right;

    private const string ViewPrefsKey = "MusicGame.FightCameraView";
    /// <summary>Third person (framing both fighters) or first person (near the player's head).
    /// Presentation only; persisted like the Runner's own toggle.</summary>
    public CameraViewMode ViewMode { get; private set; } = CameraViewMode.ThirdPerson;
    public event System.Action<CameraViewMode> ViewModeChanged;

    public void ToggleView()
    {
        ViewMode = ViewMode == CameraViewMode.ThirdPerson ? CameraViewMode.FirstPerson : CameraViewMode.ThirdPerson;
        PlayerPrefs.SetInt(ViewPrefsKey, (int)ViewMode);
        PlayerPrefs.Save();
        ViewModeChanged?.Invoke(ViewMode);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        ViewMode = (CameraViewMode)PlayerPrefs.GetInt(ViewPrefsKey, (int)CameraViewMode.ThirdPerson);
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

        // "Player on the left" side of the line = Cross(up, player→opponent) (the original framing).
        Vector3 playerLeftSide = separation > 0.0001f ? Vector3.Cross(Vector3.up, lineXZ / separation) : YawToDir(_yaw);
        float playerLeftYaw = DirToYaw(playerLeftSide);
        if (!_yawInitialized) { _yaw = playerLeftYaw; _yawInitialized = true; }

        // Of the two perpendiculars, stay on the one nearer the current view — a cross-up flips the
        // fighters' line, not the camera.
        bool onPlayerLeftSide = Mathf.Abs(Mathf.DeltaAngle(_yaw, playerLeftYaw)) <= 90f;
        float targetYaw = onPlayerLeftSide ? playerLeftYaw : playerLeftYaw + 180f;
        PlayerOnScreenRight = !onPlayerLeftSide;

        // Deliberate, slow side swap only after the player has STAYED crossed for sideSwapDelay.
        float swapDelay = _config != null ? _config.sideSwapDelay : 2.5f;
        float swapMinSep = _config != null ? _config.sideSwapMinSeparation : 1.3f;
        _crossedTime = PlayerOnScreenRight ? _crossedTime + Time.deltaTime : 0f;
        if (PlayerOnScreenRight && swapDelay > 0f && _crossedTime >= swapDelay && separation >= swapMinSep)
            targetYaw = playerLeftYaw;

        float maxOrbitSpeed = _config != null ? _config.sideSwapMaxSpeed : 110f;
        _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, 1f / orbitSmoothSpeed, maxOrbitSpeed);
        Vector3 viewSide = YawToDir(_yaw);
        Vector3 alongLine = Vector3.Cross(viewSide, Vector3.up); // screen-right along the fighters' line

        // minDistance..maxDistance = the REAL straight-line distance camera → fighters' midpoint (offset.y
        // height included), so maxDistance is exactly the farthest the normal framing ever gets.
        float viewDistance = Mathf.Clamp(Mathf.Abs(offset.z) + separation * separationPadding, minDistance, maxDistance);
        float backDistance = Mathf.Sqrt(Mathf.Max(0.25f, viewDistance * viewDistance - offset.y * offset.y));

        // offset.x slides ALONG the fighters' line (screen-right), meaningful at any orbit angle.
        Vector3 desiredPosition = midpoint + viewSide * backDistance + alongLine * offset.x + Vector3.up * offset.y;
        Quaternion desiredRotation = Quaternion.LookRotation((midpoint - desiredPosition).normalized, Vector3.up);
        ReferenceRightXZ = alongLine;

        if (ViewMode == CameraViewMode.FirstPerson)
        {
            // Near-head, slightly behind and beside the player, looking at the opponent's chest. The
            // third-person yaw above keeps running, so switching back is seamless and facing is stable.
            Vector3 toOpp = separation > 0.0001f ? lineXZ / separation : alongLine;
            float fpHeight = _config != null ? _config.firstPersonHeight : 1.85f;
            float fpBack   = _config != null ? _config.firstPersonBack : 0.9f;
            float fpSide   = _config != null ? _config.firstPersonSide : 0.35f;
            float fpLook   = _config != null ? _config.firstPersonLookHeight : 1.35f;
            float fpSmooth = _config != null ? _config.firstPersonPositionSmoothTime : 0.08f;
            Vector3 eye = _player.position + Vector3.up * fpHeight - toOpp * fpBack + alongLine * fpSide;
            Vector3 look = _opponent.position + Vector3.up * fpLook;
            transform.position = Vector3.SmoothDamp(transform.position, eye, ref _positionVelocity, fpSmooth);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation((look - transform.position).normalized, Vector3.up),
                                                  rotSmoothSpeed * 1.5f * Time.deltaTime);
            return;
        }

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _positionVelocity, posSmoothTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotSmoothSpeed * Time.deltaTime);
    }

    private static float DirToYaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    private static Vector3 YawToDir(float yaw) => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
}
