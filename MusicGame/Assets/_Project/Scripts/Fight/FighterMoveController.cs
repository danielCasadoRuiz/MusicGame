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

    public bool CanAttack => CurrentPhase == FighterMoveState.Idle || InCancelWindow();
    /// <summary>See FightMoveDefinition.lockFacingDuringMove's own doc — has no real effect yet
    /// (no real facing recalculation exists to consult it), but is real, queryable state.</summary>
    public bool IsFacingLocked => CurrentMove != null && CurrentMove.lockFacingDuringMove && CurrentPhase != FighterMoveState.Recovery;

    /// <summary>Duration of whichever phase is CURRENTLY running — 0 while Idle.</summary>
    public float CurrentPhaseDuration => CurrentMove == null ? 0f : CurrentPhase switch
    {
        FighterMoveState.Startup  => CurrentMove.startupDuration,
        FighterMoveState.Active   => CurrentMove.activeDuration,
        FighterMoveState.Recovery => CurrentMove.recoveryDuration,
        _ => 0f,
    };
    public float PhaseProgress01 => CurrentPhaseDuration > 0f ? Mathf.Clamp01(PhaseElapsed / CurrentPhaseDuration) : 1f;

    /// <summary>Not read by anything yet — see FightStatsConfig's own doc on why real
    /// Speed/Combo-driven scaling formulas wait until the whole combat pipeline exists. The seam
    /// is here (Stats.Get(FightStatId.Speed) would scale startup/recovery, Combo would scale
    /// something about combo behavior) so wiring it in later touches this one property's callers,
    /// never the data schema.</summary>
    public FighterStats Stats { get; set; }

    private bool _active;

    private System.Action<FightNormalPunchEvent> _onPunch;
    private System.Action<FightNormalKickEvent> _onKick;
    private System.Action<FightComboDetectedEvent> _onCombo;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

    public void SetAnimationDriver(IFighterAnimationDriver driver) => _animationDriver = driver ?? new DebugFighterAnimationDriver();
    public void SetMovementDriver(IFighterMovementDriver driver) => _movementDriver = driver ?? new DebugFighterMovementDriver();

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _moveSet = appConfig != null && appConfig.fightFlow != null ? appConfig.fightFlow.defaultMoveSet : null;
        if (_moveSet == null)
            Debug.LogWarning("[FighterMoveController] No FightMoveSetSO (AppConfig.fightFlow.defaultMoveSet) configured — normals/combos will be recognized but no move will ever execute.");

        _input = FindFirstObjectByType<FighterInputController>();
    }

    private void OnEnable()
    {
        _onPunch = _ => TryStartMove(_moveSet != null ? _moveSet.normalPunch : null);
        _onKick  = _ => TryStartMove(_moveSet != null ? _moveSet.normalKick  : null);
        _onCombo = e => TryStartMove(_moveSet != null && e.Combo != null ? _moveSet.GetByMoveId(e.Combo.moveId) : null);
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
                BeginMove(move);
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
        if (!_active || move == null) return;

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

    private void BeginMove(FightMoveDefinition move)
    {
        QueuedMove = null; // this move supersedes whatever was queued, if anything
        CurrentMove = move;
        MoveElapsed = 0f;
        PhaseElapsed = 0f;
        CurrentPhase = FighterMoveState.Startup; // UpdatePhase() below corrects this immediately if startupDuration is 0

        _movementDriver.SetMovementLock(move.movementLocked, move.movementMultiplier);
        if (!Mathf.Approximately(move.lungeDistance, 0f)) _movementDriver.ApplyLunge(move.lungeDistance);
        _animationDriver.PlayMoveAnimation(move);

        Debug.Log($"[FighterMoveController] Move started: {move.debugName} ({move.moveType})");

        UpdatePhase(); // handles a degenerate 0-duration phase falling straight through this same frame
    }

    private void UpdatePhase()
    {
        var move = CurrentMove;
        float startupEnd  = move.startupDuration;
        float activeEnd   = startupEnd + move.activeDuration;
        float recoveryEnd = activeEnd + move.recoveryDuration;

        FighterMoveState newPhase =
            MoveElapsed < startupEnd  ? FighterMoveState.Startup  :
            MoveElapsed < activeEnd   ? FighterMoveState.Active   :
            MoveElapsed < recoveryEnd ? FighterMoveState.Recovery :
                                        FighterMoveState.Idle;

        if (newPhase == CurrentPhase) return;

        CurrentPhase = newPhase;
        PhaseElapsed = 0f;

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
        return MoveElapsed >= CurrentMove.TotalDuration - CurrentMove.cancelWindow;
    }
}
