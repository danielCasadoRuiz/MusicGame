using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Path-based runner player — the SINGLE authority over this transform during normal
/// gameplay. Every other system (camera, fall/respawn, HUD) only reads state from here;
/// none of them write transform.position while _running &amp;&amp; !_isFalling.
///
/// Per frame, three independent pieces of state are updated (pure — no transform writes),
/// then folded into exactly one CharacterController.Move() call:
///
///   CanonicalDistance = MusicClock.MusicDistance  — where the player belongs RIGHT NOW
///                                                    according to the music. Exposed publicly;
///                                                    this is the ONLY place that formula lives —
///                                                    CameraFollow/GameplayManager/FallRespawnSystem/
///                                                    debug all read it from here instead of each
///                                                    re-deriving songTime*speed themselves.
///   forwardOffset  ∈ [0, config.maxSurge]         — distance ahead of CanonicalDistance; eases
///                                                    back to 0 when not pressed, never negative
///                                                    (so ActualDistance can never fall behind
///                                                    CanonicalDistance)
///   ActualDistance = CanonicalDistance + forwardOffset  — where the player REALLY is (incl. surge)
///   sample         = MusicPath.GetSample(ActualDistance)   — ONE sample/frame; position,
///                                                    right, up and width all come from it
///   lateralOffset  — UNCLAMPED path-local right units; how far the player can actually get
///                                                    off the track is decided entirely by physics
///                                                    (strafeSpeed, jumpForce, gravity, air time),
///                                                    never by an artificial limit. Lateral input
///                                                    works identically whether grounded or airborne
///                                                    (gated only by config.allowAirControl) — WHY
///                                                    the player is airborne (jumped, or just ran
///                                                    off a steep downhill bump and briefly lost
///                                                    ground contact) never changes that. LateralLimit
///                                                    (= sample.width/2) is only a reference value
///                                                    for "where the real edge is", read by the
///                                                    HUD/debug and IsAtLateralLimit.
///   verticalVelocity — real gravity/jump, resolved by the SAME Move() call against
///                                                    MusicWorldManager's real ground collider
///
/// Fall state (EnterFallState/ExitFallState), Respawn() and SnapToCanonicalPosition() are the
/// only exceptions: they are explicit, event-driven handoffs from FallRespawnSystem, and they
/// fully reset forwardOffset/lateralOffset/verticalVelocity so the next frame doesn't fight its
/// way back from stale state.
/// </summary>
public class PlayerController : MonoBehaviour
{
    [SerializeField] private MusicRunnerCoreConfig config;

    // ── State ─────────────────────────────────────────────────────────────────
    private float _forwardOffset;    // distance-space, 0 <= forwardOffset <= config.maxSurge
    private float _lateralOffset;    // path-local right units (negative = left)
    private float _lateralVelocity;  // path-local right units/sec — carried through the air when !allowAirControl
    private float _verticalVelocity; // real gravity-integrated vertical speed
    private bool  _running;

    // ── Fall state ────────────────────────────────────────────────────────────
    private bool    _isFalling;
    private Vector3 _fallVelocity;

    // ── Controller ────────────────────────────────────────────────────────────
    private CharacterController _cc;

    // ── Public API ─────────────────────────────────────────────────────────────
    public bool  IsRunning          => _running;
    public bool  IsFalling          => _isFalling;
    public float ForwardOffset      => _forwardOffset;
    public float LateralOffset      => _lateralOffset;
    public float VerticalVelocity   => _verticalVelocity;
    public bool  IsGrounded         => _cc != null && _cc.isGrounded;
    public float MaxForwardDistance => config.maxSurge;

    /// <summary>Where the player belongs RIGHT NOW according to the music — single source of
    /// truth for "songTime/MusicDistance → longitudinal position". Nobody else should
    /// re-derive this from MusicClock directly.</summary>
    public float CanonicalDistance => MusicClock.Instance?.MusicDistance ?? 0f;

    /// <summary>Where the player REALLY is, including surge. What GameplayManager's spawn/
    /// recycle windows and the debug HUD should read instead of MusicDistance + ForwardOffset.</summary>
    public float ActualDistance => CanonicalDistance + _forwardOffset;

