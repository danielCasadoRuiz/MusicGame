using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Path-based third-person camera, now with an optional First Person view — same single
/// authority over the Main Camera transform either way (see ViewMode). Third Person is exactly
/// the original always-on behavior; First Person just rigidly follows firstPersonCameraTarget.
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
    [SerializeField] private GameplayConfig   config;

    [Header("Position (Third Person)")]
    [SerializeField] private float behindDistance  = 12f;   // arc-length behind canonical position
    [SerializeField] private float heightAbovePath = 5f;    // units above path surface
    [SerializeField] private float xSmoothTime     = 0.15f; // lateral smoothing
    [SerializeField] private float posSmoothTime   = 0.06f; // world-space position smoothing

    [Header("Energy Response")]
    [SerializeField] private float energyHeightPeak = 2f;   // extra height at max intensity
    [SerializeField] private float energySmooth     = 2f;

    [Header("Beat Kick")]
    [Tooltip("World-unit height impulse at full onset strength")]
    [SerializeField] private float kickStrength = 0.35f;
    [Tooltip("Higher = the kick snaps back to rest faster")]
    [SerializeField] private float kickDecay    = 20f;

    [Header("View Mode — First Person")]
    [Tooltip("Child transform under the Player representing roughly the head/eyes — the First " +
             "Person camera rigidly follows this. Provisional on the capsule; just move/reassign " +
             "this transform once a real avatar exists, nothing else needs to change.")]
    [SerializeField] private Transform firstPersonCameraTarget;
    [Tooltip("Renderers hidden while in First Person (e.g. the capsule body) — CharacterController, " +
             "colliders and all gameplay logic are left completely untouched.")]
    [SerializeField] private Renderer[] hideInFirstPerson;

    [Header("View Mode — Transition")]
    [SerializeField] private float viewTransitionDuration = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool enableDebugToggleKey = true;

    public CameraViewMode ViewMode => _viewMode;

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

    private Action<SongProfileReadyEvent> _onProfile;
    private Action<GameStartedEvent>      _onStart;
    private Action<BeatPulseEvent>        _onPulse;

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
    /// SmoothDamp — call right after teleporting the player (restart/respawn) so the camera
    /// doesn't visibly swoop in from wherever it was sitting before the teleport. Does NOT
    /// touch ViewMode — a restart never changes which view you were in.
    /// </summary>
    public void SnapToPlayer()
    {
        _initialized   = false;
        _transitioning = false;
        _velPos  = Vector3.zero;
        _velX    = 0f;
        _kick    = 0f;
        _kickVel = 0f;
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

    private void ApplyRendererVisibility()
    {
        bool hide = _viewMode == CameraViewMode.FirstPerson;

        if (hideInFirstPerson != null)
            foreach (var r in hideInFirstPerson)
                if (r != null) r.enabled = !hide;

        // Also hide whatever the player's OWN visual currently is (the provisional capsule today,
        // a real avatar later) — auto-discovered via PlayerController, not hardcoded here, so
        // swapping in the future avatar needs no change to this script.
        if (playerController != null && playerController.VisualRenderers != null)
            foreach (var r in playerController.VisualRenderers)
                if (r != null) r.enabled = !hide;
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
        _onPulse   = e => _kick = Mathf.Min(_kick + e.Strength * kickStrength, kickStrength * 1.5f);
        EventBus.Subscribe(_onProfile);
        EventBus.Subscribe(_onStart);
        EventBus.Subscribe(_onPulse);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onProfile);
        EventBus.Unsubscribe(_onStart);
        EventBus.Unsubscribe(_onPulse);
    }

    private void Update()
    {
        if (!enableDebugToggleKey) return;
        var kb = Keyboard.current;
        if (kb != null && kb.vKey.wasPressedThisFrame) ToggleView();
    }

    private void LateUpdate()
    {
        if (!_running) return;

        var clock = MusicClock.Instance;
        var path  = MusicWorldManager.Instance?.Path;
        if (clock == null || path == null) return;

        // ── Third Person natural pose — computed EVERY frame regardless of active view mode,
        // so its smoothing state is always caught up (see class doc) and so it's available as a
        // transition endpoint even while First Person is active. ─────────────────────────────
        float camDist   = Mathf.Max(0f, clock.MusicDistance - behindDistance);
        var   camSample = path.GetSample(camDist);

        float targetLateral = playerController != null ? playerController.LateralOffset : 0f;
        _smoothedLateral = Mathf.SmoothDamp(_smoothedLateral, targetLateral, ref _velX, xSmoothTime);

        if (_profile != null && clock.IsRunning && clock.SongTime > config.warmupTime)
        {
            float songT = clock.SongTime - config.warmupTime;
            float e     = _profile.GetIntensityAt(songT);
            _energyHeight = Mathf.Lerp(_energyHeight, e * energyHeightPeak, energySmooth * Time.deltaTime);
        }

        _kick = Mathf.SmoothDamp(_kick, 0f, ref _kickVel, 1f / kickDecay);

        Vector3 thirdPersonPos = camSample.position
                               + camSample.right * _smoothedLateral
                               + Vector3.up      * (heightAbovePath + _energyHeight + _kick);

        Vector3 playerLookPoint = playerController != null
            ? playerController.transform.position + Vector3.up * 0.75f
            : thirdPersonPos + Vector3.forward;

        if (!_initialized)
            _smoothedLookAt = playerLookPoint; // hard snap on the very first frame — see original doc
        else
            _smoothedLookAt = Vector3.Lerp(_smoothedLookAt, playerLookPoint, 15f * Time.deltaTime);

        bool       hasFPTarget    = firstPersonCameraTarget != null;
        Vector3    firstPersonPos = hasFPTarget ? firstPersonCameraTarget.position : thirdPersonPos;
        Quaternion firstPersonRot = hasFPTarget ? firstPersonCameraTarget.rotation : transform.rotation;

        // ── Apply ────────────────────────────────────────────────────────────────────────────
        if (!_initialized)
        {
            if (_viewMode == CameraViewMode.ThirdPerson)
            {
                transform.position = thirdPersonPos;
                transform.LookAt(_smoothedLookAt, Vector3.up);
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
                // Derived the same way plain Third Person derives it below (LookAt from the
                // actual resting position toward the smoothed look target).
                endRot = Quaternion.LookRotation((_smoothedLookAt - endPos).sqrMagnitude > 0.0001f
                    ? (_smoothedLookAt - endPos).normalized : transform.forward, Vector3.up);
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
            // Exact original Third Person behavior — position smoothed, then LookAt from
            // wherever that smoothing actually landed (not from the raw unsmoothed target).
            transform.position = Vector3.SmoothDamp(transform.position, thirdPersonPos, ref _velPos, posSmoothTime);
            transform.LookAt(_smoothedLookAt, Vector3.up);
        }
        else
        {
            // Rigidly attached to the head target — no lag, it IS the player's viewpoint.
            transform.position = firstPersonPos;
            transform.rotation = firstPersonRot;
        }
    }
}
