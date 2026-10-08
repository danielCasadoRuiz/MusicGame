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
/// COMBAT RUNTIME (shared by Player and Opponent): the fighter's FighterCombatProfileSO supplies
/// the move set (SetCombatProfile); TryExecuteMove/TryExecuteRole validate a move (flow state, hit
/// stun / knockdown, match lock, posture/context, RESOURCE COST) and start it — a cost is CONSUMED
/// the instant the move starts. The move's data owns Startup/Active/Recovery (Active never scales;
/// Startup/Recovery scale with the fighter's Agility via FightCombatBalanceConfig); the animation
/// driver is told the role + duration and adapts, it never drives timing. A Dodge move makes the
/// fighter invulnerable inside its [invulnerableStart, +invulnerableDuration] window; a Block move
/// guards for its whole duration (FighterGuard). Reactions (hit/block stun, knockdown flow) live in
/// FighterHitReaction and interrupt moves; a finished match locks moves and shows Victory/Defeat.
///
/// Added by FightSceneBootstrap per fighter; only active while FightFlowState is Fighting.
/// </summary>
public class FighterMoveController : MonoBehaviour
{
    private FightMoveSetSO _moveSet;
    private FighterInputController _input;
    private FighterCombatProfileSO _profile;

    private IFighterAnimationDriver _animationDriver = new DebugFighterAnimationDriver();
    private IFighterMovementDriver _movementDriver = new DebugFighterMovementDriver();

    public FighterMoveState CurrentPhase { get; private set; } = FighterMoveState.Idle;
    public FightMoveDefinition CurrentMove { get; private set; }
    public FightMoveDefinition QueuedMove { get; private set; }

    // ── Attack context (finishers / Signature / Power) ─────────────────────────────
    // Every request may carry a FightAttackBonus and a tag; the bonus that actually applies is
    // captured when the move STARTS (× Power multipliers if Power is active then) and read by
    // FighterAttack / the move's projectile at hit time.
    public enum AttackTag { None, Combo, Finisher, Signature }
    public FightAttackBonus CurrentAttackBonus { get; private set; } = FightAttackBonus.Identity;
    public AttackTag CurrentTag { get; private set; }
    private FightAttackBonus _queuedBonus = FightAttackBonus.Identity;
    private AttackTag _queuedTag;

    // COMBO STRINGS (FightComboRecognizer): every request carries the string it came from. A
    // continuation flagged ReplacesPrevious (PPP → PPPK) cancels the move of the SAME string —
    // through InterruptMove, the existing lifecycle — and starts the longer combo's move.
    private int _currentStringId;
    private int _queuedStringId;
    /// <summary>Debug: the last "cancelled → replacement" transition, e.g. "Punch (Finisher) → Combo AaaB".</summary>
    public string LastReplaceInfo { get; private set; } = "";
    /// <summary>Debug: what the last Signature request resolved to.</summary>
    public string LastSignatureInfo { get; private set; } = "";
    /// <summary>Debug: the last combo move requested (e.g. "Punch Finisher (×1.5 dmg)").</summary>
    public string LastComboInfo { get; private set; } = "";
    public float MoveElapsed { get; private set; }
    public float PhaseElapsed { get; private set; }
    public string CurrentAnimationState => _animationDriver.CurrentState;

    public bool CanAttack => !IsHitStunned && !IsMatchLocked && (CurrentPhase == FighterMoveState.Idle || InCancelWindow());

    public FighterCombatProfileSO CombatProfile => _profile;
    /// <summary>Set once the match is decided (Victory/Defeat pose): no move can start until the next round reset.</summary>
    public bool IsMatchLocked { get; private set; }
    /// <summary>Why the last TryExecuteMove call was rejected (debug/tests); empty on success.</summary>
    public string LastRejection { get; private set; } = "";

    /// <summary>Inside the current move's invulnerability window (Dodge).</summary>
    public bool IsInvulnerable => CurrentMove != null && CurrentMove.invulnerableDuration > 0f &&
                                  MoveElapsed >= CurrentMove.invulnerableStart &&
                                  MoveElapsed <= CurrentMove.invulnerableStart + CurrentMove.invulnerableDuration;
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
    private System.Action<FightPowerRequestedEvent> _onPower;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;
    private System.Action<MatchEndedEvent> _onMatchEnded;

