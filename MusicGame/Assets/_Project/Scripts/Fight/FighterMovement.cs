using UnityEngine;

/// <summary>
/// The Fighter's REAL locomotion — the concrete thing RealFighterMovementDriver forwards
/// FighterMoveController's lock/multiplier/lunge calls into (see that class's own doc for the
/// interface boundary it implements). Reads Forward/Back/Up/Down ONLY from the already-abstracted
/// FighterInputController (see IFightInputSource's own doc) — never touches keyboard/joystick/UI
/// directly, so a future AIFightInputSource-driven Opponent needs nothing new here beyond wiring
/// its own FighterInputController-equivalent.
///
/// Moves ONLY the gameplay root (the owning FighterActor's own Transform) — see FighterActor's own
/// doc on why VisualRoot never needs to know locomotion happened.
///
/// OWNS both FighterPosture (Standing/Crouching/Airborne) and FighterMovementState (Idle/Walk/Dash/
/// Run) — see each enum's own doc — writing them onto FighterActor so every other system (Move
/// System context checks, hit resolution's hurtbox height, future AI/animation) reads the SAME
/// single source instead of re-deriving it.
///
/// CROUCH/JUMP are plain input-driven state, not FightMoveDefinitions — DASH is (see
/// DashForwardMoveId's own doc): it's a real move (lunge + a brief locked Startup/Active/Recovery),
/// triggered by the EXISTING combo recognizer detecting "Forward, Forward" as two buffered,
/// button-less taps (see FighterInputController's own doc on FightButton.None) — this class never
/// re-implements that detection, it only WATCHES for that move's own FightComboDetectedEvent to know
/// when to start counting "is Forward still held" for RUN.
///
/// CAN-DO GATES (task's own explicit ask): CanMove/CanCrouch/CanJump are all derived from the SAME
/// `_locked` flag every move/hit-stun/block-stun/KO already funnels through (see SetLock's own doc)
/// plus `_active` (FightFlowState.Fighting) — no separate, parallel "am I allowed to act" state
/// exists anywhere else.
///
/// SPEED: FightArenaConfig.baseMovementSpeed (scaled by runSpeedMultiplier/airControlMultiplier as
/// appropriate) is the only tunable this phase — not yet scaled by FighterStats.Speed (see
/// SpeedMultiplier's own doc: "no vull començar a balancejar FightStats" per an earlier phase's own
/// scope note, still honored here). Nothing here hardcodes Punch/Kick-specific numbers.
/// </summary>
public class FighterMovement : MonoBehaviour
{
    /// <summary>Must match the FightMoveDefinition.id authored for the Forward-Forward dash (see
    /// Configs/Fight/Move_DashForward.asset) — the ONE string coupling this class to that specific
    /// move, used only to (a) recognize "the dash itself is what's currently locking me" for
    /// FighterMovementState.Dash's own debug display, and (b) arm Run-tracking the instant that
    /// exact combo fires (see HandleRunTracking's own doc).</summary>
    private const string DashForwardMoveId = "move_dash_forward";

    private FighterActor _actor;
    private FighterActor _opponent;
    private FightArenaConfig _config;
    private FighterInputController _input;

    private bool _active;
    private bool _locked;
    private float _multiplier = 1f;
    private float _pendingLunge;

    private bool _groundYCaptured;
    private float _groundY;
    private bool _isJumping;
    private float _verticalVelocity;
    private FightVerticalDirection _previousVertical;

    private bool _running;

    /// <summary>Not read by anything yet — the seam FighterStats.Speed would multiply once real
    /// combat balancing exists (see class doc). Always 1 this phase.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    public bool IsLocked => _locked;
    public float Multiplier => _multiplier;
    public float LastLungeDistance { get; private set; }

    /// <summary>See class doc on the CAN-DO gates.</summary>
    public bool CanMove => _active && !_locked;
    public bool CanCrouch => CanMove && !_isJumping;
    public bool CanJump => CanMove && !_isJumping &&
        (_actor != null && (_actor.Posture == FighterPosture.Standing || _actor.Posture == FighterPosture.Crouching));

    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;
    private System.Action<FightComboDetectedEvent>    _onComboDetected;

