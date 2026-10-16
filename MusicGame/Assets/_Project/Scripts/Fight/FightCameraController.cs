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
/// INTRO TRAVELLING (BeginIntroTravelling / CompleteIntroTravelling): the Versus presentation's short
/// pass over the arena is driven HERE, by the same component that owns the transform — never a second
/// camera script. It eases from a configurable close shot of the rival to the live combat framing
/// computed every frame below, and ends (naturally or skipped) by snapping exactly onto that framing
/// with the smoothing velocity reset — so both routes reach the identical final state.
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

    // Intro travelling state (see class doc).
    private bool _introActive, _introStartCaptured, _snapNextFrame;
    private float _introElapsed, _introDuration;
    private Vector3 _introStartPos;
    private Quaternion _introStartRot;
    private Vector3 _fpLookPoint;
    private Camera _camera;

    public Camera Camera => _camera != null ? _camera : _camera = GetComponent<Camera>();
    public bool IsIntroTravelling => _introActive;

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

    /// <summary>Starts (or restarts — Fight Again) the intro pass from the configured close shot of the
    /// rival to the combat framing, over `seconds` (0 or less = no pass: snap at once).</summary>
    public void BeginIntroTravelling(float seconds)
    {
        _introElapsed = 0f;
        _introDuration = seconds;
        _introStartCaptured = false;
        _introActive = seconds > 0f;
        _snapNextFrame = !_introActive;
    }

    /// <summary>Ends the pass NOW on the exact combat framing (Skip or cancellation) — the same final
    /// state as a pass that ran to its end. No-op when no pass is running.</summary>
    public void CompleteIntroTravelling()
    {
        if (!_introActive) return;
        _introActive = false;
        SnapToTarget();
    }

    private void LateUpdate()
    {
        if (_player == null || _opponent == null) return;
        ComputeTarget(out Vector3 targetPos, out Quaternion targetRot, out bool firstPerson);

        if (_introActive) { UpdateIntro(targetPos, targetRot); return; }
        if (_snapNextFrame)
        {
            _snapNextFrame = false;
            transform.SetPositionAndRotation(targetPos, targetRot);
            _positionVelocity = Vector3.zero;
            return;
        }

        float rotSmooth = _config != null ? _config.rotationSmoothSpeed : 6f;
        if (firstPerson)
        {
            float fpSmooth = _config != null ? _config.firstPersonPositionSmoothTime : 0.08f;
            transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref _positionVelocity, fpSmooth);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation((_fpLookPoint - transform.position).normalized, Vector3.up),
                                                  rotSmooth * 1.5f * Time.deltaTime);
            return;
        }
        float posSmooth = _config != null ? _config.positionSmoothTime : 0.15f;
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref _positionVelocity, posSmooth);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotSmooth * Time.deltaTime);
    }

    private void SnapToTarget()
    {
        if (_player == null || _opponent == null) { _snapNextFrame = true; return; }
        ComputeTarget(out Vector3 pos, out Quaternion rot, out _);
        transform.SetPositionAndRotation(pos, rot);
        _positionVelocity = Vector3.zero;
    }

    private void UpdateIntro(Vector3 targetPos, Quaternion targetRot)
    {
        if (!_introStartCaptured)
        {
            ComputeIntroStart(out _introStartPos, out _introStartRot);
            _introStartCaptured = true;
        }
        _introElapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(_introElapsed / Mathf.Max(0.01f, _introDuration));
        float e = t * t * (3f - 2f * t); // smoothstep: soft start, soft landing on the combat framing
        transform.SetPositionAndRotation(Vector3.Lerp(_introStartPos, targetPos, e), Quaternion.Slerp(_introStartRot, targetRot, e));
        if (t >= 1f)
        {
            _introActive = false;
            transform.SetPositionAndRotation(targetPos, targetRot); // exactly the Skip end state
            _positionVelocity = Vector3.zero;
        }
    }

    /// <summary>Close shot of the rival (FightCameraConfig.introStartOffset / introLookHeight), on the
    /// same side of the fighters' line the combat framing watches from, so the pass never crosses it.</summary>
    private void ComputeIntroStart(out Vector3 pos, out Quaternion rot)
    {
        Vector3 local = _config != null ? _config.introStartOffset : new Vector3(0.6f, 1.6f, 2.2f);
        float lookHeight = _config != null ? _config.introLookHeight : 1.5f;
        Vector3 mid = (_player.position + _opponent.position) * 0.5f;
        Vector3 toOpp = _opponent.position - mid; toOpp.y = 0f;
        toOpp = toOpp.sqrMagnitude > 0.0001f ? toOpp.normalized : Vector3.right;
        Vector3 viewSide = YawToDir(_yaw);
        Vector3 groundOpp = new Vector3(_opponent.position.x, 0f, _opponent.position.z);
        pos = groundOpp + toOpp * local.x + Vector3.up * local.y + viewSide * local.z;
        Vector3 look = groundOpp + Vector3.up * lookHeight;
        rot = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
    }

    /// <summary>The framing the camera is heading to this frame (third or first person).</summary>
    private void ComputeTarget(out Vector3 targetPos, out Quaternion targetRot, out bool firstPerson)
    {
        Vector3 offset            = _config != null ? _config.offset             : new Vector3(0f, 2.2f, -6f);
        float   minDistance       = _config != null ? _config.minDistance        : 4f;
        float   maxDistance       = _config != null ? _config.maxDistance        : 9f;
        float   separationPadding = _config != null ? _config.separationPadding  : 0.6f;
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
            Vector3 eye = _player.position + Vector3.up * fpHeight - toOpp * fpBack + alongLine * fpSide;
            _fpLookPoint = _opponent.position + Vector3.up * fpLook;
            targetPos = eye;
            targetRot = Quaternion.LookRotation((_fpLookPoint - eye).normalized, Vector3.up);
            firstPerson = true;
            return;
        }

        targetPos = desiredPosition;
        targetRot = desiredRotation;
        firstPerson = false;
    }

    private static float DirToYaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    private static Vector3 YawToDir(float yaw) => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
}
