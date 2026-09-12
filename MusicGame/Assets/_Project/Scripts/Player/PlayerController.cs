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
///   forwardOffset  ∈ [0, config.maxSurge]         — distance ahead of the music; eases back
///                                                    to 0 when not pressed, never negative
///                                                    (so playerDistance can never fall behind
///                                                    MusicClock.MusicDistance)
///   playerDistance = MusicClock.MusicDistance + forwardOffset
///   sample         = MusicPath.GetSample(playerDistance)   — ONE sample/frame; position,
///                                                    right, up and width all come from it
///   lateralOffset  ∈ [-limit, +limit] where limit = sample.width/2 + pathFallMargin
///                                                    (same sample as above — the lateral
///                                                    limit is part of this authoritative
///                                                    calc, not a separate clamp elsewhere)
///   verticalVelocity — real gravity/jump, resolved by the SAME Move() call against
///                                                    MusicWorldManager's real ground collider
///
/// Fall state (EnterFallState/ExitFallState) and Respawn() are the only exceptions: they are
/// explicit, event-driven handoffs from FallRespawnSystem, and they fully reset forwardOffset/
/// lateralOffset/verticalVelocity so the next frame doesn't fight its way back from stale state.
/// </summary>
public class PlayerController : MonoBehaviour
{
    [SerializeField] private GameplayConfig config;

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

    // Local-width-aware lateral bound, computed once per frame in UpdateLateralOffset —
    // the single authoritative source FallRespawnSystem reads instead of recomputing it.
    public float LateralLimit     { get; private set; }
    public bool  IsAtLateralLimit { get; private set; }

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

        // ── 1. Longitudinal: forwardOffset never negative → never behind the music ─────────
        UpdateForwardOffset();
        float playerDistance = clock.MusicDistance + _forwardOffset;
        var   sample         = path.GetSample(playerDistance);

        // ── 2. Lateral: clamped by the SAME sample used for the final position ─────────────
        UpdateLateralOffset(sample.width);

        // ── 3. Vertical: pure gravity/jump state, no position writes yet ───────────────────
        UpdateVertical();

        // ── 4. Single motion authority ──────────────────────────────────────────────────────
        ApplyMotion(sample);
    }

    // ── Longitudinal ─────────────────────────────────────────────────────────────

    private void UpdateForwardOffset()
    {
        var kb = Keyboard.current;
        bool surging = kb != null && (kb.wKey.isPressed || kb.upArrowKey.isPressed);

        _forwardOffset = surging
            ? Mathf.MoveTowards(_forwardOffset, config.maxSurge, config.surgeSpeed * Time.deltaTime)
            : Mathf.MoveTowards(_forwardOffset, 0f, config.surgeDecay * Time.deltaTime);

        _forwardOffset = Mathf.Clamp(_forwardOffset, 0f, config.maxSurge);
    }

    // ── Lateral ──────────────────────────────────────────────────────────────────

    private void UpdateLateralOffset(float pathWidth)
    {
        LateralLimit = pathWidth * 0.5f + config.pathFallMargin;

        // Grounded (or allowAirControl=true): full direct control from input, same as always —
        // and this is also where _lateralVelocity is kept up to date, so a jump taken mid-strafe
        // has a real velocity ready to carry into the air below.
        // Airborne with allowAirControl=false: input is ignored entirely — whatever velocity was
        // last set while grounded keeps applying unchanged (no decay, no new player-driven
        // acceleration/direction change) until landing.
        if (config.allowAirControl || _cc.isGrounded)
        {
            var kb = Keyboard.current;
            float dir = 0f;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  dir -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir += 1f;
            }
            _lateralVelocity = dir * config.strafeSpeed;
        }

        _lateralOffset = Mathf.Clamp(
            _lateralOffset + _lateralVelocity * Time.deltaTime,
            -LateralLimit, LateralLimit);

        IsAtLateralLimit = Mathf.Abs(_lateralOffset) >= LateralLimit - 0.001f;
    }

    // ── Vertical ─────────────────────────────────────────────────────────────────

    private void UpdateVertical()
    {
        var kb       = Keyboard.current;
        bool grounded = _cc.isGrounded;

        if (grounded && kb != null && kb.spaceKey.wasPressedThisFrame)
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

    // Resets every piece of per-frame motion state so the frame right after a respawn/fall-exit
    // computes forwardOffset/lateralOffset/verticalVelocity fresh instead of chasing stale values.
    private void ResetMotionState()
    {
        _fallVelocity     = Vector3.zero;
        _forwardOffset    = 0f;
        _lateralOffset    = 0f;
        _lateralVelocity  = 0f;
        _verticalVelocity = 0f;
        LateralLimit      = 0f;
        IsAtLateralLimit  = false;
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

    private void BuildVisual()
    {
        var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cap.transform.SetParent(transform);
        cap.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        cap.transform.localScale    = new Vector3(0.7f, 0.75f, 0.7f);
        Destroy(cap.GetComponent<CapsuleCollider>());

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.25f, 0.65f, 1f) };
        cap.GetComponent<MeshRenderer>().material = mat;
        cap.name = "Visual";

        VisualRenderers = GetComponentsInChildren<Renderer>(true);
    }
}
