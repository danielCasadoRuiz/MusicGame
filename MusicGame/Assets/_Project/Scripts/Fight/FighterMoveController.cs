using UnityEngine;

/// <summary>
/// Turns the input layer's decoupled output (FightNormalPunchEvent/FightNormalKickEvent/
/// FightComboDetectedEvent) into real move EXECUTION — a Startup -> Active -> Recovery state
/// machine driven entirely by FightMoveDefinition data, resolved through a FightMoveSetSO. This is
/// the ONLY thing that ever touches FightMoveSetSO or runs the state machine; FighterInputController
/// still knows nothing about moves, and nothing downstream needs to know HOW a move got triggered.
///
/// QUEUE / CANCEL POLICY (V1 — simple on purpose, see class doc for why nothing fancier is built
/// yet):
///   - Idle: any request (Normal or Combo-resolved) starts immediately. CanAttack is true.
///   - Startup / Active / early Recovery: a new request is REJECTED-but-remembered — it overwrites
///     QueuedMove (a single slot; only the MOST RECENT request survives, older ones are simply
///     lost) rather than starting anything. The queued move fires automatically, unconditionally,
///     the instant the current move reaches Idle.
///   - The trailing `cancelWindow` seconds of the CURRENT move's total duration (see
///     FightMoveDefinition.cancelWindow) are the one exception: a request arriving there starts
///     IMMEDIATELY instead of queuing — this is what lets a recognized combo's own move "chain"
///     into the tail of the normal that led into it, instead of always waiting for full Recovery.
///   - A combo's own move (once started) follows the EXACT same rules as any other move — nothing
///     about combo-triggered moves is special-cased beyond "where do we look it up"
///     (FightMoveSetSO.GetByMoveId vs the dedicated normalPunch/normalKick slots).
/// This is deliberately NOT a multi-slot input queue, priority system, or per-phase cancel window —
/// exactly the "prepare the idea, don't over-build it" the design asked for. Growing any of that
/// later only touches TryStartMove/UpdatePhase, never the event wiring or the data schema.
///
/// ANIMATION/MOVEMENT/FACING are read from FightMoveDefinition and reported to
/// IFighterAnimationDriver/IFighterMovementDriver/IsFacingLocked — see each interface's own doc.
/// No real Animator or Transform exists yet (see this phase's own scope notes), so both drivers
/// default to inert Debug* implementations; swapping in real ones later needs no change here.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController), like every other Fight-flow
/// controller — only active while FightFlowState is Fighting.
/// </summary>
public class FighterMoveController : MonoBehaviour
{
    private FightMoveSetSO _moveSet;
    private FighterInputController _input;

    private IFighterAnimationDriver _animationDriver = new DebugFighterAnimationDriver();
    private IFighterMovementDriver _movementDriver = new DebugFighterMovementDriver();

    public FighterMoveState CurrentPhase { get; private set; } = FighterMoveState.Idle;
    public FightMoveDefinition CurrentMove { get; private set; }
    public FightMoveDefinition QueuedMove { get; private set; }
    public float MoveElapsed { get; private set; }
    public float PhaseElapsed { get; private set; }
    public string CurrentAnimationState => _animationDriver.CurrentState;

    public bool CanAttack => !IsHitStunned && (CurrentPhase == FighterMoveState.Idle || InCancelWindow());
    /// <summary>See FightMoveDefinition.lockFacingDuringMove's own doc — has no real effect yet
    /// (no real facing recalculation exists to consult it), but is real, queryable state.</summary>
    public bool IsFacingLocked => CurrentMove != null && CurrentMove.lockFacingDuringMove && CurrentPhase != FighterMoveState.Recovery;

    /// <summary>Duration of whichever phase is CURRENTLY running — 0 while Idle. Startup/Recovery
    /// are scaled by Speed (see FightCombatBalanceConfig.speedToTimingScale's own doc); Active never is.</summary>
    public float CurrentPhaseDuration => CurrentMove == null ? 0f : CurrentPhase switch
    {
        FighterMoveState.Startup  => CurrentMove.startupDuration * _timingScale,
        FighterMoveState.Active   => CurrentMove.activeDuration,
        FighterMoveState.Recovery => CurrentMove.recoveryDuration * _timingScale,
        _ => 0f,
    };
    public float PhaseProgress01 => CurrentPhaseDuration > 0f ? Mathf.Clamp01(PhaseElapsed / CurrentPhaseDuration) : 1f;

