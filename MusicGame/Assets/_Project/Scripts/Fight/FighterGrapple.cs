using UnityEngine;

public enum FighterGrappleState
{
    None,
    /// <summary>Grab whiffed / throw finished / break: short recovery, movement locked.</summary>
    Recover,
    /// <summary>Attacker holding the defender during the escape window.</summary>
    Holding,
    /// <summary>Defender being held — Punch + Kick breaks it while the window is open.</summary>
    Held,
    /// <summary>Attacker on top after a running takedown: Punch = ground strike (limited).</summary>
    GroundControl,
    /// <summary>Defender taken down and under ground control.</summary>
    Grounded,
}

/// <summary>
/// Grab / takedown / ground follow-up, without a dedicated button: the logical GRAB is Punch + Kick
/// together (FighterInputController → FightGrabRequestedEvent; the AI sends the same chord).
///
///   standing grab : in range (minimumFighterSeparation + grabRangePadding), grounded, opponent free →
///                   Holding / Held for grabEscapeWindow → THROW (unblockable runtime hit, knockdown).
///   running grab  : same, but arriving at ≥ runningGrabMinSpeed → TAKEDOWN (shorter escape window,
///                   knockdown) → GroundControl: up to groundStrikeMax Punch strikes, hard-capped at
///                   groundControlDuration; the defender may escape with Punch + Kick after
///                   groundEscapeMinTime. Related to the charge/tackle: same run speed, same hit pipeline.
///   escape        : the held defender's Punch + Kick inside the window → THROW BREAK (both pushed apart).
///   whiff         : nobody in range → grabWhiffRecovery lock. Grabs are never free or guaranteed.
/// Every state is timed and ALWAYS terminates (window / duration / KO / round reset / fight end).
/// Fighters are never teleported: the gap closes through the normal clamped knockback travel.
/// </summary>
public class FighterGrapple : MonoBehaviour
{
    private FighterActor _actor, _opponent;
    private FighterInputController _input;
    private FightArenaConfig _arena;
    private FightCombatBalanceConfig _bal;
    private FightFlowConfig _flow;
    private FightMoveDefinition _throwMove;

    public FighterGrappleState State { get; private set; } = FighterGrappleState.None;
    /// <summary>True in ANY grapple state — FighterMoveController ignores normal buttons meanwhile.</summary>
    public bool IsBusy => State != FighterGrappleState.None;
    public bool IsTakedown { get; private set; }
    public float StateTime { get; private set; }
    public int GroundStrikes { get; private set; }
    /// <summary>Held / Grounded: the window in which Punch + Kick escapes is open now.</summary>
    public bool CanEscapeNow =>
        (State == FighterGrappleState.Held && _timer > 0f) ||
        (State == FighterGrappleState.Grounded && _holder != null && _holder.StateTime >= (_bal != null ? _bal.groundEscapeMinTime : 0.6f));

    private float _timer;
    private float _nextStrike;
    private FighterGrapple _holder;   // defender side: who is holding me
    private FighterGrapple _target;   // attacker side: who I hold
    private bool _active;

    private System.Action<FightGrabRequestedEvent> _onGrab;
    private System.Action<FightNormalPunchEvent> _onPunch;
    private System.Action<FightFlowStateChangedEvent> _onFlow;

    public void Initialize(FighterActor actor, FighterActor opponent, FighterInputController input, FightArenaConfig arena)
    {
        _actor = actor; _opponent = opponent; _input = input; _arena = arena;
        var app = Resources.Load<AppConfigSO>("AppConfig");
        _bal = app != null ? app.combatBalance : null;
        _flow = app != null ? app.fightFlow : null;
    }

