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
/// COMBAT PLANE (see FighterActor.ForwardXZ/SideXZ's own doc): every horizontal displacement this
/// class ever applies — walk, run, dash, backdash, lunge, sidestep, sidewalk — is expressed as
/// `forwardAmount * _actor.ForwardXZ + sideAmount * _actor.SideXZ`, never a raw world-X delta. This
/// is what makes "Forward/Back" and "Side" stay meaningful once fighters occupy a small depth range
/// (FightArenaConfig.minDepth/maxDepth) instead of a single world axis — a real fighting-game-style
/// limited 3D combat plane, deliberately never free 8-way roaming (see class doc's own scope note).
///
/// OWNS both FighterPosture (Standing/Crouching/Airborne) and FighterMovementState (Idle/Walk/Dash/
/// Run/SideStep/SideWalk) — see each enum's own doc — writing them onto FighterActor so every other
/// system (Move System context checks, hit resolution's hurtbox height, future AI/animation) reads
/// the SAME single source instead of re-deriving it.
///
/// CROUCH/JUMP/SIDESTEP/SIDEWALK are plain input-driven state, not FightMoveDefinitions — DASH and
/// BACKDASH are (see DashForwardMoveId/BackdashMoveId's own doc): both are real moves (a lunge + a
/// brief locked Startup/Active/Recovery), triggered by the EXISTING combo recognizer detecting
/// "Forward,Forward"/"Back,Back" as two buffered, button-less taps (see FighterInputController's own
/// doc on FightButton.None) — this class never re-implements that detection, it only WATCHES for
/// dash-forward's own FightComboDetectedEvent to know when to start counting "is Forward still held"
/// for RUN (backdash has no such hold-to-run equivalent).
///
/// TAP vs HOLD (Up/Down — see HandleVerticalDirection's own doc): Up/Down now serve THREE purposes
/// depending on timing — a short tap sidesteps, a hold jumps/crouches, and a second same-direction
/// tap that's then HELD becomes a continuous SideWalk instead. Down+Back is ALWAYS resolved as
/// instant Crouch-intent, bypassing this whole tap/hold machinery entirely, so it can never race
/// against or accidentally hijack FighterGuard's own Down+Back Crouch Guard (task's own explicit
/// "no vull que dispari accidentalment un sidestep" priority rule).
///
/// CAN-DO GATES (task's own explicit ask): CanMove/CanCrouch/CanJump are all derived from the SAME
/// `_locked` flag every move/hit-stun/block-stun/KO already funnels through (see SetLock's own doc)
/// plus `_active` (FightFlowState.Fighting) — no separate, parallel "am I allowed to act" state
/// exists anywhere else. Sidestep/SideWalk reuse the exact same CanMove gate.
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
    /// <summary>Must match Configs/Fight/Move_Backdash.asset — same "Dash" display state as the
    /// forward dash (see UpdateMovementState), but never arms Run-tracking (there is no "hold Back
    /// to run backward" equivalent this phase).</summary>
    private const string BackdashMoveId = "move_backdash";

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

    private bool _running;

    // ── Vertical direction tap/hold/double-tap state — see HandleVerticalDirection's own doc ─────
    private FightVerticalDirection _verticalPressDirection = FightVerticalDirection.Neutral;
    private float _verticalPressStart;
    private bool _verticalHoldResolved;
    private FightVerticalDirection _pendingSideWalkArm = FightVerticalDirection.Neutral;
    private float _pendingSideWalkArmExpiry;

    // ── Sidestep — a short, timed dodge; see StartSidestep's own doc ─────────────────────────────
    private bool _sidestepActive;
    private float _sidestepElapsed;
    private int _sidestepDirection; // -1 / 0 / +1, along _actor.SideXZ
    private float _sidestepReadyTime;

    // ── SideWalk — continuous while armed; see HandleVerticalDirection's own doc ─────────────────
    private int _sideWalkDirection; // -1 / 0 / +1, along _actor.SideXZ

    /// <summary>Not read by anything yet — the seam FighterStats.Speed would multiply once real
    /// combat balancing exists (see class doc). Always 1 this phase.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    public bool IsLocked => _locked;
    public float Multiplier => _multiplier;
    public float LastLungeDistance { get; private set; }

    /// <summary>-1 (dodging along -SideXZ) / 0 (none) / +1 (along +SideXZ) — see FightDebugHUD's own
    /// doc on why this is exposed: purely informational, nothing reads it as an input.</summary>
    public int SidestepDirection => _sidestepActive ? _sidestepDirection : 0;
    /// <summary>True while a SideWalk is actively carrying the fighter sideways — see
    /// FightDebugHUD's own doc.</summary>
    public bool IsSideWalking => _sideWalkDirection != 0;

    /// <summary>See class doc on the CAN-DO gates.</summary>
    public bool CanMove => _active && !_locked;
    public bool CanCrouch => CanMove && !_isJumping && _sideWalkDirection == 0;
    public bool CanJump => CanMove && !_isJumping &&
        (_actor != null && (_actor.Posture == FighterPosture.Standing || _actor.Posture == FighterPosture.Crouching));
    private bool CanSidestepOrSideWalk => CanMove && !_isJumping && _actor.Posture != FighterPosture.Crouching;

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
    /// CanJump/sidestep/sidewalk all read this one flag.</summary>
    public void SetLock(bool locked, float multiplier)
    {
        _locked     = locked;
        _multiplier = multiplier;
        // Getting hit/locked cancels an in-flight sidestep/sidewalk outright — task's own explicit
        // "no podem ignorar HitStun/BlockStun/KO" requirement — rather than letting it play out.
        if (locked)
        {
            _sidestepActive = false;
            _sideWalkDirection = 0;
        }
    }

    /// <summary>Called by RealFighterMovementDriver.ApplyLunge — accumulates in case more than one
    /// arrives the same frame (shouldn't happen with today's data, but stays correct if it ever
    /// does). Consumed (and clamped against bounds/separation) on the very next Update. Sign
    /// matters: a NEGATIVE lungeDistance (see Move_Backdash.asset) lunges BACKWARD along
    /// _actor.ForwardXZ instead of forward — no separate backward-lunge field needed.</summary>
    public void QueueLunge(float distance) => _pendingLunge += distance;

    /// <summary>Explicit API for FightMatchController's between-rounds reset (via FighterActor.
    /// ResetForRound) — clears any lock (including a KO's permanent one — see FighterHealth's own
    /// doc), any pending knockback/lunge, and every locomotion/posture state (jump arc, run
    /// tracking, sidestep/sidewalk/tap-hold bookkeeping) so a new round always starts from a clean
    /// Standing/Idle baseline.</summary>
    public void ResetForRound()
    {
        _locked = false;
        _multiplier = 1f;
        _pendingLunge = 0f;
        LastLungeDistance = 0f;

        _isJumping = false;
        _verticalVelocity = 0f;
        _running = false;
        _groundYCaptured = false; // recaptured next Update, from the fresh spawn position already applied by FighterActor

        _verticalPressDirection = FightVerticalDirection.Neutral;
        _verticalHoldResolved = false;
        _pendingSideWalkArm = FightVerticalDirection.Neutral;
        _sidestepActive = false;
        _sidestepDirection = 0;
        _sidestepReadyTime = 0f;
        _sideWalkDirection = 0;
    }

    private void OnEnable()
    {
        _onFightFlowChanged = e => _active = e.Current == FightFlowState.Fighting;
        // Arms Run-tracking the instant the dash-forward move fires — see HandleRunTracking's own
        // doc for why this must NOT also require !_locked (the dash's own brief lock would
        // otherwise immediately disarm it before it ever had a chance to matter). Backdash never
        // arms this — there is no "hold Back to run backward" this phase.
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

        HandleVerticalDirection();
        HandleRunTracking();

        // ── Forward/Back — relative to the live line between fighters, never world X directly ────
        float forwardAmount = 0f;
        if (!_locked && _actor.Posture != FighterPosture.Crouching && _sideWalkDirection == 0)
        {
            switch (_input.CurrentHorizontal)
            {
                case FightHorizontalDirection.Forward: forwardAmount =  1f; break;
                case FightHorizontalDirection.Back:    forwardAmount = -1f; break;
            }
        }

        float speed = (_config != null ? _config.baseMovementSpeed : 4f) * SpeedMultiplier;
        if (_running) speed *= _config != null ? _config.runSpeedMultiplier : 1.6f;
        float airControl = _actor.Posture == FighterPosture.Airborne ? (_config != null ? _config.airControlMultiplier : 0.5f) : 1f;

        Vector3 delta = _actor.ForwardXZ * (forwardAmount * speed * _multiplier * airControl * Time.deltaTime);

        // ── Side — SideWalk (continuous, held) or Sidestep (a short timed dodge) ─────────────────
        if (_sideWalkDirection != 0)
        {
            float sidewalkSpeed = _config != null ? _config.sidewalkSpeed : 2.2f;
            delta += _actor.SideXZ * (_sideWalkDirection * sidewalkSpeed * _multiplier * airControl * Time.deltaTime);
        }
        delta += ComputeSidestepDelta();

        if (_pendingLunge != 0f)
        {
            delta += _actor.ForwardXZ * _pendingLunge;
            LastLungeDistance = _pendingLunge;
            _pendingLunge = 0f;
        }

        if (delta.sqrMagnitude > 0.0000001f) ApplyClampedDelta(delta);

        UpdateVerticalPhysics();
        UpdateMovementState(forwardAmount);
    }

    // ── Crouch (instant Down+Back guard case handled inline in HandleVerticalDirection) ─────────

    private void ApplyCrouchPosture(bool wantsCrouch)
    {
        if (!CanCrouch) return; // holds whatever posture it already had — task's own explicit
                                  // "si cap move/hit stun ho impedeix" (no forced transition here)
        if (wantsCrouch && _actor.Posture == FighterPosture.Standing) _actor.SetPosture(FighterPosture.Crouching);
        else if (!wantsCrouch && _actor.Posture == FighterPosture.Crouching) _actor.SetPosture(FighterPosture.Standing);
    }

    // ── Jump ──────────────────────────────────────────────────────────────────

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

    // ── Vertical direction: tap → Sidestep, hold → Jump/Crouch, double-tap+hold → SideWalk ──────
    //
    // PRIORITY (task's own explicit "commands combinats de combat/guard > sidestep tap simple"
    // rule): Down+Back is checked FIRST and unconditionally resolves as instant Crouch-intent —
    // FighterGuard's own Down+Back Crouch Guard reads CurrentHorizontal/CurrentVertical directly
    // and doesn't care about ANY of this tap/hold bookkeeping, but the fighter's visible POSTURE
    // still needs to crouch instantly too (a High attack must actually whiff over a "Crouch
    // Guard"-ing fighter, which only happens if their hurtbox really is the crouching one — see
    // FighterHurtbox's own doc) — so this bypasses the tap timer entirely rather than racing it.
    //
    // Any OTHER horizontal combination (Forward, for either Up or Down) suppresses the SIDESTEP
    // outcome specifically ("tap Up/Down SENSE combinació" — task's own wording) but still allows
    // the normal HOLD outcome (Jump/Crouch) once the threshold elapses — a forward-jump or a
    // forward+down crouch both keep working exactly as before.
    private void HandleVerticalDirection()
    {
        if (!CanMove)
        {
            _verticalPressDirection = FightVerticalDirection.Neutral;
            _verticalHoldResolved = false;
            _pendingSideWalkArm = FightVerticalDirection.Neutral;
            _sideWalkDirection = 0;
            return;
        }

        var vertical = _input.CurrentVertical;
        bool isDownBackGuardCombo = vertical == FightVerticalDirection.Down && _input.CurrentHorizontal == FightHorizontalDirection.Back;

        if (isDownBackGuardCombo)
        {
            _sideWalkDirection = 0;
            // Treated as an ALREADY-resolved hold (not reset to Neutral) — so if Back releases
            // while Down stays held, the very next frame sees the SAME press direction it already
            // had (no fresh-press reset), and the continuous Crouch-posture check below keeps
            // holding the crouch instead of a one-frame un-crouch/re-crouch flash while the tap/hold
            // timer needlessly restarts.
            _verticalPressDirection = FightVerticalDirection.Down;
            _verticalHoldResolved = true;
            ApplyCrouchPosture(true);
            return;
        }

        // SideWalk continues purely off whether its OWN armed direction is still held — independent
        // of the tap/hold timer below (that timer only ever governs how a FRESH press resolves).
        if (_sideWalkDirection != 0)
        {
            bool stillHeld = (_sideWalkDirection > 0 && vertical == FightVerticalDirection.Up) ||
                              (_sideWalkDirection < 0 && vertical == FightVerticalDirection.Down);
            if (!stillHeld) _sideWalkDirection = 0; // released, opposite, or neutral — cancel (class doc: "cancel·la en soltar")
        }

        if (vertical == FightVerticalDirection.Neutral)
        {
            // Released — a genuine short TAP (never reached the hold threshold) fires Sidestep now.
            if (_verticalPressDirection != FightVerticalDirection.Neutral && !_verticalHoldResolved)
            {
                bool combined = _input.CurrentHorizontal != FightHorizontalDirection.Neutral;
                if (!combined) TryFireSidestepAndArmSideWalk(_verticalPressDirection);
            }
            _verticalPressDirection = FightVerticalDirection.Neutral;
            _verticalHoldResolved = false;
            ApplyCrouchPosture(false);
            return;
        }

        if (vertical != _verticalPressDirection)
        {
            _verticalPressDirection = vertical;
            _verticalPressStart = Time.time;
            _verticalHoldResolved = false;
        }

        if (_sideWalkDirection != 0) return; // already sidewalking this exact direction — resolved

        float threshold = _config != null ? Mathf.Max(0.02f, _config.directionHoldThreshold) : 0.15f;
        if (!_verticalHoldResolved && Time.time - _verticalPressStart >= threshold)
        {
            _verticalHoldResolved = true;
            bool horizontalCombo = _input.CurrentHorizontal != FightHorizontalDirection.Neutral;
            bool isSecondTapArmed = _pendingSideWalkArm == vertical && Time.time <= _pendingSideWalkArmExpiry;

            if (vertical == FightVerticalDirection.Up)
            {
                if (isSecondTapArmed && !horizontalCombo)
                {
                    _sideWalkDirection = 1;
                    _pendingSideWalkArm = FightVerticalDirection.Neutral;
                }
                else if (CanJump) StartJump();
            }
            else // Down
            {
                if (isSecondTapArmed && !horizontalCombo)
                {
                    _sideWalkDirection = -1;
                    _pendingSideWalkArm = FightVerticalDirection.Neutral;
                }
                // else: falls through to the continuous Crouch-posture update below, same as before.
            }
        }

        // Crouch posture is a continuous LEVEL (not a one-shot) — active whenever Down has resolved
        // past the tap window and isn't currently SideWalking. This mirrors the pre-existing
        // instant-crouch feel closely, since directionHoldThreshold is short by design (see
        // FightArenaConfig's own doc) — a genuine short tap still resolves as Sidestep instead.
        bool wantsCrouch = vertical == FightVerticalDirection.Down && _verticalHoldResolved && _sideWalkDirection == 0;
        ApplyCrouchPosture(wantsCrouch);
    }

    private void TryFireSidestepAndArmSideWalk(FightVerticalDirection tappedDirection)
    {
        int sideSign = tappedDirection == FightVerticalDirection.Up ? 1 : -1;
        StartSidestep(sideSign);

        // A SECOND same-direction tap within doubleTapWindow — consumed either way (never a
        // triple-tap chain); if it's THIS press being consumed as the second half, the next hold
        // resolution above turns into SideWalk instead of Jump/Crouch (see class doc).
        bool isSecondTap = _pendingSideWalkArm == tappedDirection && Time.time <= _pendingSideWalkArmExpiry;
        if (isSecondTap)
        {
            _pendingSideWalkArm = FightVerticalDirection.Neutral;
        }
        else
        {
            float window = _config != null ? Mathf.Max(0.05f, _config.doubleTapWindow) : 0.3f;
            _pendingSideWalkArm = tappedDirection;
            _pendingSideWalkArmExpiry = Time.time + window;
        }
    }

    // ── Sidestep — a short, timed dodge along SideXZ; see class doc ──────────────────────────────

    private void StartSidestep(int sideSign)
    {
        if (!CanSidestepOrSideWalk) return;
        if (Time.time < _sidestepReadyTime) return; // cooldown/recovery — see FightArenaConfig.sidestepRecovery's own doc

        _sidestepActive = true;
        _sidestepElapsed = 0f;
        _sidestepDirection = sideSign;

        float duration = _config != null ? Mathf.Max(0.05f, _config.sidestepDuration) : 0.18f;
        float recovery = _config != null ? Mathf.Max(0f, _config.sidestepRecovery) : 0.12f;
        _sidestepReadyTime = Time.time + duration + recovery;
    }

    private Vector3 ComputeSidestepDelta()
    {
        if (!_sidestepActive) return Vector3.zero;
        if (_locked) { _sidestepActive = false; return Vector3.zero; } // see SetLock's own doc — redundant safety net, already cleared there too

        float duration = _config != null ? Mathf.Max(0.05f, _config.sidestepDuration) : 0.18f;
        float distance = _config != null ? _config.sidestepDistance : 1.2f;

        float remaining = duration - _sidestepElapsed;
        float step = Mathf.Min(Time.deltaTime, Mathf.Max(0f, remaining));
        _sidestepElapsed += Time.deltaTime;
        if (_sidestepElapsed >= duration) _sidestepActive = false;

        return _actor.SideXZ * (_sidestepDirection * (distance / duration) * step);
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

    private void UpdateMovementState(float forwardAmount)
    {
        var currentMoveId = _locked && _actor.MoveController != null && _actor.MoveController.CurrentMove != null
            ? _actor.MoveController.CurrentMove.id
            : null;
        bool isDashing = currentMoveId == DashForwardMoveId || currentMoveId == BackdashMoveId;

        FighterMovementState newState =
            isDashing              ? FighterMovementState.Dash :
            _sideWalkDirection != 0 ? FighterMovementState.SideWalk :
            _sidestepActive        ? FighterMovementState.SideStep :
            _running               ? FighterMovementState.Run  :
            forwardAmount != 0f    ? FighterMovementState.Walk :
                                      FighterMovementState.Idle;

        _actor.SetMovementState(newState);
    }

    private void ApplyClampedDelta(Vector3 delta)
    {
        Vector3 opponentPos = _opponent != null ? _opponent.transform.position : _actor.transform.position;
        _actor.transform.position = FightMovementUtility.ClampXZ(_actor.transform.position, delta, opponentPos, _config);
    }

    // ── Gizmos — depth bounds + this fighter's own forward/side axis (see this phase's own doc) ──

    private void OnDrawGizmos()
    {
        if (!FightCombatDebugVisuals.Enabled || _actor == null) return;

        float minX = _config != null ? _config.minBoundX : -4f;
        float maxX = _config != null ? _config.maxBoundX : 4f;
        float minZ = _config != null ? _config.minDepth : -1.5f;
        float maxZ = _config != null ? _config.maxDepth : 1.5f;
        float y = _actor.transform.position.y;

        Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
        Vector3 a = new Vector3(minX, y, minZ), b = new Vector3(maxX, y, minZ);
        Vector3 c = new Vector3(maxX, y, maxZ), d = new Vector3(minX, y, maxZ);
        Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);

        Vector3 pos = _actor.transform.position;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(pos, pos + _actor.ForwardXZ * 1.5f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(pos, pos + _actor.SideXZ * 1.5f);
    }
}