    /// <summary>Set externally (FightSceneBootstrap wires this to the owning FighterActor's own
    /// Stats) — read by FightCombatBalanceConfig.speedToTimingScale via BeginMove, and by
    /// FightHitResolver via FighterActor.Stats directly for damage/hit-stun/knockback (this
    /// property and FighterActor.Stats point at the SAME instance for whichever fighter has both).</summary>
    public FighterStats Stats { get; set; }

    /// <summary>Set by FighterHitReaction while this fighter is in hit stun — see that class's own
    /// doc. Blocks CanAttack/TryStartMove entirely; never touched from anywhere else.</summary>
    public bool IsHitStunned { get; set; }

    private float _timingScale = 1f;
    private FightCombatBalanceConfig _balanceConfig;

    private bool _active;

    /// <summary>Set externally (FightSceneBootstrap) — the owning FighterActor, read for
    /// Posture/MovementState context checks (IsContextAllowed) and posture-aware normal resolution
    /// (ResolvePunch/ResolveKick). Null-tolerant everywhere it's read (treated as "no restriction").</summary>
    private FighterActor _actor;

    private System.Action<FightNormalPunchEvent> _onPunch;
    private System.Action<FightNormalKickEvent> _onKick;
    private System.Action<FightComboDetectedEvent> _onCombo;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

    public void SetAnimationDriver(IFighterAnimationDriver driver) => _animationDriver = driver ?? new DebugFighterAnimationDriver();
    public void SetMovementDriver(IFighterMovementDriver driver) => _movementDriver = driver ?? new DebugFighterMovementDriver();
    public void SetActor(FighterActor actor) => _actor = actor;
    /// <summary>Set externally (FightSceneBootstrap) — THIS fighter's own FighterInputController.
    /// Every FightNormalPunchEvent/FightNormalKickEvent/FightComboDetectedEvent is filtered against
    /// this exact instance (e.Source == _input) so the Player's presses never drive the Opponent's
    /// FighterMoveController and vice versa — see FightNormalPunchEvent's own doc. No longer
    /// resolved via FindFirstObjectByType now that both fighters have their own instance.</summary>
    public void SetInputController(FighterInputController input) => _input = input;

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _moveSet = appConfig != null && appConfig.fightFlow != null ? appConfig.fightFlow.defaultMoveSet : null;
        if (_moveSet == null)
            Debug.LogWarning("[FighterMoveController] No FightMoveSetSO (AppConfig.fightFlow.defaultMoveSet) configured — normals/combos will be recognized but no move will ever execute.");

