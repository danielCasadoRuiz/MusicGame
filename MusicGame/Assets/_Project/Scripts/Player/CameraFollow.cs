using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Path-based third-person camera, now with an optional First Person view — same single
/// authority over the Main Camera transform either way (see ViewMode). Third Person is exactly
/// the original always-on behavior; First Person is the avatar's REAL view: the camera is fixed to the eye
/// pivot under the Head bone (FirstPersonHeadRig) — position AND rotation, so it moves, bobs and turns with
/// the head, 360° spins included; no smoothing, no track-direction compensation. The lens sits just in front
/// of the face (nothing of the head in view, nothing hidden); body, arms and shadow stay visible.
/// firstPersonCameraTarget is only for the placeholder capsule.
/// A short position/rotation blend runs whenever the view mode changes.
///
/// Stability principles (Third Person):
///  - Lateral tracked as a stored float (_smoothedLateral), never re-derived from world pos
///    (re-deriving via Vector3.Dot with camSample.right jitters when path curves)
///  - Camera position smoothed with SmoothDamp to absorb path-sample discontinuities
///  - LookAt uses Vector3.up (not camSample.up) to prevent roll jitter on curves
///  - LookAt target itself smoothed to prevent rapid rotation snaps
///  - Energy modulates height only (not Z-distance), no path-curve interaction
///
/// This is 100% visual — it never touches songTime/musicDistance/player movement/scoring. Music
/// stays the sole temporal authority regardless of which view is active.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    private const string ViewModePrefsKey = "MusicGame.CameraViewMode";

    [Header("References")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private MusicRunnerGameplayConfig config;

    [Header("Position (Third Person)")]
    [SerializeField] private float behindDistance  = 12f;   // arc-length behind canonical position
    [SerializeField] private float heightAbovePath = 5f;    // units above path surface
    [Tooltip("Extra camera elevation, ADDED on top of Height Above Path — kept as a separate field " +
             "so you can dial in a steeper/shallower viewing angle onto the scene (more top-down, " +
             "more scenery visible below the player, different parallax against the Horizon World) " +
             "without redefining what Height Above Path itself means. This is a POSITION change: " +
             "because the camera always looks AT the player (see thirdPersonAllowYawRotation/" +
             "Framing Pan below), the player stays centered on its look axis no matter what this is " +
             "set to — it changes the ANGLE the shot is taken from, not where the player sits on " +
             "screen. For that, use Framing Pan Degrees below instead. 0 = original height.")]
    [SerializeField] private float extraHeight = 0f;
    [SerializeField] private float xSmoothTime     = 0.15f; // lateral smoothing
    [SerializeField] private float posSmoothTime   = 0.06f; // world-space position smoothing
    [Tooltip("true: exact original behavior — Third Person's rotation is a full LookAt toward the " +
             "player, which can wobble side to side as the player strafes laterally. false (new " +
             "default): yaw is LOCKED to the path's own heading at the camera's position instead — " +
             "it still turns through genuine track curves (that's the path curving, not the " +
             "player), it just no longer reacts to the player's lateral position/LookAt jitter. " +
             "Pitch still follows the player's height either way, so vertical framing is unchanged.")]
    [SerializeField] private bool thirdPersonAllowYawRotation = false;
    [Header("Framing Pan (Composer-style, like Cinemachine's Composer)")]
    [Tooltip("THIS is what actually moves the player up/down on screen — Height/Extra Height above " +
             "only move the CAMERA (the shot's position/angle), but since the camera always looks " +
             "directly AT the player, the player itself stays glued to the center of that look axis " +
             "no matter how you reposition the camera (this is exactly the limitation you hit: " +
             "position offsets can't 're-center' where the subject sits in the frame). A real pan " +
             "instead tilts the camera's AIM away from the player by this many degrees — same idea " +
             "as Cinemachine's Composer 'Screen Y': it doesn't move the camera at all, it just " +
             "rotates it so the tracked subject projects somewhere other than dead-center.\n" +
             "Positive = aim tilts UPWARD, so the player ends up LOWER in the frame and more of the " +
             "scenery above (sky/Horizon World arc/mountains) becomes visible — exactly the 'frame " +
             "the character toward the bottom, see the scene from above' composition. Negative = " +
             "the opposite (player higher in frame, aim tilts down). 0 = player exactly centered on " +
             "the look axis, the original behavior. Try small values first (5-15) — this is a real " +
             "rotation, not a viewport crop, so large values will visibly swing the horizon.")]
    [SerializeField] private float framePanDegrees = 0f;

    [Header("Energy Response")]
    [SerializeField] private float energyHeightPeak = 2f;   // extra height at max intensity
    [SerializeField] private float energySmooth     = 2f;

    [Header("Beat Kick")]
    [Tooltip("World-unit height impulse at full onset strength")]
    [SerializeField] private float kickStrength = 0.35f;
    [Tooltip("Higher = the kick snaps back to rest faster")]
    [SerializeField] private float kickDecay    = 20f;
    [Tooltip("Pulses whose MUSICAL time (eventTime) lies within this many seconds of an already-applied " +
             "one are the same audible hit: only the extra strength is added (max, not sum). Distinct " +
             "times always add, even in the same frame; nothing is delayed.")]
    [SerializeField] private float kickMergeWindow = 0.03f;

    [Header("View Mode — First Person")]
    [Tooltip("Child transform under the Player representing roughly the head/eyes — the First " +
             "Person camera rigidly follows this. Provisional on the capsule; just move/reassign " +
             "this transform once a real avatar exists, nothing else needs to change.")]
    [SerializeField] private Transform firstPersonCameraTarget;
    [Tooltip("Renderers hidden while in First Person (e.g. the capsule body) — CharacterController, " +
             "colliders and all gameplay logic are left completely untouched.")]
    [SerializeField] private Renderer[] hideInFirstPerson;

    [Header("View Mode — First Person on the real avatar (FirstPersonHeadRig)")]
    [Tooltip("With a Humanoid avatar the First Person camera sits at the avatar's REAL eyes (Head bone + " +
             "this offset, metres for the canonical 1.75 m body, scaled with the avatar) instead of " +
             "firstPersonCameraTarget — which stays only as the fallback for the placeholder capsule.")]
    [SerializeField] private float firstPersonEyeUp = FirstPersonHeadRig.DefaultEyeUp;
    [SerializeField] private float firstPersonEyeForward = FirstPersonHeadRig.DefaultEyeForward;
    [Tooltip("Vertical field of view (degrees) in first person; third person keeps the camera's own value. " +
             "A little wider than third person, so the own arms/body and the surroundings read naturally.")]
    [SerializeField] private float firstPersonFieldOfView = 80f;
    [Tooltip("Camera near clip plane in first person (restored in third person): the lens is just in front of " +
             "the face, a short near plane keeps the own hands visible without ever reaching into the head.")]
    [SerializeField] private float firstPersonNearClip = 0.03f;
    [Tooltip("Max sideways tilt (roll, degrees) the first-person view takes from the head: side-on clips (the " +
             "jump) would otherwise show the world tilted.")]
    [SerializeField] private float firstPersonMaxRoll = 8f;
    [Tooltip("Max yaw (degrees) the first-person view may turn away from the player's heading — side-on clips " +
             "(the jump) turn the head; the 360° AutoReturn spin is exempt and turns freely.")]
    [SerializeField] private float firstPersonMaxYaw = 25f;

    [Header("View Mode — Transition")]
    [SerializeField] private float viewTransitionDuration = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool enableDebugToggleKey = true;

    public CameraViewMode ViewMode => _viewMode;

    /// <summary>Single query point for "what's the current bonus visual reveal distance" —
    /// GameplayManager just reads this, with zero if(firstPerson) branching of its own. Changes
    /// immediately on a view toggle (next reveal check just uses the new value; already-revealed
    /// events are untouched). MusicRunnerCollectiblesConfig.GetBonusVisualActivationDistance is
    /// the actual Third/First Person mapping; this just supplies it with the CURRENT mode.</summary>
    public float EffectiveBonusVisualActivationDistance =>
        config != null ? config.collectibles.GetBonusVisualActivationDistance(_viewMode) : 0f;

    // ── Runtime: Third Person smoothing state (kept up to date regardless of active view mode,
    // so switching back to Third Person never has to "catch up" from stale values) ─────────────
    private SongProfile _profile;
    private float       _velX;
    private float       _smoothedLateral;
    private float       _energyHeight;
    private Vector3     _velPos;
    private Vector3     _smoothedLookAt;
    private bool        _initialized;
    private bool        _running;
    private float       _kick;
    private float       _kickVel;

    // ── Runtime: view mode / transition ─────────────────────────────────────────────────────
    private CameraViewMode _viewMode = CameraViewMode.ThirdPerson;
    private bool       _transitioning;
    private float      _transitionT;
    private Vector3    _transitionStartPos;
    private Quaternion _transitionStartRot;

    // ── Runtime: First Person on the real avatar ────────────────────────────────────────────
    private readonly FirstPersonHeadRig _headRig = new();

    /// <summary>Diagnostics (FirstPersonCameraCapture): the avatar head rig the First Person camera uses.</summary>
    public FirstPersonHeadRig HeadRig => _headRig;

    /// <summary>The head pivot's rotation relative to the player's heading, with roll clamped to
    /// firstPersonMaxRoll and yaw to firstPersonMaxYaw (except during the 360° AutoReturn spin). Pitch is free.</summary>
    private Quaternion LimitHeadRotation(Quaternion head)
    {
        Quaternion frame = playerController.transform.rotation;
        Vector3 e = (Quaternion.Inverse(frame) * head).eulerAngles;
        float pitch = Mathf.DeltaAngle(0f, e.x), yaw = Mathf.DeltaAngle(0f, e.y), roll = Mathf.DeltaAngle(0f, e.z);
        if (_runnerAnimator == null) _runnerAnimator = playerController.GetComponent<RunnerAvatarAnimator>();
        bool spinning = _runnerAnimator != null && _runnerAnimator.CurrentRole == RunnerAnimationRole.AutoReturn;
        if (!spinning) yaw = Mathf.Clamp(yaw, -firstPersonMaxYaw, firstPersonMaxYaw);
        roll = Mathf.Clamp(roll, -firstPersonMaxRoll, firstPersonMaxRoll);
        return frame * Quaternion.Euler(pitch, yaw, roll);
    }

    private RunnerAvatarAnimator _runnerAnimator;

    /// <summary>How the view deviates from the player's own heading this frame: yaw (degrees, signed — the head
    /// turning, a 360° spin) and roll (signed tilt). The Horizon camera copies exactly this, so the distant
    /// background turns and tilts WITH the view, while path curves (heading changes) still never swing it.</summary>
    public void GetViewOffsetFromHeading(out float yaw, out float roll)
    {
        Vector3 fwd = transform.forward;
        Vector3 heading = playerController != null ? playerController.transform.forward : fwd;
        fwd.y = 0f; heading.y = 0f;
        yaw = fwd.sqrMagnitude > 1e-6f && heading.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(heading, fwd, Vector3.up) : 0f;
        roll = Mathf.DeltaAngle(0f, transform.eulerAngles.z);
    }

    private Action<SongProfileReadyEvent> _onProfile;
    private Action<GameStartedEvent>      _onStart;
    private Action<BeatPulseEvent>        _onPulse;
    private Action<MusicTimeDiscontinuityEvent> _onDiscontinuity;
    private MusicalPulseDeduplicator      _pulseDedup;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        int saved = PlayerPrefs.GetInt(ViewModePrefsKey, (int)CameraViewMode.ThirdPerson);
        _viewMode = Enum.IsDefined(typeof(CameraViewMode), saved) ? (CameraViewMode)saved : CameraViewMode.ThirdPerson;
        ApplyRendererVisibility();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Forces the NEXT LateUpdate to hard-snap to the player instead of gliding there via
    /// SmoothDamp — call right after teleporting the player (restart/respawn), AFTER the player's
    /// own reset (ResetMotionState/Respawn) has already run, so LateralOffset/CanonicalDistance
    /// read back here are the POST-reset values, not stale ones. Does NOT touch ViewMode — a
    /// restart never changes which view you were in.
    ///
    /// Must clear _smoothedLateral itself, not just its velocity (_velX): _smoothedLateral is a
    /// SmoothDamp'd float re-evaluated every LateUpdate regardless of _initialized, so leaving it
    /// at its pre-teleport value meant the very first "hard snap" frame still built its position
    /// from a stale lateral offset — only _velX was zeroed, so it then took several more frames of
    /// SmoothDamp to visibly crawl from the old lateral position to the new (correct) one. That
    /// multi-frame crawl was the whole bug; resetting the value itself here removes it entirely.
    /// _energyHeight is reset for the same reason (no stale smoothed value carried across a hard
    /// reset) — a fresh gameplay moment should read its energy fresh, not ease into it.
    /// </summary>
    public void SnapToPlayer()
    {
        _initialized     = false;
        _transitioning   = false;
        _velPos          = Vector3.zero;
        _velX            = 0f;
        _kick            = 0f;
        _kickVel         = 0f;
        _smoothedLateral = playerController != null ? playerController.LateralOffset : 0f;
        _energyHeight    = 0f;
    }

    /// <summary>
    /// Single entry point for changing the view — the UI button and the 'V' debug key both call
    /// exactly this (never duplicate the toggle logic). Safe to call repeatedly/rapidly: each
    /// call re-starts the blend from wherever the camera actually is right now, so fast clicking
    /// never leaves it in an inconsistent state.
    /// </summary>
    public void ToggleView() => SetView(_viewMode == CameraViewMode.ThirdPerson ? CameraViewMode.FirstPerson : CameraViewMode.ThirdPerson);

    public void SetView(CameraViewMode mode)
    {
        _transitionStartPos = transform.position;
        _transitionStartRot = transform.rotation;
        _transitioning       = _initialized; // no blend needed before the camera has ever placed itself
        _transitionT         = 0f;

        _viewMode = mode;
        ApplyRendererVisibility();

        PlayerPrefs.SetInt(ViewModePrefsKey, (int)mode);
        PlayerPrefs.Save();

        EventBus.Publish(new CameraViewChangedEvent { Mode = mode });
    }

    /// <summary>
    /// Third Person's rotation — the ONLY place that decides it, used for the first-frame snap,
    /// the view-transition blend target, and the steady-state frame, so thirdPersonAllowYawRotation
    /// AND framePanDegrees behave identically everywhere instead of several slightly different
    /// LookAt call sites.
    /// </summary>
    private Quaternion ComputeThirdPersonRotation(Vector3 camPos, Vector3 tangent)
    {
        Vector3 toLook = _smoothedLookAt - camPos;
        Vector3 forward;

        if (thirdPersonAllowYawRotation)
        {
            // Exact original behavior — full LookAt toward the player.
            forward = toLook.sqrMagnitude > 0.0001f ? toLook.normalized : transform.forward;
        }
        else
        {
            // Yaw LOCKED to the path's own heading (still turns through real curves — that's the
            // path, not the player) — pitch still tracks the look-at target's height, built
            // directly from vectors (not Euler angles) to avoid any up/down sign ambiguity.
            Vector3 flatTangent = new Vector3(tangent.x, 0f, tangent.z);
            flatTangent = flatTangent.sqrMagnitude > 0.0001f ? flatTangent.normalized : Vector3.forward;

            float horizDist = new Vector3(toLook.x, 0f, toLook.z).magnitude;
            forward = (flatTangent * Mathf.Max(horizDist, 0.01f) + Vector3.up * toLook.y).normalized;
        }

        // Framing pan (Composer-style — see framePanDegrees doc): tilts the AIM, not the position,
        // so the player ends up off-center in the frame instead of always being re-centered by the
        // LookAt above. Rotating `forward` around the camera's own local right axis by a negative
        // angle tilts the aim upward, which pushes the (unmoved) player DOWN in the frame — so a
        // positive framePanDegrees reads as "player lower on screen", matching the tooltip.
        if (Mathf.Abs(framePanDegrees) > 0.0001f)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude > 0.0001f)
                forward = Quaternion.AngleAxis(-framePanDegrees, right.normalized) * forward;
        }

        return Quaternion.LookRotation(forward, Vector3.up);
    }

    private void ApplyRendererVisibility()
    {
        bool hide = _viewMode == CameraViewMode.FirstPerson;

        if (hideInFirstPerson != null)
            foreach (var r in hideInFirstPerson)
                if (r != null) r.enabled = !hide;

        // The player's OWN visual (auto-discovered via PlayerController, never hardcoded): a real
        // Humanoid avatar stays fully VISIBLE in First Person — the lens sits in front of the face, so
        // body, arms and shadow ground the view. Only a visual without a head rig (the placeholder
        // capsule) is hidden whole.
        bool hideWholeBody = hide && !TryResolveHeadRig();
        if (playerController != null && playerController.VisualRenderers != null)
            foreach (var r in playerController.VisualRenderers)
                if (r != null) r.enabled = !hideWholeBody;
    }

    private bool TryResolveHeadRig()
    {
        if (playerController == null) return false;
        return _headRig.Resolve(playerController.transform, firstPersonEyeUp, firstPersonEyeForward);
    }

    private void OnEnable()
    {
        _onProfile = e => _profile = e.Profile;
        // Also re-applies renderer visibility here (not just Awake) — PlayerController's visual
        // is built in ITS OWN Awake(), whose ordering relative to this one isn't guaranteed; by
        // GameStartedEvent time (GameplayManager's coroutine, well after every Awake) it's
        // definitely built, so a persisted First Person session starts with the body correctly
        // hidden from frame one instead of only on the next manual toggle.
        _onStart   = _ => { _running = true; ApplyRendererVisibility(); };
        // Capped: a dense burst of pulses (many onsets in quick succession) shouldn't be able
        // to stack past a sane ceiling — otherwise rapid-fire beats can pile the kick up faster
        // than it decays and the camera reads as constantly launching upward.
        // Deduplicated by musical identity first (MusicalPulseDeduplicator — same event, or the same
        // onset time within kickMergeWindow), so coinciding timeline events (an Impact plus a Kick
        // on one onset) kick once instead of stacking.
        _pulseDedup ??= new MusicalPulseDeduplicator(kickMergeWindow);
        _onPulse   = e =>
        {
            _pulseDedup.MergeWindow = kickMergeWindow;
            float add = _pulseDedup.Accept(e.EventIndex, e.EventTime, e.Strength);
            if (add > 0f) _kick = Mathf.Min(_kick + add * kickStrength, kickStrength * 1.5f);
        };
        // Seek/respawn/restart: the same event indices legitimately pulse again.
        _onDiscontinuity = _ => _pulseDedup.Reset();
        EventBus.Subscribe(_onProfile);
        EventBus.Subscribe(_onStart);
        EventBus.Subscribe(_onPulse);
        EventBus.Subscribe(_onDiscontinuity);
        // The real avatar is built asynchronously, after the visibility above was applied.
        if (playerController != null) playerController.VisualChanged += ApplyRendererVisibility;
    }

    private void OnDisable()
    {
        if (playerController != null) playerController.VisualChanged -= ApplyRendererVisibility;
        EventBus.Unsubscribe(_onProfile);
        EventBus.Unsubscribe(_onStart);
        EventBus.Unsubscribe(_onPulse);
        EventBus.Unsubscribe(_onDiscontinuity);
    }

    private void Update()
    {
        if (!enableDebugToggleKey) return;
        var kb = Keyboard.current;
        if (kb != null && kb.vKey.wasPressedThisFrame) ToggleView();
    }

    private Camera _camera;
    private float _thirdPersonFov = -1f; // the camera's own field of view, restored outside first person
    private float _thirdPersonNear = 0.3f;

    /// <summary>First person widens the lens (firstPersonFieldOfView) so the own arms/body show; eased so
    /// the Third↔First switch never pops.</summary>
    private void UpdateFieldOfView()
    {
        if (_camera == null) _camera = GetComponent<Camera>();
        if (_camera == null) return;
        if (_thirdPersonFov < 0f) { _thirdPersonFov = _camera.fieldOfView; _thirdPersonNear = _camera.nearClipPlane; }
        bool firstPerson = _viewMode == CameraViewMode.FirstPerson && _headRig.IsValid;
        _camera.nearClipPlane = firstPerson ? firstPersonNearClip : _thirdPersonNear;
        _camera.fieldOfView = Mathf.MoveTowards(_camera.fieldOfView, firstPerson ? firstPersonFieldOfView : _thirdPersonFov, 90f * Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (!_running) return;
        UpdateFieldOfView();

        var clock = MusicClock.Instance;
        var path  = MusicWorldManager.Instance?.Path;
        if (clock == null || path == null) return;

        // ── Third Person natural pose — computed EVERY frame regardless of active view mode,
        // so its smoothing state is always caught up (see class doc) and so it's available as a
        // transition endpoint even while First Person is active. ─────────────────────────────
        // Framed off PlayerController.CanonicalDistance (the player's own authority on "where it
        // belongs right now"), NOT MusicDistance directly — CameraFollow consumes the player's
        // state, it doesn't re-derive it from the music itself. Deliberately CanonicalDistance,
        // not ActualDistance: the camera must keep ignoring surge exactly as before (that's what
        // lets the player visibly pull ahead of the camera on a surge — see class doc).
        float playerBaseDistance = playerController != null ? playerController.CanonicalDistance : clock.MusicDistance;
        float camDist   = Mathf.Max(0f, playerBaseDistance - behindDistance);
        var   camSample = path.GetSample(camDist);

        float targetLateral = playerController != null ? playerController.LateralOffset : 0f;
        _smoothedLateral = Mathf.SmoothDamp(_smoothedLateral, targetLateral, ref _velX, xSmoothTime);

        if (_profile != null && clock.IsRunning && clock.HasPlaybackStarted)
        {
            float e = _profile.GetIntensityAt(clock.ClipTime); // SongProfile is clip-time indexed
            _energyHeight = Mathf.Lerp(_energyHeight, e * energyHeightPeak, energySmooth * Time.deltaTime);
        }

        _kick = Mathf.SmoothDamp(_kick, 0f, ref _kickVel, 1f / kickDecay);

        Vector3 thirdPersonPos = camSample.position
                               + camSample.right * _smoothedLateral
                               + Vector3.up      * (heightAbovePath + extraHeight + _energyHeight + _kick);

        Vector3 playerLookPoint = playerController != null
            ? playerController.transform.position + Vector3.up * (PlayerController.CapsuleHeight * 0.5f)
            : thirdPersonPos + Vector3.forward;

        if (!_initialized)
            _smoothedLookAt = playerLookPoint; // hard snap on the very first frame — see original doc
        else
            _smoothedLookAt = Vector3.Lerp(_smoothedLookAt, playerLookPoint, 15f * Time.deltaTime);

        bool       hasFPTarget    = firstPersonCameraTarget != null;
        Vector3    firstPersonPos = hasFPTarget ? firstPersonCameraTarget.position : thirdPersonPos;
        Quaternion firstPersonRot = hasFPTarget ? firstPersonCameraTarget.rotation : transform.rotation;
        bool       hasAvatarEyes  = TryResolveHeadRig();
        if (hasAvatarEyes)
        {
            // The avatar's REAL view: the eye pivot under the Head bone, position and rotation, unfiltered —
            // run bob, crouch, jumps and the 360° AutoReturn spin all move the view exactly as the head does.
            firstPersonPos = _headRig.Pivot.position;
            firstPersonRot = LimitHeadRotation(_headRig.Pivot.rotation);
        }
        // Placeholder capsule: the eyes go down with the body, over the same eased transition (and back up).
        else if (hasFPTarget && playerController != null) firstPersonPos -= playerController.transform.up * playerController.CrouchEyeDrop;

        // ── Apply ────────────────────────────────────────────────────────────────────────────
        if (!_initialized)
        {
            if (_viewMode == CameraViewMode.ThirdPerson)
            {
                transform.position = thirdPersonPos;
                transform.rotation = ComputeThirdPersonRotation(thirdPersonPos, camSample.tangent);
            }
            else
            {
                transform.position = firstPersonPos;
                transform.rotation = firstPersonRot;
            }
            _initialized = true;
            return;
        }

        if (_transitioning)
        {
            _transitionT += Time.deltaTime / Mathf.Max(0.0001f, viewTransitionDuration);
            bool done = _transitionT >= 1f;
            float t   = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_transitionT));

            Vector3    endPos;
            Quaternion endRot;
            if (_viewMode == CameraViewMode.ThirdPerson)
            {
                endPos = thirdPersonPos;
                // Derived the same way plain Third Person derives it below — respects
                // thirdPersonAllowYawRotation for the transition's end pose too.
                endRot = ComputeThirdPersonRotation(endPos, camSample.tangent);
            }
            else
            {
                endPos = firstPersonPos;
                endRot = firstPersonRot;
            }

            transform.position = Vector3.Lerp(_transitionStartPos, endPos, t);
            transform.rotation = Quaternion.Slerp(_transitionStartRot, endRot, t);

            if (done) _transitioning = false;
            return;
        }

        if (_viewMode == CameraViewMode.ThirdPerson)
        {
            // Exact original Third Person behavior — position smoothed, then rotation resolved
            // from wherever that smoothing actually landed (not from the raw unsmoothed target).
            transform.position = Vector3.SmoothDamp(transform.position, thirdPersonPos, ref _velPos, posSmoothTime);
            transform.rotation = ComputeThirdPersonRotation(transform.position, camSample.tangent);
        }
        else
        {
            // Rigidly attached to the eyes — no lag, it IS the player's viewpoint.
            transform.position = firstPersonPos;
            transform.rotation = firstPersonRot;
        }
    }
}