    public void Initialize(FighterActor actor, FighterActor opponent, FightArenaConfig config, FighterInputController input)
    {
        _actor    = actor;
        _opponent = opponent;
        _config   = config;
        _input    = input;
    }

    /// <summary>Called by RealFighterMovementDriver.SetMovementLock — see FightMoveDefinition.
    /// movementLocked/movementMultiplier's own doc. This is ALSO the single authority hit
    /// stun/block stun/KO lock through (see FighterHitReaction's own doc) — CanMove/CanCrouch/
    /// CanJump all read this one flag.</summary>
    public void SetLock(bool locked, float multiplier)
    {
        _locked     = locked;
        _multiplier = multiplier;
    }

    /// <summary>Called by RealFighterMovementDriver.ApplyLunge — accumulates in case more than one
    /// arrives the same frame (shouldn't happen with today's data, but stays correct if it ever
    /// does). Consumed (and clamped against bounds/separation) on the very next Update.</summary>
    public void QueueLunge(float distance) => _pendingLunge += distance;

    /// <summary>Explicit API for FightMatchController's between-rounds reset (via FighterActor.
    /// ResetForRound) — clears any lock (including a KO's permanent one — see FighterHealth's own
    /// doc), any pending knockback/lunge, and every locomotion/posture state (jump arc, run
    /// tracking) so a new round always starts from a clean Standing/Idle baseline.</summary>
    public void ResetForRound()
    {
        _locked = false;
        _multiplier = 1f;
        _pendingLunge = 0f;
        LastLungeDistance = 0f;

        _isJumping = false;
        _verticalVelocity = 0f;
        _previousVertical = FightVerticalDirection.Neutral;
        _running = false;
        _groundYCaptured = false; // recaptured next Update, from the fresh spawn position already applied by FighterActor
    }