    public void SetAnimationDriver(IFighterAnimationDriver driver) => _animationDriver = driver ?? new DebugFighterAnimationDriver();
    public void SetMovementDriver(IFighterMovementDriver driver) => _movementDriver = driver ?? new DebugFighterMovementDriver();
    public void SetActor(FighterActor actor) => _actor = actor;

    /// <summary>The fighter's moves come from its combat profile's move set (falls back to
    /// AppConfig.fightFlow.defaultMoveSet when the profile has none).</summary>
    public void SetCombatProfile(FighterCombatProfileSO profile)
    {
        _profile = profile;
        if (profile != null && profile.moveSet != null) _moveSet = profile.moveSet;
    }

    /// <summary>Tests/tools only: enables execution without a running Fight flow.</summary>
    public void DebugSetActive(bool active) => _active = active;
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
        // While grappling (grab hold / ground control) FighterGrapple owns the buttons.
        _onPunch = e => { if (e.Source == _input && !GrappleBusy) TryExecuteMove(ResolvePunch(), FightAttackBonus.Identity, AttackTag.None, e.StringId, false); };
        _onKick  = e => { if (e.Source == _input && !GrappleBusy) TryExecuteMove(ResolveKick(), FightAttackBonus.Identity, AttackTag.None, e.StringId, false); };
        _onCombo = e => { if (e.Source == _input && !GrappleBusy) OnCombo(e.Combo, e.StringId, e.ReplacesPrevious); };
        _onPower = e => { if (e.Source == _input) TryActivatePower(); };
        _onFightFlowChanged = e => _active = e.Current == FightFlowState.Fighting;
        _onMatchEnded = e =>
        {
            if (_actor == null) return;
            IsMatchLocked = true;
            InterruptMove();
            _input?.ResetComboString();
            _actor.Power?.Clear(_actor);
            bool won = e.Winner == _actor.Side;
            // A KO'd loser stays down (FighterHitReaction's Downed); otherwise show the pose.
            if (won || _actor.HitReaction == null || !_actor.HitReaction.IsInKnockdownFlow)
                _animationDriver.PlayRole(won ? CombatRole.Victory : CombatRole.Defeat);
        };
        EventBus.Subscribe(_onMatchEnded);
        EventBus.Subscribe(_onPunch);
        EventBus.Subscribe(_onKick);
        EventBus.Subscribe(_onCombo);
        EventBus.Subscribe(_onPower);
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onPunch);
        EventBus.Unsubscribe(_onKick);
        EventBus.Unsubscribe(_onCombo);
        EventBus.Unsubscribe(_onPower);
        EventBus.Unsubscribe(_onFightFlowChanged);
        EventBus.Unsubscribe(_onMatchEnded);
    }

    private void Update()
    {
        if (!_active) return;

        _actor?.Power?.Tick(_actor, Time.deltaTime);
        // A hit/knockdown or the match ending ends the combo string (nothing typed before continues).
        if (IsHitStunned || IsMatchLocked) _input?.ResetComboString();

        if (CurrentPhase == FighterMoveState.Idle)
        {
            if (QueuedMove != null)
            {
                var move = QueuedMove;
                var bonus = _queuedBonus;
                var tag = _queuedTag;
                int stringId = _queuedStringId;
                QueuedMove = null;
                // A queued enhanced Signature that can no longer be paid for falls back to the basic one.
                if (tag == AttackTag.Signature && !CanAfford(move) && _moveSet != null && _moveSet.signatureBasic != null)
                {
                    move = _moveSet.signatureBasic;
                    bonus = _balanceConfig != null ? _balanceConfig.signatureBasic : FightAttackBonus.Identity;
                }
                // Re-validated at dequeue time too — posture/movement-state may have changed while
                // this move sat queued, and the resource may no longer be affordable.
                if (IsContextAllowed(move) && CanAfford(move)) BeginMove(move, bonus, tag, stringId);
            }
            else if (!IsHitStunned && !IsMatchLocked)
            {
                // Holding guard shows Block; otherwise idle/locomotion. Reactions own the animation
                // while stunned/knocked down (FighterHitReaction), the match end owns it afterwards.
                if (_actor != null && _actor.Guard != null && _actor.Guard.State != FighterGuardState.None)
                    _animationDriver.PlayRole(CombatRole.Block);
                else if (_input != null)
                    _animationDriver.SetLocomotion(_input.CurrentHorizontal,
                        _actor != null ? _actor.MovementState : FighterMovementState.Idle,
                        _actor != null ? _actor.Posture : FighterPosture.Standing,
                        _actor != null && _actor.IsTurning);
            }
            return;
        }

        // A frame hitch must never skip a phase (skipping Active would mean the hitbox never
        // exists): time may advance at most to just past the CURRENT phase's end this frame, so every
        // phase — Active included — lasts at least one frame. Normal frames are unaffected.
        float dt = Time.deltaTime;
        float phaseEnd = CurrentPhaseEndTime();
        if (phaseEnd >= 0f && MoveElapsed + dt > phaseEnd) dt = Mathf.Max(0f, phaseEnd - MoveElapsed) + 1e-4f;
        MoveElapsed += dt;
        PhaseElapsed += dt;
        UpdatePhase();
    }

    /// <summary>
    /// A recognized combo (FightComboRecognizer). Empty moveId = the last step's own normal (the
    /// Punch/Kick finishers are PPP/KKK definitions with a finisher bonus); the Signature combo keeps
    /// its basic/enhanced resolution. `replaces` = this extends an already-completed combo of the same
    /// string: its move is cancelled and replaced (see TryExecuteMove).
    /// </summary>
    private void OnCombo(FightComboDefinition combo, int stringId, bool replaces)
    {
        if (combo == null || _moveSet == null) return;
        if (_moveSet.signatureSpecial != null && combo.moveId == _moveSet.signatureSpecial.id)
        {
            TryExecuteSignature(stringId, replaces);
            return;
        }

        FightMoveDefinition move;
        if (string.IsNullOrEmpty(combo.moveId))
        {
            var last = combo.steps != null && combo.steps.Length > 0 ? combo.steps[combo.steps.Length - 1].button : FightButton.None;
            move = last == FightButton.Kick ? ResolveKick() : last == FightButton.Punch ? ResolvePunch() : null;
        }
        else move = _moveSet.GetByMoveId(combo.moveId);

        var bonus = ComboBonus(combo.attackBonus);
        var tag = combo.attackBonus != FightComboBonus.None ? AttackTag.Finisher : AttackTag.Combo;
        if (TryExecuteMove(move, bonus, tag, stringId, replaces))
            LastComboInfo = $"{(string.IsNullOrEmpty(combo.debugName) ? combo.id : combo.debugName)} → {move.debugName}{(bonus.IsIdentity ? "" : $" ({bonus})")}";
    }

    private FightAttackBonus ComboBonus(FightComboBonus kind) => _balanceConfig == null ? FightAttackBonus.Identity : kind switch
    {
        FightComboBonus.PunchFinisher => _balanceConfig.punchFinisher,
        FightComboBonus.KickFinisher  => _balanceConfig.kickFinisher,
        _                             => FightAttackBonus.Identity,
    };

    /// <summary>
    /// The Signature input: with a Special available → the ENHANCED move (signatureSpecial — the
    /// projectile, costs 1 Special, signatureSpecial bonus); otherwise → the BASIC move
    /// (signatureBasic, no projectile, signatureBasic bonus). The Special is consumed by BeginMove,
    /// i.e. only when the move actually starts, exactly once per started move.
    /// </summary>
    public bool TryExecuteSignature() => TryExecuteSignature(0, false);

    public bool TryExecuteSignature(int stringId, bool replaces)
    {
        if (_moveSet == null) return false;
        var special = _moveSet.signatureSpecial;
        bool enhanced = special != null && Wallet != null && CanAfford(special) && special.resourceCost != CombatResourceType.None;
        var move = enhanced ? special : _moveSet.signatureBasic;
        if (move == null) { LastRejection = "no signatureBasic move"; return false; }
        var bonus = _balanceConfig == null ? FightAttackBonus.Identity
                  : enhanced ? _balanceConfig.signatureSpecial : _balanceConfig.signatureBasic;
        bool ok = TryExecuteMove(move, bonus, AttackTag.Signature, stringId, replaces);
        LastSignatureInfo = ok ? $"{(enhanced ? "ENHANCED" : "basic")} requested" : $"rejected ({LastRejection})";
        return ok;
    }

    /// <summary>Debug: result of the last Power attempt (activated + x4 consumed, or why it was rejected).</summary>
    public string LastPowerInfo { get; private set; } = "";

    /// <summary>
    /// Hold Down + Punch + Kick completed (FightPowerRequestedEvent). Activates the Power State and
    /// consumes exactly 1 x4 (CombatResourceType.QuadCombo, the Runner's x4 count in this fighter's
    /// FighterCombatResources wallet) — but ONLY if every condition holds: fighting, not stunned or
    /// knocked down, match not over, Power not already active (no refresh/extend/stack), and x4 ≥ 1.
    /// Any rejection consumes nothing and does nothing else (the absorbed presses never replay).
    /// </summary>
    private void TryActivatePower()
    {
        if (_actor == null || _actor.Power == null) return;
        string reject =
            !_active || IsMatchLocked ? "not fighting / match over" :
            IsHitStunned              ? "stunned / knocked down" :
            _actor.Power.IsActive     ? "already active" :
            Wallet == null || !Wallet.CanAfford(CombatResourceType.QuadCombo, 1) ? "no x4" : null;
        if (reject != null)
        {
            LastPowerInfo = $"REJECTED ({reject}) — x4 {Wallet?.QuadCombos ?? 0}";
            Debug.Log($"[FighterMoveController] {_actor.Side} Power rejected: {reject}");
            return;
        }

        float duration = _balanceConfig != null ? _balanceConfig.powerDuration : 6f;
        var mult = _balanceConfig != null ? _balanceConfig.PowerMultipliers : FightAttackBonus.Identity;
        if (!Wallet.TryConsume(CombatResourceType.QuadCombo, 1) || !_actor.Power.TryActivate(_actor, duration, mult))
        {
            LastPowerInfo = "REJECTED (could not consume x4)";
            return;
        }
        LastPowerInfo = $"ACTIVATED — 1 x4 consumed (x4 left {Wallet.QuadCombos})";
        Debug.Log($"[FighterMoveController] {_actor.Side} POWER STATE ON for {duration:0.0}s ({mult}); 1 x4 consumed, {Wallet.QuadCombos} left");
    }

    private void TryStartMove(FightMoveDefinition move) => TryExecuteMove(move);

    /// <summary>Executes (or queues, during a running move) the profile move for `role`.</summary>
    public bool TryExecuteRole(CombatRole role)
    {
        var move = _profile != null ? _profile.GetMove(role) : null;
        if (move == null) { LastRejection = $"profile has no {role} move"; return false; }
        return TryExecuteMove(move);
    }

    /// <summary>
    /// The one entry point for starting a move. Rejected (false, LastRejection says why, nothing
    /// consumed) when: not fighting, stunned/knocked down, match locked, wrong posture/context, or
    /// its resource cost isn't affordable. Accepted: starts now (Idle / cancel window) — consuming
    /// the cost — or is queued (single slot, latest wins; cost consumed when it actually starts).
    /// </summary>
    public bool TryExecuteMove(FightMoveDefinition move) => TryExecuteMove(move, FightAttackBonus.Identity, AttackTag.None, 0, false);

    /// <summary>
    /// `stringId` = the combo string the request came from (0 = none). `replaces` = it extends an
    /// already-completed combo of that string: if that string's move is RUNNING it is cancelled through
    /// InterruptMove (hitbox closed by the phase event, movement lock released, queue cleared, bonus/tag
    /// reset) and this move begins at once; if it is still QUEUED, this request takes its slot.
    /// Whatever that move already committed stays committed (a landed hit is never undone or repeated,
    /// an already-spawned projectile keeps flying); a move cancelled in Startup committed nothing.
    /// If the string's move isn't current any more (finished, interrupted by a hit) it is a plain request.
    /// </summary>
    public bool TryExecuteMove(FightMoveDefinition move, FightAttackBonus bonus, AttackTag tag, int stringId, bool replaces)
    {
        LastRejection = "";
        if (move == null)             { LastRejection = "no move"; return false; }
        if (!_active)                 { LastRejection = "not fighting"; return false; }
        if (IsMatchLocked)            { LastRejection = "match over"; return false; }
        if (IsHitStunned)             { LastRejection = "stunned / knocked down"; return false; }
        if (!IsContextAllowed(move))  { LastRejection = "context (posture/movement)"; return false; }
        if (!CanAfford(move))         { LastRejection = $"needs {move.resourceAmount} {move.resourceCost}"; return false; }

        if (replaces && stringId != 0 && CurrentMove != null && _currentStringId == stringId)
        {
            var cancelled = CurrentMove;
            var cancelledPhase = CurrentPhase;
            InterruptMove();
            BeginMove(move, bonus, tag, stringId);
            LastReplaceInfo = $"{cancelled.debugName} ({cancelledPhase}) → {move.debugName}";
            Debug.Log($"[FighterMoveController] Combo extended: cancelled {LastReplaceInfo}");
            return true;
        }
        if (replaces && stringId != 0 && QueuedMove != null && _queuedStringId == stringId)
            LastReplaceInfo = $"{QueuedMove.debugName} (queued) → {move.debugName}";

        if (CurrentPhase == FighterMoveState.Idle || InCancelWindow())
            BeginMove(move, bonus, tag, stringId);
        else
        {
            QueuedMove = move; // latest request wins — a single queue slot, see class doc
            _queuedBonus = bonus;
            _queuedTag = tag;
            _queuedStringId = stringId;
        }
        return true;
    }

    private FighterCombatResources Wallet => _actor != null ? _actor.CombatResources : null;

    private bool CanAfford(FightMoveDefinition move) =>
        move.resourceCost == CombatResourceType.None || (Wallet != null && Wallet.CanAfford(move.resourceCost, move.resourceAmount));

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
    private bool GrappleBusy => _actor != null && _actor.Grapple != null && _actor.Grapple.IsBusy;

    private FightMoveDefinition ResolvePunch() =>
        _moveSet == null ? null : ResolveNormal(_moveSet.airNormalPunch, _moveSet.runNormalPunch, _moveSet.highNormalPunch, _moveSet.normalPunch, _moveSet.lowNormalPunch);

    private FightMoveDefinition ResolveKick() =>
        _moveSet == null ? null : ResolveNormal(_moveSet.airNormalKick, _moveSet.runNormalKick, _moveSet.highNormalKick, _moveSet.normalKick, _moveSet.lowNormalKick);

    /// <summary>Context first (airborne → air move, running → running move), then the ATTACK HEIGHT
    /// from the vertical direction held at the press: Up = HIGH, none = MID, Down = LOW. An Up/Down
    /// used for a height is consumed so it never also becomes a jump/sidestep. Missing slots fall back
    /// to the MID normal.</summary>
    private FightMoveDefinition ResolveNormal(FightMoveDefinition air, FightMoveDefinition run, FightMoveDefinition high,
                                              FightMoveDefinition mid, FightMoveDefinition low)
    {
        if (_actor != null && _actor.Posture == FighterPosture.Airborne && air != null) return air;
        if (_actor != null && _actor.MovementState == FighterMovementState.Run && run != null) return run;
        var vertical = _input != null ? _input.CurrentVertical : FightVerticalDirection.Neutral;
        if (vertical == FightVerticalDirection.Up && high != null) { _actor?.Movement?.ConsumeVerticalPress(); return high; }
        if (vertical == FightVerticalDirection.Down && low != null) { _actor?.Movement?.ConsumeVerticalPress(); return low; }
        return mid;
    }

    private void BeginMove(FightMoveDefinition move, FightAttackBonus bonus, AttackTag tag, int stringId)
    {
        QueuedMove = null; // this move supersedes whatever was queued, if anything
        _queuedTag = AttackTag.None;
        _queuedStringId = 0;
        _currentStringId = stringId;
        CurrentMove = move;
        MoveElapsed = 0f;
        PhaseElapsed = 0f;
        if (move.resourceCost != CombatResourceType.None && (Wallet == null || !Wallet.TryConsume(move.resourceCost, move.resourceAmount)))
        {
            LastRejection = $"needs {move.resourceAmount} {move.resourceCost}";
            CurrentMove = null;
            CurrentAttackBonus = FightAttackBonus.Identity;
            CurrentTag = AttackTag.None;
            _currentStringId = 0;
            return;
        }
        if (move.resourceCost != CombatResourceType.None)
            EventBus.Publish(new FightResourceSpentEvent { Fighter = _actor, Move = move, Type = move.resourceCost, Amount = move.resourceAmount });

        // Power multiplies whatever this attack already carries (captured now, for this whole move).
        bool power = _actor != null && _actor.Power != null && _actor.Power.IsActive;
        CurrentAttackBonus = power ? bonus * _actor.Power.Multipliers : bonus;
        CurrentTag = tag;
        if (tag == AttackTag.Signature)
        {
            bool enhanced = move.resourceCost != CombatResourceType.None;
            int left = Wallet != null ? Wallet.Specials : 0;
            LastSignatureInfo = $"{(enhanced ? "ENHANCED (1 Special consumed)" : "basic")} — {move.debugName}, Specials left {left}";
            Debug.Log($"[FighterMoveController] {(_actor != null ? _actor.Side.ToString() : "?")} SIGNATURE {LastSignatureInfo}; bonus {CurrentAttackBonus}");
            EventBus.Publish(new SignatureExecutedEvent { Fighter = _actor, Move = move, Enhanced = enhanced, SpecialsLeft = left });
        }
        _timingScale = _balanceConfig != null && _actor != null ? _balanceConfig.ComputeModifiers(_actor.BuildStats).TimingScale : 1f;
        // CurrentPhase is left as whatever it currently is (Idle, or the previous move's phase if
        // this is a cancel-chain) — UpdatePhase() below is the ONLY place a phase transition (and
        // its FightMovePhaseChangedEvent) is ever published, so it must own this one too.

        // MOMENTUM: the move keeps a share of the current horizontal velocity (forwardCarry on the
        // ground, airCarry in a jump) instead of stopping dead — see FighterMovement.SetLock.
        bool airborneMove = _actor != null && _actor.Posture == FighterPosture.Airborne;
        float carry = airborneMove ? move.airCarry : move.forwardCarry;
        float speedIn = _actor != null && _actor.Movement != null ? Mathf.Abs(_actor.Movement.ForwardSpeed + _actor.Movement.CarryVelocity) : 0f;
        _movementDriver.SetMovementLock(move.movementLocked, move.movementMultiplier, carry);
        if (!Mathf.Approximately(move.lungeDistance, 0f)) _movementDriver.ApplyLunge(move.lungeDistance);
        if (airborneMove && _moveSet != null && move == _moveSet.airNormalKick && speedIn >= 2.5f)
            EventBus.Publish(new FightFlyingKickEvent { Fighter = _actor, Speed = speedIn });
        _animationDriver.PlayMoveAnimation(move, move.startupDuration * _timingScale + move.activeDuration + move.recoveryDuration * _timingScale);

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

    // Move time at which the current phase ends (-1 when Idle / no move).
    private float CurrentPhaseEndTime()
    {
        if (CurrentMove == null) return -1f;
        float startupEnd = CurrentMove.startupDuration * _timingScale;
        float activeEnd  = startupEnd + CurrentMove.activeDuration;
        return CurrentPhase switch
        {
            FighterMoveState.Startup  => startupEnd,
            FighterMoveState.Active   => activeEnd,
            FighterMoveState.Recovery => activeEnd + CurrentMove.recoveryDuration * _timingScale,
            _ => -1f,
        };
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
        if (CurrentMove == null) { QueuedMove = null; _queuedTag = AttackTag.None; _queuedStringId = 0; return; }

        var previousPhase = CurrentPhase;
        var move = CurrentMove;

        Debug.Log($"[FighterMoveController] Move interrupted: {move.debugName}");
        CurrentMove = null;
        QueuedMove = null;
        _queuedTag = AttackTag.None;
        _queuedStringId = 0;
        _currentStringId = 0;
        CurrentAttackBonus = FightAttackBonus.Identity; // never leak into whatever starts next
        CurrentTag = AttackTag.None;
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
        IsMatchLocked = false;

        CurrentMove = null;
        QueuedMove = null;
        _queuedTag = AttackTag.None;
        _queuedStringId = 0;
        _currentStringId = 0;
        CurrentAttackBonus = FightAttackBonus.Identity;
        CurrentTag = AttackTag.None;
        _input?.ResetComboString();
        _actor?.Power?.Clear(_actor);
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