        _balanceConfig = appConfig != null ? appConfig.combatBalance : null;
    }

    private void OnEnable()
    {
        _onPunch = e => { if (e.Source == _input) TryStartMove(ResolvePunch()); };
        _onKick  = e => { if (e.Source == _input) TryStartMove(ResolveKick()); };
        _onCombo = e => { if (e.Source == _input) TryStartMove(_moveSet != null && e.Combo != null ? _moveSet.GetByMoveId(e.Combo.moveId) : null); };
        _onFightFlowChanged = e => _active = e.Current == FightFlowState.Fighting;
        EventBus.Subscribe(_onPunch);
        EventBus.Subscribe(_onKick);
        EventBus.Subscribe(_onCombo);
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onPunch);
        EventBus.Unsubscribe(_onKick);
        EventBus.Unsubscribe(_onCombo);
        EventBus.Unsubscribe(_onFightFlowChanged);
    }

    private void Update()
    {
        if (!_active) return;

        if (CurrentPhase == FighterMoveState.Idle)
        {
            if (QueuedMove != null)
            {
                var move = QueuedMove;
                QueuedMove = null;
                // Re-validated at dequeue time too — posture/movement-state may have changed while
                // this move sat queued (e.g. it landed a jump between queuing and now).
                if (IsContextAllowed(move)) BeginMove(move);
            }
            else
            {
                // Locomotion-driven placeholder animation state — see IFighterAnimationDriver's
                // own doc. Reads FighterInputController directly; no move is involved.
                if (_input != null) _animationDriver.SetLocomotion(_input.CurrentHorizontal);
            }
            return;
        }

        MoveElapsed += Time.deltaTime;
        PhaseElapsed += Time.deltaTime;
        UpdatePhase();
    }

    private void TryStartMove(FightMoveDefinition move)
    {
        if (!_active || move == null || IsHitStunned || !IsContextAllowed(move)) return;

        if (CurrentPhase == FighterMoveState.Idle || InCancelWindow())
        {
            BeginMove(move);
        }
        else
        {
            // Latest request wins — a single queue slot, see class doc on the policy.
            QueuedMove = move;
        }
    }

    /// <summary>Checked once, whenever a move would actually START (including a queued move about
    /// to dequeue) — see FightMoveDefinition.allowedPostures/requiredMovementStates' own doc. Both
    /// empty/unset (the default for every move authored before this existed) means unrestricted.</summary>
    private bool IsContextAllowed(FightMoveDefinition move)
    {
        if (_actor == null) return true;

        if (move.allowedPostures != null && move.allowedPostures.Length > 0)
        {
            bool postureOk = false;
            foreach (var posture in move.allowedPostures)
                if (posture == _actor.Posture) { postureOk = true; break; }
            if (!postureOk) return false;
        }

        if (move.requiredMovementStates != null && move.requiredMovementStates.Length > 0)
        {
            bool stateOk = false;
            foreach (var state in move.requiredMovementStates)
                if (state == _actor.MovementState) { stateOk = true; break; }
            if (!stateOk) return false;
        }

        return true;
    }

    /// <summary>Airborne -> airNormalPunch (if assigned); Run -> runNormalPunch (if assigned);
    /// otherwise the plain grounded normalPunch — see FightMoveSetSO's own doc. Airborne takes
    /// priority over Run since the two are mutually exclusive in practice (Run requires Standing/
    /// grounded Forward-holding — see FighterMovement's own doc) but airborne is checked first for
    /// safety regardless.</summary>
    private FightMoveDefinition ResolvePunch()
    {
        if (_moveSet == null) return null;
        if (_actor != null && _actor.Posture == FighterPosture.Airborne && _moveSet.airNormalPunch != null) return _moveSet.airNormalPunch;
        if (_actor != null && _actor.MovementState == FighterMovementState.Run && _moveSet.runNormalPunch != null) return _moveSet.runNormalPunch;
        return _moveSet.normalPunch;
    }

    private FightMoveDefinition ResolveKick()
    {
        if (_moveSet == null) return null;
        if (_actor != null && _actor.Posture == FighterPosture.Airborne && _moveSet.airNormalKick != null) return _moveSet.airNormalKick;
        return _moveSet.normalKick;
    }

    private void BeginMove(FightMoveDefinition move)
    {
        QueuedMove = null; // this move supersedes whatever was queued, if anything
        CurrentMove = move;
        MoveElapsed = 0f;
        PhaseElapsed = 0f;
        _timingScale = _balanceConfig != null ? _balanceConfig.ComputeModifiers(Stats).TimingScale : 1f;
        // CurrentPhase is left as whatever it currently is (Idle, or the previous move's phase if
        // this is a cancel-chain) — UpdatePhase() below is the ONLY place a phase transition (and
        // its FightMovePhaseChangedEvent) is ever published, so it must own this one too.

        _movementDriver.SetMovementLock(move.movementLocked, move.movementMultiplier);
        if (!Mathf.Approximately(move.lungeDistance, 0f)) _movementDriver.ApplyLunge(move.lungeDistance);
        _animationDriver.PlayMoveAnimation(move);

        Debug.Log($"[FighterMoveController] Move started: {move.debugName} ({move.moveType})");

        UpdatePhase(); // transitions into Startup (or beyond, for a degenerate 0-duration phase) and publishes the event
    }

    private void UpdatePhase()
    {
        var move = CurrentMove;
        float startupEnd  = move.startupDuration * _timingScale;
        float activeEnd   = startupEnd + move.activeDuration;
        float recoveryEnd = activeEnd + move.recoveryDuration * _timingScale;

        FighterMoveState newPhase =
            MoveElapsed < startupEnd  ? FighterMoveState.Startup  :
            MoveElapsed < activeEnd   ? FighterMoveState.Active   :
            MoveElapsed < recoveryEnd ? FighterMoveState.Recovery :
                                        FighterMoveState.Idle;

        if (newPhase == CurrentPhase) return;

        var previousPhase = CurrentPhase;
        CurrentPhase = newPhase;
        PhaseElapsed = 0f;

        EventBus.Publish(new FightMovePhaseChangedEvent { Source = this, Previous = previousPhase, Current = newPhase, Move = move });

        if (newPhase == FighterMoveState.Idle)
        {
            Debug.Log($"[FighterMoveController] Move ended: {move.debugName}");
            CurrentMove = null;
            _movementDriver.SetMovementLock(false, 1f);
        }
    }

    private bool InCancelWindow()
    {
        if (CurrentMove == null || CurrentMove.cancelWindow <= 0f) return false;
        float effectiveTotal = CurrentMove.startupDuration * _timingScale + CurrentMove.activeDuration + CurrentMove.recoveryDuration * _timingScale;
        return MoveElapsed >= effectiveTotal - CurrentMove.cancelWindow;
    }

    /// <summary>Explicit external API to abnormally end whatever move is currently running — e.g.
    /// FighterHitReaction calling this the instant a hit lands (see that class's own doc). Cleanly
    /// resets to Idle (cancelling any movement lock via the driver) and clears any queued move too
    /// (a queued follow-up shouldn't fire right into a hit reaction). Publishes
    /// FightMovePhaseChangedEvent so anything reacting to Active (FighterAttack's own hitbox) turns
    /// off correctly, same as a natural phase transition — no separate "interrupted" event exists.</summary>
    public void InterruptMove()
    {
        if (CurrentMove == null) { QueuedMove = null; return; }

        var previousPhase = CurrentPhase;
        var move = CurrentMove;

        Debug.Log($"[FighterMoveController] Move interrupted: {move.debugName}");
        CurrentMove = null;
        QueuedMove = null;
        CurrentPhase = FighterMoveState.Idle;
        PhaseElapsed = 0f;
        MoveElapsed = 0f;
        _movementDriver.SetMovementLock(false, 1f);

        if (previousPhase != FighterMoveState.Idle)
            EventBus.Publish(new FightMovePhaseChangedEvent { Source = this, Previous = previousPhase, Current = FighterMoveState.Idle, Move = move });
    }

    /// <summary>Explicit API for FightMatchController's between-rounds reset (via FighterActor.
    /// ResetForRound) — a full, silent return to Idle: no queued move survives, IsHitStunned clears
    /// (even a KO's permanent one — see FighterHealth's own doc), and the movement driver is
    /// unlocked. Publishes FightMovePhaseChangedEvent if a move/hitbox was somehow still live (see
    /// FighterAttack's own doc on why no stale Active window may survive into a new round) — a
    /// harmless no-op the rest of the time, since a round never actually ends mid-Active in
    /// practice (Fighting stops updating this state machine the instant it's no longer active).</summary>
    public void ResetForRound()
    {
        var previousPhase = CurrentPhase;

        CurrentMove = null;
        QueuedMove = null;
        CurrentPhase = FighterMoveState.Idle;
        MoveElapsed = 0f;
        PhaseElapsed = 0f;
        IsHitStunned = false;
        _timingScale = 1f;
        _movementDriver.SetMovementLock(false, 1f);

        if (previousPhase != FighterMoveState.Idle)
            EventBus.Publish(new FightMovePhaseChangedEvent { Source = this, Previous = previousPhase, Current = FighterMoveState.Idle, Move = null });
    }
}