    private void OnEnable()
    {
        _onFightFlowChanged = e => _active = e.Current == FightFlowState.Fighting;
        // Arms Run-tracking the instant the dash-forward move fires — see HandleRunTracking's own
        // doc for why this must NOT also require !_locked (the dash's own brief lock would
        // otherwise immediately disarm it before it ever had a chance to matter).
        _onComboDetected = e =>
        {
            if (e.Source == _input && e.Combo != null && e.Combo.moveId == DashForwardMoveId) _running = true;
        };
        EventBus.Subscribe(_onFightFlowChanged);
        EventBus.Subscribe(_onComboDetected);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFightFlowChanged);
        EventBus.Unsubscribe(_onComboDetected);
    }

    private void Update()
    {
        if (!_active || _input == null || _actor == null) return;

        if (!_groundYCaptured)
        {
            _groundY = _actor.transform.position.y;
            _groundYCaptured = true;
        }

        HandleCrouch();
        HandleJumpInput();
        HandleRunTracking();

        float dirSign = 0f;
        if (!_locked && _actor.Posture != FighterPosture.Crouching)
        {
            switch (_input.CurrentHorizontal)
            {
                case FightHorizontalDirection.Forward: dirSign = _actor.FacingRight ?  1f : -1f; break;
                case FightHorizontalDirection.Back:    dirSign = _actor.FacingRight ? -1f :  1f; break;
            }
        }

        float speed = (_config != null ? _config.baseMovementSpeed : 4f) * SpeedMultiplier;
        if (_running) speed *= _config != null ? _config.runSpeedMultiplier : 1.6f;
        float airControl = _actor.Posture == FighterPosture.Airborne ? (_config != null ? _config.airControlMultiplier : 0.5f) : 1f;

        float delta = dirSign * speed * _multiplier * airControl * Time.deltaTime;

        if (_pendingLunge != 0f)
        {
            float lungeSign = _actor.FacingRight ? 1f : -1f;
            delta += lungeSign * _pendingLunge;
            LastLungeDistance = _pendingLunge;
            _pendingLunge = 0f;
        }

        if (!Mathf.Approximately(delta, 0f)) ApplyClampedDelta(delta);

        UpdateVerticalPhysics();
        UpdateMovementState(dirSign);
    }

    // ── Crouch ────────────────────────────────────────────────────────────────

    private void HandleCrouch()
    {
        if (!CanCrouch) return; // holds whatever posture it already had — task's own explicit
                                  // "si cap move/hit stun ho impedeix" (no forced transition here)

        bool wantsDown = _input.CurrentVertical == FightVerticalDirection.Down;
        if (wantsDown && _actor.Posture == FighterPosture.Standing) _actor.SetPosture(FighterPosture.Crouching);
        else if (!wantsDown && _actor.Posture == FighterPosture.Crouching) _actor.SetPosture(FighterPosture.Standing);
    }

    // ── Jump ──────────────────────────────────────────────────────────────────

    // Edge-detected locally (never buffered through FightInputBuffer/the combo recognizer) — Jump
    // is a direct locomotion action, not a combo/move trigger, exactly like Crouch/Guard reading
    // FighterInputController's level state directly.
    private void HandleJumpInput()
    {
        bool up = _input.CurrentVertical == FightVerticalDirection.Up;
        bool jumpPressed = up && _previousVertical != FightVerticalDirection.Up;
        _previousVertical = _input.CurrentVertical;

        if (jumpPressed && CanJump) StartJump();
    }

    private void StartJump()
    {
        _isJumping = true;
        _verticalVelocity = _config != null ? _config.jumpVelocity : 6f;
        _actor.SetPosture(FighterPosture.Airborne);
    }

    // Runs unconditionally whenever airborne — even through hit stun/lock, so a fighter hit mid-air
    // still falls and lands instead of floating forever (only HORIZONTAL input is gated by _locked).
    private void UpdateVerticalPhysics()
    {
        if (!_isJumping) return;

        float gravity = _config != null ? _config.gravity : 20f;
        _verticalVelocity -= gravity * Time.deltaTime;

        var pos = _actor.transform.position;
        pos.y += _verticalVelocity * Time.deltaTime;

        if (pos.y <= _groundY)
        {
            pos.y = _groundY;
            _isJumping = false;
            _verticalVelocity = 0f;
            _actor.SetPosture(FighterPosture.Standing); // always lands Standing in V1, regardless of the posture jumped from
        }

        _actor.transform.position = pos;
    }

    // ── Run tracking ──────────────────────────────────────────────────────────

    // _running is armed directly by the dash-forward FightComboDetectedEvent (see OnEnable) and
    // stays true across the dash move's own brief lock (deliberately NOT gated by !_locked here —
    // see DashForwardMoveId's own doc) until Forward is released, at which point it always clears —
    // matching "Quan deixa anar Forward -> torna a locomoció normal" exactly.
    private void HandleRunTracking()
    {
        if (_input.CurrentHorizontal != FightHorizontalDirection.Forward) _running = false;
    }

    // ── Posture-derived debug/context state ──────────────────────────────────

    private void UpdateMovementState(float dirSign)
    {
        bool isDashing = _locked && _actor.MoveController != null &&
                          _actor.MoveController.CurrentMove != null &&
                          _actor.MoveController.CurrentMove.id == DashForwardMoveId;

        FighterMovementState newState =
            isDashing        ? FighterMovementState.Dash :
            _running         ? FighterMovementState.Run  :
            dirSign != 0f    ? FighterMovementState.Walk :
                               FighterMovementState.Idle;

        _actor.SetMovementState(newState);
    }

    private void ApplyClampedDelta(float delta)
    {
        float currentX  = _actor.transform.position.x;
        float opponentX = _opponent != null ? _opponent.transform.position.x : currentX;
        float targetX   = FightMovementUtility.ClampDeltaX(currentX, delta, opponentX, _config);

        var pos = _actor.transform.position;
        pos.x = targetX;
        _actor.transform.position = pos;
    }
}