    private void OnEnable()
    {
        _onGrab  = e => { if (e.Source != null && e.Source == _input) OnChord(); };
        _onPunch = e => { if (e.Source != null && e.Source == _input && State == FighterGrappleState.GroundControl) TryGroundStrike(); };
        _onFlow  = e =>
        {
            _active = e.Current == FightFlowState.Fighting;
            if (!_active && IsBusy) Abort("fight state " + e.Current);
        };
        EventBus.Subscribe(_onGrab);
        EventBus.Subscribe(_onPunch);
        EventBus.Subscribe(_onFlow);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onGrab);
        EventBus.Unsubscribe(_onPunch);
        EventBus.Unsubscribe(_onFlow);
    }

    // ── Chord ────────────────────────────────────────────────────────────────

    private void OnChord()
    {
        switch (State)
        {
            case FighterGrappleState.Held:     TryBreak(); break;
            case FighterGrappleState.Grounded: TryGroundEscape(); break;
            case FighterGrappleState.None:     TryGrab(); break;
        }
    }

    private float GrabRange => (_arena != null ? _arena.minimumFighterSeparation : 1f) + (_bal != null ? _bal.grabRangePadding : 0.45f);

    /// <summary>Pure range rule (also used by the AI): within grab reach on the combat plane.</summary>
    public bool InGrabRange() => _opponent != null && Distance() <= GrabRange;

    private float Distance()
    {
        Vector3 a = _actor.transform.position, b = _opponent.transform.position;
        return new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }

    /// <summary>Can this fighter start a grab right now (own state only)?</summary>
    public bool CanStartGrab()
    {
        if (!_active || _actor == null || _opponent == null || State != FighterGrappleState.None) return false;
        if (_actor.Health != null && _actor.Health.IsKO) return false;
        if (_actor.HitReaction != null && _actor.HitReaction.State != FighterReactionState.None) return false;
        if (_actor.Posture == FighterPosture.Airborne) return false;
        var mc = _actor.MoveController;
        if (mc == null || mc.IsMatchLocked) return false;
        // Idle, a cancel window, or the very start of the chord's first-press normal (converted).
        float chord = _flow != null ? _flow.grabChordWindow : 0.08f;
        return mc.CurrentPhase == FighterMoveState.Idle || mc.CanAttack ||
               (mc.CurrentPhase == FighterMoveState.Startup && mc.MoveElapsed <= chord + 0.06f);
    }

    private bool OpponentGrabbable()
    {
        var o = _opponent;
        if (o.Health != null && o.Health.IsKO) return false;
        if (o.IsInvulnerable) return false;                       // dodge window / knockdown flow
        if (o.Posture == FighterPosture.Airborne) return false;
        if (o.Grapple != null && o.Grapple.IsBusy) return false;
        return true;
    }

    private void TryGrab()
    {
        if (!CanStartGrab()) return;
        float speed = _actor.Movement != null ? _actor.Movement.ForwardSpeed + _actor.Movement.CarryVelocity : 0f;
        _actor.MoveController.InterruptMove(); // the chord's first-press normal becomes the grab

        if (!InGrabRange() || !OpponentGrabbable())
        {
            EnterRecover(_bal != null ? _bal.grabWhiffRecovery : 0.45f);
            _actor.AnimationDriver?.PlayRole(CombatRole.LightAttack, 0.4f);
            EventBus.Publish(new FightGrappleEvent { Attacker = _actor, Defender = _opponent, Kind = FightGrappleKind.Whiff });
            return;
        }

        IsTakedown = speed >= (_bal != null ? _bal.runningGrabMinSpeed : 5f);
        float window = IsTakedown ? (_bal != null ? _bal.takedownEscapeWindow : 0.3f) : (_bal != null ? _bal.grabEscapeWindow : 0.45f);
        _target = _opponent.Grapple;
        Enter(FighterGrappleState.Holding, window);
        LockMovement(true);
        _actor.AnimationDriver?.PlayRole(IsTakedown ? CombatRole.Run : CombatRole.HeavyAttack, window);

        // Close any small gap by travel (clamped by separation) — never a teleport.
        float gap = Mathf.Max(0f, Distance() - (_arena != null ? _arena.minimumFighterSeparation : 1f));
        if (gap > 0.01f) _opponent.Movement?.QueueKnockback(-_actor.ForwardXZ * gap);
        _target?.EnterHeld(this, window);

        Debug.Log($"[FighterGrapple] {_actor.name} {(IsTakedown ? "TAKEDOWN" : "GRAB")} attempt at {speed:F1} m/s — escape window {window:F2}s");
        EventBus.Publish(new FightGrappleEvent { Attacker = _actor, Defender = _opponent, Kind = IsTakedown ? FightGrappleKind.Takedown : FightGrappleKind.Grab });
    }

    // ── Defender side ────────────────────────────────────────────────────────

    private void EnterHeld(FighterGrapple holder, float window)
    {
        _holder = holder;
        _actor.MoveController?.InterruptMove();
        Enter(FighterGrappleState.Held, window);
        _actor.HitReaction?.HoldGrabbed(window + 0.5f); // locked + "held" reaction until thrown/broken
    }

    private void TryBreak()
    {
        if (State != FighterGrappleState.Held || _timer <= 0f || _holder == null) return;
        var holder = _holder;
        float lockTime = _bal != null ? _bal.throwBreakLock : 0.25f, push = _bal != null ? _bal.throwBreakPush : 0.9f;
        ClearToNone();
        _actor.HitReaction?.ReleaseStun();
        holder.OnTargetBroke(lockTime, push);
        _actor.Movement?.QueueKnockback(holder._actor.ForwardXZ * push * 0.5f);
        Debug.Log($"[FighterGrapple] {_actor.name} THROW BREAK");
        EventBus.Publish(new FightGrappleEvent { Attacker = holder._actor, Defender = _actor, Kind = FightGrappleKind.Break });
    }

    private void TryGroundEscape()
    {
        if (!CanEscapeNow || _holder == null) return;
        var holder = _holder;
        holder.EndGroundControl(escaped: true);
        _actor.HitReaction?.ForceGetUp();
        EventBus.Publish(new FightGrappleEvent { Attacker = holder._actor, Defender = _actor, Kind = FightGrappleKind.GroundEscape });
    }

    private void OnTargetBroke(float lockTime, float push)
    {
        _target = null;
        EnterRecover(lockTime);
        _actor.Movement?.QueueKnockback(-_actor.ForwardXZ * push * 0.5f);
    }

    // ── Attacker resolution ──────────────────────────────────────────────────

    private void ExecuteThrow()
    {
        var target = _target;
        _target = null;
        if (target == null || target.State != FighterGrappleState.Held) { EnterRecover(0.2f); return; }
        target.ClearToNone(); // the knockdown below takes over the defender's reaction state

        if (_throwMove == null)
        {
            _throwMove = ScriptableObject.CreateInstance<FightMoveDefinition>();
            _throwMove.name = "Throw (runtime)";
            _throwMove.id = "move_throw";
            _throwMove.debugName = "Throw";
            _throwMove.scalingStat = FighterBuildStat.PunchPower;
        }
        _throwMove.knockdownOnHit = true;
        var hit = new FightHitDefinition
        {
            baseDamage    = IsTakedown ? (_bal != null ? _bal.takedownDamage : 9f) : (_bal != null ? _bal.throwDamage : 12f),
            baseKnockback = IsTakedown ? 0.35f : (_bal != null ? _bal.throwKnockback : 1.4f),
            baseHitStun   = 0.4f,
            attackHeight  = AttackHeight.Mid,
            guardType     = GuardType.Unblockable,
        };
        target._actor.HitReaction?.ReleaseStun(); // so the knockdown is not swallowed by the hold stun
        var result = FightHitDispatcher.ResolveAndApply(_actor, target._actor, _throwMove, hit, _bal);
        _actor.AnimationDriver?.PlayRole(CombatRole.HeavyAttackAlt, 0.5f);

        bool landed = !result.IsEvaded && !result.IsBlocked;
        if (IsTakedown && landed && (target._actor.Health == null || !target._actor.Health.IsKO))
        {
            _target = target;
            GroundStrikes = 0;
            _nextStrike = 0f;
            Enter(FighterGrappleState.GroundControl, _bal != null ? _bal.groundControlDuration : 1.5f);
            target._holder = this;
            target.Enter(FighterGrappleState.Grounded, float.PositiveInfinity); // ended by EndGroundControl
            Debug.Log($"[FighterGrapple] {_actor.name} TAKEDOWN landed ({result.FinalDamage:F1} dmg) — ground control");
        }
        else
        {
            EnterRecover(0.3f);
            Debug.Log($"[FighterGrapple] {_actor.name} THROW ({result.FinalDamage:F1} dmg)");
            EventBus.Publish(new FightGrappleEvent { Attacker = _actor, Defender = target._actor, Kind = FightGrappleKind.Throw, Damage = result.FinalDamage });
        }
    }

    private void TryGroundStrike()
    {
        if (_target == null || Time.time < _nextStrike) return;
        int max = _bal != null ? _bal.groundStrikeMax : 3;
        if (GroundStrikes >= max) return;
        GroundStrikes++;
        _nextStrike = Time.time + (_bal != null ? _bal.groundStrikeInterval : 0.3f);
        float dmg = _bal != null ? _bal.groundStrikeDamage : 4f;
        _actor.AnimationDriver?.PlayRole(CombatRole.LightAttack, 0.3f);
        _target._actor.Health?.ApplyDamage(dmg);
        EventBus.Publish(new FightGrappleEvent { Attacker = _actor, Defender = _target._actor, Kind = FightGrappleKind.GroundStrike, Damage = dmg });
        if (GroundStrikes >= max) EndGroundControl(escaped: false);
    }

    private void EndGroundControl(bool escaped)
    {
        if (State != FighterGrappleState.GroundControl) return;
        var target = _target;
        _target = null;
        if (target != null && target.State == FighterGrappleState.Grounded) target.ClearToNone();
        EnterRecover(0.2f);
        if (!escaped)
            EventBus.Publish(new FightGrappleEvent { Attacker = _actor, Defender = target != null ? target._actor : null, Kind = FightGrappleKind.GroundEnd });
    }

    // ── State plumbing ───────────────────────────────────────────────────────

    private void Enter(FighterGrappleState state, float duration)
    {
        State = state;
        _timer = duration;
        StateTime = 0f;
    }

    private void EnterRecover(float duration)
    {
        Enter(FighterGrappleState.Recover, duration);
        LockMovement(true);
    }

    private void ClearToNone()
    {
        State = FighterGrappleState.None;
        _timer = 0f;
        _holder = null;
    }

    private void LockMovement(bool locked) => _actor.Movement?.SetLock(locked, locked ? 0f : 1f);

    private void Update()
    {
        if (State == FighterGrappleState.None) return;
        StateTime += Time.deltaTime;
        if (!float.IsInfinity(_timer)) _timer -= Time.deltaTime;

        // Interrupted from outside (hit, KO): never leave anyone stuck.
        bool selfKO = _actor.Health != null && _actor.Health.IsKO;
        switch (State)
        {
            case FighterGrappleState.Holding:
                if (selfKO || (_actor.HitReaction != null && _actor.HitReaction.State != FighterReactionState.None)) { Abort("holder interrupted"); return; }
                if (_timer <= 0f) ExecuteThrow();
                return;
            case FighterGrappleState.Held:
                // Safety: the holder vanished or stopped holding → free.
                if (_holder == null || _holder.State != FighterGrappleState.Holding || _timer < -1f)
                {
                    ClearToNone();
                    _actor.HitReaction?.ReleaseStun();
                }
                return;
            case FighterGrappleState.GroundControl:
                bool targetGone = _target == null || (_target._actor.Health != null && _target._actor.Health.IsKO) ||
                                  _target._actor.HitReaction == null || !_target._actor.HitReaction.IsInKnockdownFlow;
                if (selfKO || _timer <= 0f || targetGone) EndGroundControl(escaped: false); // HARD cap
                return;
            case FighterGrappleState.Grounded:
                if (_holder == null || _holder.State != FighterGrappleState.GroundControl) ClearToNone();
                return;
            case FighterGrappleState.Recover:
                if (_timer <= 0f)
                {
                    ClearToNone();
                    if (_actor.HitReaction == null || _actor.HitReaction.State == FighterReactionState.None) LockMovement(false);
                }
                return;
        }
    }

    /// <summary>Ends everything at once (fight left/paused state change, round reset, interruption).</summary>
    public void Abort(string reason)
    {
        if (State == FighterGrappleState.None) return;
        var target = _target;
        bool wasHolder = State == FighterGrappleState.Holding || State == FighterGrappleState.GroundControl;
        _target = null;
        ClearToNone();
        if (wasHolder && target != null && target.IsBusy) { target.ClearToNone(); target._actor.HitReaction?.ReleaseStun(); }
        if (_actor.HitReaction == null || _actor.HitReaction.State == FighterReactionState.None) LockMovement(false);
        Debug.Log($"[FighterGrapple] {_actor.name} grapple aborted ({reason}).");
    }

    public void ResetForRound()
    {
        _target = null;
        _holder = null;
        State = FighterGrappleState.None;
        _timer = 0f;
        StateTime = 0f;
        GroundStrikes = 0;
        IsTakedown = false;
    }
}