    // The track's real half-width at the player's own distance, computed once per frame in
    // UpdateLateralOffset — a REFERENCE value only (HUD/debug, IsAtLateralLimit below); it does
    // NOT clamp _lateralOffset. How far the player can actually get outside it is pure physics
    // (strafeSpeed/jumpForce/gravity/air time), and being past it is not, by itself, a fall — see
    // IsBelowTrackSurface.
    public float LateralLimit     { get; private set; }
    public bool  IsAtLateralLimit { get; private set; }

    /// <summary>
    /// True once the player has sunk below the TRACK's own local surface plane by more than
    /// config.fallDeathDepth — the single authoritative fall signal FallRespawnSystem reads.
    /// Uses the path's local frame (sample.position/sample.up at the player's own distance), not
    /// a world Y, so it stays correct on slopes/hills/future curves. Being laterally outside the
    /// track (IsAtLateralLimit) but still above this plane — e.g. airborne over the void,
    /// correctable with air control — does NOT count as a fall.
    /// </summary>
    public bool IsBelowTrackSurface { get; private set; }

    // Purely so CameraFollow can hide the player's own body in First Person without hardcoding
    // anything about the current provisional capsule — whatever BuildVisual() ends up creating
    // (capsule today, a real avatar later) is exposed here automatically, no GameObject.Find.
    public Renderer[] VisualRenderers { get; private set; } = System.Array.Empty<Renderer>();

