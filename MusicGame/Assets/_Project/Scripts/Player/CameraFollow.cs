using System;
using UnityEngine;

/// <summary>
/// Path-based third-person camera.
///
/// Stability principles:
///  - Lateral tracked as a stored float (_smoothedLateral), never re-derived from world pos
///    (re-deriving via Vector3.Dot with camSample.right jitters when path curves)
///  - Camera position smoothed with SmoothDamp to absorb path-sample discontinuities
///  - LookAt uses Vector3.up (not camSample.up) to prevent roll jitter on curves
///  - LookAt target itself smoothed to prevent rapid rotation snaps
///  - Energy modulates height only (not Z-distance), no path-curve interaction
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("References")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private GameplayConfig   config;

    [Header("Position")]
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

    // ── Runtime ───────────────────────────────────────────────────────────────
    private SongProfile _profile;
    private float       _velX;
    private float       _smoothedLateral;  // path-local right, persisted (never re-derived)
    private float       _energyHeight;     // height bump from intensity
    private Vector3     _velPos;           // SmoothDamp velocity for world position
    private Vector3     _smoothedLookAt;   // smoothed look-at target
    private bool        _initialized;
    private bool        _running;
    private float       _kick;             // beat-impulse height offset, decays to 0
    private float       _kickVel;

    private Action<SongProfileReadyEvent> _onProfile;
    private Action<GameStartedEvent>      _onStart;
    private Action<BeatPulseEvent>        _onPulse;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Forces the NEXT LateUpdate to hard-snap to the player instead of gliding there via
    /// SmoothDamp — call right after teleporting the player (restart/respawn) so the camera
    /// doesn't visibly swoop in from wherever it was sitting before the teleport.
    /// </summary>
    public void SnapToPlayer()
    {
        _initialized = false;
        // Zeroed so the FIRST SmoothDamp'd frame after the snap doesn't inherit leftover
        // velocity from wherever the camera was moving before the teleport (would show up as a
        // brief overshoot/wobble right after the otherwise-instant snap).
        _velPos  = Vector3.zero;
        _velX    = 0f;
        _kick    = 0f;
        _kickVel = 0f;
    }

    private void OnEnable()
    {
        _onProfile = e => _profile = e.Profile;
        _onStart   = _ => _running = true;
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

    private void LateUpdate()
    {
        if (!_running) return;

        var clock = MusicClock.Instance;
        var path  = MusicWorldManager.Instance?.Path;
        if (clock == null || path == null) return;

        // ── Sample path behind the player ─────────────────────────────────────
        float camDist   = Mathf.Max(0f, clock.MusicDistance - behindDistance);
        var   camSample = path.GetSample(camDist);

        // ── Lateral: smooth stored float (NOT Vector3.Dot re-derivation) ─────
        float targetLateral = playerController != null ? playerController.LateralOffset : 0f;
        _smoothedLateral = Mathf.SmoothDamp(_smoothedLateral, targetLateral, ref _velX, xSmoothTime);

        // ── Energy → height only (no Z shift → no path-curve interaction) ────
        if (_profile != null && clock.IsRunning && clock.SongTime > config.warmupTime)
        {
            float songT = clock.SongTime - config.warmupTime;
            float e     = _profile.GetIntensityAt(songT);
            _energyHeight = Mathf.Lerp(_energyHeight, e * energyHeightPeak, energySmooth * Time.deltaTime);
        }

        // ── Beat kick: short impulse per pulse, exponential decay to rest ─────
        _kick = Mathf.SmoothDamp(_kick, 0f, ref _kickVel, 1f / kickDecay);

        // ── Target world position ─────────────────────────────────────────────
        Vector3 camPos = camSample.position
                       + camSample.right * _smoothedLateral
                       + Vector3.up      * (heightAbovePath + _energyHeight + _kick);

        // Smooth world position to absorb any tiny path-sample discontinuities
        if (!_initialized)
        {
            transform.position = camPos;
            if (playerController != null)
                _smoothedLookAt = playerController.transform.position + Vector3.up * 0.75f;
            _initialized = true;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(transform.position, camPos, ref _velPos, posSmoothTime);
        }

        // ── Look at player ─────────────────────────────────────────────────────
        // Use Vector3.up (not camSample.up) → no camera roll when path tilts
        // Smooth look target → no rotation snaps from physics/position jumps
        if (playerController != null)
        {
            Vector3 targetLook = playerController.transform.position + Vector3.up * 0.75f;
            _smoothedLookAt = Vector3.Lerp(_smoothedLookAt, targetLook, 15f * Time.deltaTime);
            transform.LookAt(_smoothedLookAt, Vector3.up);
        }
    }
}
