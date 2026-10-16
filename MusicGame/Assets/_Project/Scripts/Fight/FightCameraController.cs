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
    private Camera _camera;

    // First person from the player's eye pivot (FirstPersonHeadRig) — see ComputeFirstPersonView.
    private readonly FirstPersonHeadRig _playerHead = new();
    private Vector3 _fpEye;              // this frame's eye pivot position (world)
    private Quaternion _fpRot;           // stabilised head rotation (moderate smoothing + roll damping)
    private FighterActor _playerActor;   // knockdown state (the downed view turns to the rival), resolved lazily
    private bool _fpRotInit;
    private float _fpEnterT = 1f;        // 0 -> 1 blend from the previous view into the eyes, then rigid
    private Vector3 _fpEnterPos;
    private Quaternion _fpEnterRot;
    private float _thirdPersonNearClip = -1f; // the camera's own near plane, restored outside first person
    private float _thirdPersonFov = -1f;      // the camera's own field of view, restored outside first person

    /// <summary>Diagnostics (FirstPersonCameraCapture): the player's head rig and this frame's eye target.</summary>
    public FirstPersonHeadRig PlayerHeadRig => _playerHead;
    public Vector3 FirstPersonEyeTarget => _fpEye;

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
        if (ViewMode == CameraViewMode.FirstPerson) BeginFirstPersonEnter();
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
        _playerHead.Release(); // never leave the player's head collapsed
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
        UpdateCameraPose();
        UpdateFirstPersonPresentation();
    }

    private void UpdateCameraPose()
    {
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
            // Position: glued to the eye pivot (child of the Head bone) - no lag, ever: advancing, lunging,
            // being hit or falling moves the view exactly with the head. Only the moment of switching view
            // blends in (firstPersonEnterBlendTime). Rotation: the head's own, moderately stabilised
            // (ComputeFirstPersonView) - never disconnected from it.
            if (_fpEnterT < 1f)
            {
                float blend = _config != null ? _config.firstPersonEnterBlendTime : 0.3f;
                _fpEnterT = Mathf.Min(1f, _fpEnterT + Time.deltaTime / Mathf.Max(0.01f, blend));
                float e = _fpEnterT * _fpEnterT * (3f - 2f * _fpEnterT);
                transform.SetPositionAndRotation(Vector3.Lerp(_fpEnterPos, targetPos, e), Quaternion.Slerp(_fpEnterRot, targetRot, e));
            }
            else transform.SetPositionAndRotation(targetPos, targetRot);
            _positionVelocity = Vector3.zero;
            return;
        }
        float posSmooth = _config != null ? _config.positionSmoothTime : 0.15f;
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref _positionVelocity, posSmooth);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotSmooth * Time.deltaTime);
    }

    private void BeginFirstPersonEnter()
    {
        _fpEnterT = 0f;
        _fpEnterPos = transform.position;
        _fpEnterRot = transform.rotation;
        _fpRotInit = false;
    }

    /// <summary>First-person-only presentation: a short near clip plane (the lens is just in front of the
    /// face; the own hands stay visible) and a slightly wider lens. Nothing of the avatar is hidden.
    /// Restored in third person.</summary>
    private void UpdateFirstPersonPresentation()
    {
        bool firstPerson = ViewMode == CameraViewMode.FirstPerson;
        var cam = Camera;
        if (cam != null)
        {
            if (_thirdPersonNearClip < 0f) _thirdPersonNearClip = cam.nearClipPlane;
            float fpNear = _config != null ? Mathf.Max(0.005f, _config.firstPersonNearClip) : 0.03f;
            cam.nearClipPlane = firstPerson ? fpNear : _thirdPersonNearClip;
            if (_thirdPersonFov < 0f) _thirdPersonFov = cam.fieldOfView;
            float fpFov = _config != null ? _config.firstPersonFieldOfView : 80f;
            // Eased like the position blend, so the switch never pops the lens.
            cam.fieldOfView = Mathf.MoveTowards(cam.fieldOfView, firstPerson ? fpFov : _thirdPersonFov, 90f * Time.unscaledDeltaTime);
        }
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
            // The player's REAL view: the eye pivot under the Head bone. The third-person yaw above keeps
            // running, so switching back is seamless.
            ComputeFirstPersonView(out targetPos, out targetRot);
            firstPerson = true;
            return;
        }

        targetPos = desiredPosition;
        targetRot = desiredRotation;
        firstPerson = false;
    }

    /// <summary>
    /// The player's eye pivot (FirstPersonHeadRig, child of the Head bone): position exact; rotation = the
    /// head's, smoothed over firstPersonRotationSmoothTime with its roll partly levelled
    /// (firstPersonRollDamping) - punches and hit reactions don't shake the view, yet it stays the head's
    /// view (a fall or KO ends looking from the floor, from the eyes).
    /// </summary>
    private void ComputeFirstPersonView(out Vector3 position, out Quaternion rotation)
    {
        float eyeUp  = _config != null ? _config.firstPersonEyeUp : FirstPersonHeadRig.DefaultEyeUp;
        float eyeFwd = _config != null ? _config.firstPersonEyeForward : FirstPersonHeadRig.DefaultEyeForward;
        if (!_playerHead.Resolve(_player, eyeUp, eyeFwd))
        {
            float fallbackHeight = _config != null ? _config.firstPersonFallbackEyeHeight : 1.63f; // arena floor is y = 0
            position = new Vector3(_player.position.x, fallbackHeight, _player.position.z);
            Vector3 flatToRival = _opponent.position - _player.position; flatToRival.y = 0f;
            rotation = flatToRival.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flatToRival.normalized, Vector3.up) : transform.rotation;
            _fpEye = position;
            return;
        }

        position = _fpEye = _playerHead.Pivot.position;
        Quaternion head = _playerHead.Pivot.rotation;
        float rollDamping = _config != null ? _config.firstPersonRollDamping : 0.5f;
        Vector3 forward = head * Vector3.forward;

        // EYES: real eyes turn inside the head toward what the fighter watches — the rival's upper body. The
        // gaze may deviate from the head's facing by at most firstPersonEyeGazeMaxAngle (a boxing stance
        // carries the chin down and the head turned), so the view never detaches from the head. Downed (fall /
        // KO) the head faces the sky: the gaze then turns fully to the rival, still from the eyes on the floor.
        Vector3 toRival = RivalUpperBody() - position;
        if (toRival.sqrMagnitude > 0.0001f)
        {
            if (_playerActor == null && _player != null) _playerActor = _player.GetComponent<FighterActor>();
            bool downed = _playerActor != null && ((_playerActor.HitReaction != null && _playerActor.HitReaction.IsInKnockdownFlow)
                                                   || (_playerActor.Health != null && _playerActor.Health.IsKO));
            float maxGaze = downed ? 180f : (_config != null ? _config.firstPersonEyeGazeMaxAngle : 25f);
            forward = Vector3.RotateTowards(forward, toRival.normalized, maxGaze * Mathf.Deg2Rad, 0f);
        }
        Vector3 up = Vector3.Slerp(head * Vector3.up, Vector3.up, rollDamping);
        if (Vector3.Cross(forward, up).sqrMagnitude < 1e-6f) up = head * Vector3.up; // looking straight up/down
        Quaternion target = Quaternion.LookRotation(forward, up);
        float tau = _config != null ? _config.firstPersonRotationSmoothTime : 0.1f;
        _fpRot = !_fpRotInit ? target : Quaternion.Slerp(_fpRot, target, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, tau)));
        _fpRotInit = true;
        rotation = _fpRot;
    }

    /// <summary>The rival's chest height over its root (Humanoid Chest bone when available).</summary>
    private Vector3 RivalUpperBody()
    {
        var anim = _opponent.GetComponentInChildren<Animator>();
        Transform chest = null;
        if (anim != null && anim.isHuman)
        {
            chest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.Chest);
        }
        return chest != null ? chest.position : _opponent.position + Vector3.up * 1.3f;
    }

    private static float DirToYaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    private static Vector3 YawToDir(float yaw) => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
}