    public void StartRunning() => _running = true;
    public void StopRunning()  => _running = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        BuildController();
        BuildVisual();
    }

    private void Update()
    {
        if (!_running) return;

        if (_isFalling)
        {
            UpdateFall();
            return;
        }

        var clock = MusicClock.Instance;
        var path  = MusicWorldManager.Instance?.Path;
        if (clock == null || path == null) return;

        // ── 1. Longitudinal: forwardOffset never negative → never behind CanonicalDistance ──
        UpdateForwardOffset();
        var sample = path.GetSample(ActualDistance);

        // ── 2. Lateral: clamped by the SAME sample used for the final position ─────────────
        UpdateLateralOffset(sample.width);

        // ── 3. Vertical: pure gravity/jump state, no position writes yet ───────────────────
        UpdateVertical();

        // ── 4. Single motion authority ──────────────────────────────────────────────────────
        ApplyMotion(sample);

        // ── 5. Fall check: below the TRACK's local surface plane, not the lateral limit ─────
        // Reaching IsAtLateralLimit (still computed above, just no longer used for this) no
        // longer confirms a fall by itself — being laterally outside the track but still above
        // it (e.g. mid-air over the void, correctable with air control) is fine.
        UpdateIsBelowTrackSurface(sample);
    }

    // ── Longitudinal ─────────────────────────────────────────────────────────────

    private void UpdateForwardOffset()
    {
        bool readTouch    = PlatformService.IsMobile || PlatformService.DualInputInEditor;
        bool readKeyboard = !PlatformService.IsMobile || PlatformService.DualInputInEditor;

        bool surging = false;
        if (readTouch) surging |= TouchInputState.Surging;
        if (readKeyboard)
        {
            var kb = Keyboard.current;
            surging |= kb != null && (kb.wKey.isPressed || kb.upArrowKey.isPressed);
        }

        _forwardOffset = surging
            ? Mathf.MoveTowards(_forwardOffset, config.maxSurge, config.surgeSpeed * Time.deltaTime)
            : Mathf.MoveTowards(_forwardOffset, 0f, config.surgeDecay * Time.deltaTime);

        _forwardOffset = Mathf.Clamp(_forwardOffset, 0f, config.maxSurge);
    }

    // ── Lateral ──────────────────────────────────────────────────────────────────

    private void UpdateLateralOffset(float pathWidth)
    {
        // Reference only — the real edge, no artificial extra margin. Does not clamp anything
        // below; see LateralLimit's own doc comment.
        LateralLimit = pathWidth * 0.5f;

        bool grounded = _cc.isGrounded;

        // Lateral input works the same regardless of WHY the player is airborne — jumped, ran
        // off a steep downhill bump and briefly lost ground contact, whatever. Grounded: always
        // full control. Airborne: full control too, but only if allowAirControl is on; if it's
        // off, input is ignored and whatever velocity was last set while grounded just keeps
        // applying unchanged (no decay, no new player-driven direction change) until landing.
        if (grounded || config.allowAirControl)
        {
            bool readTouch    = PlatformService.IsMobile || PlatformService.DualInputInEditor;
            bool readKeyboard = !PlatformService.IsMobile || PlatformService.DualInputInEditor;

            float dir = 0f;
            if (readTouch) dir = Mathf.Clamp(TouchInputState.Lateral, -1f, 1f);
            // Keyboard only drives this when the joystick isn't actively doing so — combining both
            // additively would double the effective speed while a developer holds both at once.
            if (readKeyboard && Mathf.Approximately(dir, 0f))
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  dir -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir += 1f;
                }
            }
            _lateralVelocity = dir * config.strafeSpeed;
        }

        // Unclamped — how far this can actually carry the player is pure physics (strafeSpeed
        // while it applies, jumpForce/gravity/air time bounding how long that lasts), not a limit
        // imposed here.
        _lateralOffset += _lateralVelocity * Time.deltaTime;

        IsAtLateralLimit = Mathf.Abs(_lateralOffset) >= LateralLimit;
    }

    // ── Vertical ─────────────────────────────────────────────────────────────────

    private void UpdateVertical()
    {
        bool grounded = _cc.isGrounded;

        bool readTouch    = PlatformService.IsMobile || PlatformService.DualInputInEditor;
        bool readKeyboard = !PlatformService.IsMobile || PlatformService.DualInputInEditor;

        bool jumpPressed = false;
        if (readTouch)
        {
            jumpPressed = TouchInputState.JumpRequested;
            TouchInputState.JumpRequested = false; // edge-triggered — consume it the same frame
        }
        if (readKeyboard)
        {
            var kb = Keyboard.current;
            jumpPressed |= kb != null && kb.spaceKey.wasPressedThisFrame;
        }

        if (grounded && jumpPressed)
            _verticalVelocity = config.jumpForce;

        _verticalVelocity += config.gravity * Time.deltaTime;

        if (grounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f; // small constant downward push keeps isGrounded reliable on slopes
    }

    // ── Single motion authority: exactly one CharacterController.Move() per frame ──────────

    private void ApplyMotion(in MusicPath.Sample sample)
    {
        Vector3 targetXZ         = sample.position + sample.right * _lateralOffset;
        Vector3 horizontalDelta  = targetXZ - transform.position;
        horizontalDelta.y = 0f;

        Vector3 motion = horizontalDelta + Vector3.up * _verticalVelocity * Time.deltaTime;
        _cc.Move(motion);

        transform.rotation = Quaternion.LookRotation(sample.tangent, Vector3.up);
    }

    // ── Fall detection (vertical, track-relative — NOT the lateral limit) ──────────────────

    private void UpdateIsBelowTrackSurface(in MusicPath.Sample sample)
    {
        // Height relative to the TRACK's own local plane at this distance (position + up), not a
        // world Y — stays correct on slopes/hills/future curves. Standing normally on the (frequency-
        // driven, possibly bumpy) walkable surface gives a small POSITIVE value here (the surface
        // sits above the centerline); only sinking fallDeathDepth below the centerline itself counts.
        float signedHeight = Vector3.Dot(transform.position - sample.position, sample.up);
        IsBelowTrackSurface = signedHeight < -config.fallDeathDepth;
    }

    // ── Fall state ────────────────────────────────────────────────────────────

    public void EnterFallState()
    {
        _isFalling    = true;
        _fallVelocity = Vector3.zero;
        if (_cc != null) _cc.enabled = false; // hand off to manual world-space fall movement below
    }

    public void ExitFallState()
    {
        ResetMotionState();
        _isFalling = false;
        if (_cc != null) _cc.enabled = true;
    }

    public void Respawn(Vector3 worldPos, Quaternion rotation)
    {
        ResetMotionState();
        _isFalling = false;

        // Disable while teleporting — CharacterController resists having its transform moved
        // a large distance directly while active.
        if (_cc != null) _cc.enabled = false;
        transform.position = worldPos;
        transform.rotation = rotation;
        if (_cc != null) _cc.enabled = true;
    }

    /// <summary>
    /// Teleports the player to wherever CanonicalDistance says it belongs RIGHT NOW (sampling the
    /// real walkable surface, not the path centerline) — the single respawn/restart entry point.
    /// FallRespawnSystem calls this instead of recomputing songTime*speed and the surface-sample
    /// logic itself; it only needs to resync MusicClock to the right songTime FIRST (so
    /// CanonicalDistance reads correctly), then call this.
    /// </summary>
    public void SnapToCanonicalPosition()
    {
        var world = MusicWorldManager.Instance;
        var path  = world?.Path;
        if (path == null) return;

        float dist   = CanonicalDistance;
        var   sample = path.GetSample(dist);
        // Surface, not centerline — the walkable surface sits above it by the frequency-driven relief.
        Vector3 pos = world != null ? world.SampleSurface(dist, 0f).position : sample.position;
        Respawn(pos, Quaternion.LookRotation(sample.tangent, Vector3.up));
    }

    // Resets every piece of per-frame motion state so the frame right after a respawn/fall-exit
    // computes forwardOffset/lateralOffset/verticalVelocity fresh instead of chasing stale values.
    private void ResetMotionState()
    {
        _fallVelocity     = Vector3.zero;
        _forwardOffset    = 0f;
        _lateralOffset    = 0f;
        _lateralVelocity  = 0f;
        _verticalVelocity    = 0f;
        LateralLimit         = 0f;
        IsAtLateralLimit     = false;
        IsBelowTrackSurface  = false;
    }

    private void UpdateFall()
    {
        // World-space free-fall (CharacterController disabled — see EnterFallState)
        _fallVelocity      += Vector3.up * config.gravity * Time.deltaTime;
        transform.position += _fallVelocity * Time.deltaTime;
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    private void BuildController()
    {
        _cc             = gameObject.AddComponent<CharacterController>();
        _cc.center      = new Vector3(0f, 0.75f, 0f);
        _cc.radius      = 0.45f;
        _cc.height      = 1.5f;
        _cc.slopeLimit  = 45f;
        _cc.stepOffset  = 0.3f;
        _cc.skinWidth   = 0.04f;
    }

    // Player Visual Theme (see the app-flow/Theme refactor's "Player Theme" phase): everything
    // above this point is gameplay logic and never changes per-Theme. The VISUAL alone is
    // swappable — a themed prefab (PlayerStyleSO.playerVisualPrefab) is instantiated under a
    // dedicated VisualAnchor child instead of PlayerController's own transform directly, so a
    // future real avatar never needs its own copy of PlayerController (per the plan's explicit
    // "no PlayerController duplicat dins de cada avatar"). No theme content authored yet (see
    // PlayerStyle_Base.asset) falls straight through to the exact same hardcoded placeholder
    // capsule this already used — zero behavior change today.
    private Transform _visualAnchor;

    private void BuildVisual()
    {
        var anchorGO = new GameObject("VisualAnchor");
        anchorGO.transform.SetParent(transform, false);
        _visualAnchor = anchorGO.transform;

        var themedPrefab = ThemeManager.Instance != null ? ThemeManager.Instance.CurrentTheme?.Player?.playerVisualPrefab : null;
        if (themedPrefab != null)
            Instantiate(themedPrefab, _visualAnchor, false);
        else
            BuildPlaceholderCapsuleVisual();

        VisualRenderers = GetComponentsInChildren<Renderer>(true);
    }

    // The ORIGINAL, unthemed placeholder — untouched behavior, just parented under _visualAnchor
    // instead of directly under the player root so a themed prefab can slot into the exact same
    // spot.
    private void BuildPlaceholderCapsuleVisual()
    {
        var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cap.transform.SetParent(_visualAnchor, false);
        cap.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        cap.transform.localScale    = new Vector3(0.7f, 0.75f, 0.7f);
        Destroy(cap.GetComponent<CapsuleCollider>());

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.25f, 0.65f, 1f) };
        cap.GetComponent<MeshRenderer>().material = mat;
        cap.name = "Visual";
    }
}
