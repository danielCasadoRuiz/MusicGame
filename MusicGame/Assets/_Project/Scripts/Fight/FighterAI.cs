using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What the AI is currently trying to do — a Utility AI picks ONE of these per decision
/// tick (see FighterAI's own doc), which then generates real inputs until it naturally finishes or
/// the fighter can no longer act.</summary>
public enum FightAIIntention
{
    Wait,
    Approach,
    Retreat,
    Guard,
    CrouchGuard,
    Crouch,
    Jump,
    DashApproach,
    RunApproach,
    NormalAttack,
    LowAttack,
    ComboAttack,
    SpecialAttack,
    Punish,
    SideStepLeft,
    SideStepRight,
    SideWalk,
    BackDash,
}

/// <summary>
/// The Opponent's "brain" — a simple Utility AI, NOT a rigid FSM: every decision tick it scores every
/// FightAIIntention against the current FightAIContext + its own AIDifficultyProfile, picks one
/// (occasionally a deliberately suboptimal one, per errorRate), and drives its OWN AIFightInputSource
/// to actually perform it. That input source is the ONLY thing this class ever touches — see
/// AIFightInputSource's own doc and this phase's own explicit "NO FER TRAMPES" checklist:
///   - Never touches Transform/Health/FighterMoveController/FighterAttack/FightHitResolver directly.
///   - Never invokes a FightMoveDefinition directly — combos/specials are played out as REAL,
///     separately-timed button+direction presses through AIFightInputSource, exactly like a human,
///     so they go through the SAME FightInputBuffer/FightComboRecognizer/Move System and can
///     genuinely fail (dropped input, blown timing window) exactly like a clumsy human's can.
///   - Never reads the Player's future/buffered inputs — FightAIContext only ever captures already-
///     resolved, on-screen state (health%, posture, current move/phase, positions).
///   - Only re-observes/re-decides once per reactionTime (see EffectiveReactionTime) — an event that
///     just happened this frame isn't "seen" until the AI's next decision tick, same as a human.
///
/// REACTION TIME paces the DECISION LOOP itself (Update only calls Decide once every
/// EffectiveReactionTime seconds, floored by FightAIConfig.minReactionTime even for a very "Hard"
/// profile) — between ticks, whatever was last decided keeps executing (a held direction, or a
/// committed coroutine sequence for Jump/Dash/Run/attacks/combos/specials). CanAct is checked EVERY
/// frame regardless (KO/HitStun/BlockStun/move-lock immediately cancel whatever's running and hold
/// neutral input — see Update) so the AI never "queues up" an impossible action.
///
/// PERSONALITY comes entirely from AIDifficultyProfile's independent fields (aggression ≠ defense ≠
/// punishSkill ≠ comboSkill ≠ spacingAccuracy ≠ errorRate ≠ specialUsage) — see each scoring method's
/// own use of them. Difficulty (Easy/Medium/Hard) is just three example combinations of the SAME
/// fields, never a separate "difficulty" number, and never touches FighterStats (combat capability
/// stays entirely separate from AI quality, per this phase's own explicit requirement).
/// </summary>
public class FighterAI : MonoBehaviour
{
    private const string DashForwardMoveId = "move_dash_forward";

    private FighterActor _actor;
    private FighterActor _opponent;
    private AIFightInputSource _inputSource;
    private AIDifficultyProfile _profile;
    private FightArenaConfig _arenaConfig;

    private FightAIConfig _aiConfig;
    private FightMoveSetSO _moveSet;
    private FightComboSetSO _comboSet;
    private System.Random _random;

    private bool _active;
    private float _nextDecisionTime;
    private Coroutine _executionRoutine;

    private float _meleeRange = 1f;
    private float _lowRange = 1f;
    private float _projectileRange = 6f;

    // ── Debug-facing state (see FightDebugHUD's own AI section) ─────────────────
    public bool IsActive => _active;
    public AIDifficultyProfile Profile => _profile;
    public FightAIIntention CurrentIntention { get; private set; }
    public IReadOnlyDictionary<FightAIIntention, float> LastScores => _lastScores;
    public float DecisionTimeRemaining => Mathf.Max(0f, _nextDecisionTime - Time.time);
    public float TargetIdealDistance { get; private set; }
    public string LastErrorNote { get; private set; }
    public FightComboDefinition LastSelectedCombo { get; private set; }
    public int LastComboStepIndex { get; private set; } = -1;

    private Dictionary<FightAIIntention, float> _lastScores = new();

    private System.Action<FightFlowStateChangedEvent> _onFlowChanged;

    public void Initialize(FighterActor actor, FighterActor opponent, AIFightInputSource inputSource, AIDifficultyProfile profile, FightArenaConfig arenaConfig)
    {
        _actor = actor;
        _opponent = opponent;
        _inputSource = inputSource;
        _profile = profile;
        _arenaConfig = arenaConfig;

        _meleeRange = EstimateHitRange(_moveSet != null ? _moveSet.normalPunch : null);
        var lowMove = FindMoveWithHeight(AttackHeight.Low);
        _lowRange = lowMove != null ? EstimateHitRange(lowMove) : _meleeRange;
        var projectileMove = FindProjectileMove();
        _projectileRange = projectileMove?.projectile != null ? projectileMove.projectile.maxDistance : 6f;
    }

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _aiConfig = appConfig != null ? appConfig.aiConfig : null;
        _moveSet  = appConfig != null && appConfig.fightFlow != null ? appConfig.fightFlow.defaultMoveSet : null;
        _comboSet = appConfig != null && appConfig.fightFlow != null ? appConfig.fightFlow.comboSet : null;

        int seed = _aiConfig != null && _aiConfig.debugSeed != 0 ? _aiConfig.debugSeed : System.Environment.TickCount;
        _random = new System.Random(seed);
    }

    private void OnEnable()
    {
        _onFlowChanged = e =>
        {
            _active = e.Current == FightFlowState.Fighting;
            if (_active) _nextDecisionTime = Time.time + EffectiveReactionTime();
        };
        EventBus.Subscribe(_onFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowChanged);
    }

    private void Update()
    {
        if (!_active || _actor == null || _opponent == null)
        {
            StopExecuting();
            return;
        }

        var ctx = FightAIContext.Capture(_actor, _opponent, _arenaConfig, FightMatchController.Instance);

        if (!ctx.SelfCanAct)
        {
            StopExecuting();
            _inputSource.SetDirection(0f, 0f);
            _nextDecisionTime = Time.time + EffectiveReactionTime();
            return;
        }

        if (_executionRoutine != null) return; // committed to whatever was last decided
        if (Time.time < _nextDecisionTime) return; // still "reacting"

        Decide(ctx);
        _nextDecisionTime = Time.time + EffectiveReactionTime();
    }

    /// <summary>Explicit API for FightMatchController's between-rounds reset (via FighterActor.
    /// ResetForRound) — task's own explicit checklist: clears current decision, execution sequence,
    /// pending combo step, reaction timer, and held movement/guard intention, so nothing from the
    /// previous round bleeds into the next.</summary>
    public void ResetForRound()
    {
        StopExecuting();
        if (_inputSource != null) _inputSource.SetDirection(0f, 0f);
        CurrentIntention = FightAIIntention.Wait;
        LastSelectedCombo = null;
        LastComboStepIndex = -1;
        LastErrorNote = null;
        _lastScores.Clear();
        _nextDecisionTime = _active ? Time.time + EffectiveReactionTime() : 0f;
    }

    private void StopExecuting()
    {
        if (_executionRoutine != null) { StopCoroutine(_executionRoutine); _executionRoutine = null; }
    }

    private float EffectiveReactionTime()
    {
        float floor = _aiConfig != null ? _aiConfig.minReactionTime : 0.08f;
        float profileTime = _profile != null ? _profile.reactionTime : 0.3f;
        return Mathf.Max(floor, profileTime);
    }

    private bool RollChance(double probability) => _random.NextDouble() < probability;

    /// <summary>The ABSOLUTE raw-axis value (see IFightInputSource.Horizontal's own -1..+1 contract)
    /// that currently resolves to Forward for this fighter — i.e. exactly what to feed
    /// AIFightInputSource.SetDirection to move towards the opponent (negate it for Back).
    ///
    /// Deliberately just mirrors _actor.FacingRight (+1 when true, -1 when false) instead of
    /// independently recomputing a world-X sign: FacingRight is, BY DEFINITION, "a positive raw
    /// axis value currently means Forward" (see RealFightFacingProvider.FacingRight's own doc — it's
    /// now resolved against the actual Fight camera's own screen-right axis, not world X, since the
    /// camera orbits the live fighters' line). Mirroring it here means this AI automatically stays
    /// correct under that same camera-relative convention WITHOUT ever touching the camera itself —
    /// no separate, parallel "which way is forward" computation exists to drift out of sync with
    /// what FighterInputController will actually resolve this exact value against.</summary>
    private float ApproachSign() => _actor.FacingRight ? 1f : -1f;

    // ── Decision ──────────────────────────────────────────────────────────────

    private void Decide(FightAIContext ctx)
    {
        var scores = ComputeScores(ctx);
        _lastScores = scores;

        var best = ArgMax(scores);
        FightAIIntention chosen;
        if (RollChance((_profile != null ? _profile.errorRate : 0.2f) * 0.4))
        {
            chosen = WeightedRandomExcluding(scores, best);
            LastErrorNote = "suboptimal decision";
        }
        else
        {
            chosen = best;
            LastErrorNote = null;
        }

        CurrentIntention = chosen;
        BeginIntention(chosen, ctx);
    }

    private static FightAIIntention ArgMax(Dictionary<FightAIIntention, float> scores)
    {
        var best = FightAIIntention.Wait;
        float bestScore = float.NegativeInfinity;
        foreach (var kv in scores)
        {
            if (kv.Value > bestScore) { bestScore = kv.Value; best = kv.Key; }
        }
        return best;
    }

    private FightAIIntention WeightedRandomExcluding(Dictionary<FightAIIntention, float> scores, FightAIIntention exclude)
    {
        var candidates = new List<FightAIIntention>();
        foreach (var kv in scores)
            if (kv.Key != exclude && kv.Value > 0.01f) candidates.Add(kv.Key);
        return candidates.Count == 0 ? FightAIIntention.Wait : candidates[_random.Next(candidates.Count)];
    }

    // ── Utility scoring — see class doc; simple heuristics, not a perfect solver ────────────────

    private Dictionary<FightAIIntention, float> ComputeScores(FightAIContext ctx)
    {
        float aggression  = _profile != null ? _profile.aggression : 0.5f;
        float defense      = _profile != null ? _profile.defenseProbability : 0.5f;
        float comboSkill    = _profile != null ? _profile.comboSkill : 0.5f;
        float specialUsage  = _profile != null ? _profile.specialUsage : 0.5f;
        float punishSkill   = _profile != null ? _profile.punishSkill : 0.5f;
        float spacingAccuracy = _profile != null ? _profile.spacingAccuracy : 0.5f;

        float spacingNoise = SpacingNoise();
        float meleeRange = _meleeRange + spacingNoise;
        float lowRange    = _lowRange + spacingNoise;
        float idealDistance = _meleeRange + (_aiConfig != null ? _aiConfig.idealDistancePadding : 0.6f) + spacingNoise;
        TargetIdealDistance = idealDistance;

        var scores = new Dictionary<FightAIIntention, float> { [FightAIIntention.Wait] = 0.08f };

        // Movement
        float approachRoom = Mathf.Max(0f, ctx.Distance - idealDistance);
        scores[FightAIIntention.Approach] = ctx.Distance > meleeRange
            ? Mathf.Clamp01(approachRoom / (idealDistance * 2f)) * Mathf.Lerp(0.2f, 0.9f, aggression)
            : 0f;

        bool opponentAttacking = ctx.OpponentMovePhase == FighterMoveState.Startup || ctx.OpponentMovePhase == FighterMoveState.Active;
        float retreatBase = ctx.Distance < idealDistance * 0.6f ? 0.35f : 0.05f;
        if (opponentAttacking && ctx.Distance < meleeRange * 1.3f) retreatBase += 0.25f;
        scores[FightAIIntention.Retreat] = retreatBase * Mathf.Lerp(0.9f, 0.3f, aggression);

        scores[FightAIIntention.DashApproach] = ctx.Distance > idealDistance * 1.3f ? Mathf.Lerp(0.05f, 0.55f, aggression) : 0f;
        scores[FightAIIntention.RunApproach]  = ctx.Distance > idealDistance * 1.8f ? Mathf.Lerp(0.03f, 0.45f, aggression) : 0f;

        // Defense — see DetectIncomingAttack's own doc
        var incoming = DetectIncomingAttack(ctx);
        float guardBase = 0.03f, crouchGuardBase = 0.02f, crouchBase = 0.03f, jumpDefenseBase = 0f, retreatDefenseBonus = 0f;
        if (incoming.present)
        {
            if (incoming.guardType == GuardType.Unblockable)
            {
                jumpDefenseBase = 0.5f;
                retreatDefenseBonus = 0.4f;
            }
            else if (incoming.height == AttackHeight.Low)
            {
                crouchGuardBase = 0.8f;
                guardBase = 0.15f; // the WRONG guard against a Low — only picked via an error roll
            }
            else if (incoming.height == AttackHeight.High)
            {
                guardBase = 0.7f;
                crouchBase = 0.3f;
            }
            else // Mid
            {
                guardBase = 0.75f;
                crouchGuardBase = 0.1f; // wrong against Mid
            }
        }
        scores[FightAIIntention.Guard]       = guardBase * defense;
        scores[FightAIIntention.CrouchGuard] = crouchGuardBase * defense;
        scores[FightAIIntention.Crouch]      = crouchBase * defense * 0.6f;
        scores[FightAIIntention.Jump]        = jumpDefenseBase * defense + 0.04f * aggression;
        if (retreatDefenseBonus > 0f) scores[FightAIIntention.Retreat] += retreatDefenseBonus * defense;

        // Offense
        bool inMeleeRange      = ctx.Distance <= meleeRange;
        bool inLowRange        = ctx.Distance <= lowRange;
        bool inProjectileRange = ctx.Distance <= _projectileRange;

        scores[FightAIIntention.NormalAttack] = inMeleeRange ? Mathf.Lerp(0.25f, 0.7f, aggression) : 0f;
        scores[FightAIIntention.LowAttack] = inLowRange
            ? Mathf.Lerp(0.15f, 0.6f, aggression) * (ctx.OpponentGuardState == FighterGuardState.StandingGuard ? 1.4f : 0.7f)
            : 0f;
        scores[FightAIIntention.ComboAttack] = inMeleeRange ? Mathf.Lerp(0.05f, 0.65f, comboSkill) * Mathf.Lerp(0.4f, 1f, aggression) : 0f;
        scores[FightAIIntention.SpecialAttack] = (inProjectileRange || inMeleeRange)
            ? Mathf.Lerp(0.05f, 0.55f, specialUsage) * (ctx.Distance > meleeRange ? 1.3f : 0.7f)
            : 0f;

        // Punish
        bool punishWindow = ctx.OpponentMovePhase == FighterMoveState.Recovery &&
                             ctx.OpponentMovePhaseProgress01 < (_aiConfig != null ? _aiConfig.punishWindowMaxProgress : 0.6f) &&
                             ctx.Distance <= meleeRange * 1.2f;
        scores[FightAIIntention.Punish] = punishWindow ? Mathf.Lerp(0.3f, 1.3f, punishSkill) : 0f;

        // Sidestep — reacting to an incoming attack that's actually dodgeable this way (task's own
        // explicit "especialment contra projectils lineals, moves amb poc tracking" — Homing is
        // deliberately excluded here since a sidestep isn't meant to beat it, even though none are
        // authored yet). Split evenly between Left/Right — either genuinely dodges a straight-line
        // attack (see FighterAttack._activeAttackForward's own doc), so the actual side is
        // arbitrary; still fully gated by defenseProbability like every other defensive choice.
        bool incomingIsDodgeable = incoming.present && incoming.guardType != GuardType.Unblockable &&
                                    incoming.tracking != FightHitTracking.Homing;
        float sidestepBase = incomingIsDodgeable ? 0.55f : 0.03f;
        scores[FightAIIntention.SideStepLeft]  = sidestepBase * defense * 0.5f;
        scores[FightAIIntention.SideStepRight] = sidestepBase * defense * 0.5f;

        // SideWalk — a puntual repositioning tool, never a constant circling (task's own explicit
        // "no facis que doni voltes constants"): low baseline, only rises a little with
        // spacingAccuracy, and only when nothing urgent (no incoming attack) is happening.
        scores[FightAIIntention.SideWalk] = incoming.present ? 0f : 0.05f * spacingAccuracy;

        // BackDash — an alternative, more decisive burst of separation than plain Retreat when the
        // opponent is already too close AND actively attacking.
        scores[FightAIIntention.BackDash] = (opponentAttacking && ctx.Distance < meleeRange * 1.1f)
            ? Mathf.Lerp(0.1f, 0.5f, defense) : 0f;

        return scores;
    }

    private float SpacingNoise()
    {
        float spacingAccuracy = _profile != null ? _profile.spacingAccuracy : 0.5f;
        float maxError = _aiConfig != null ? _aiConfig.maxSpacingError : 1.5f;
        float error = Mathf.Lerp(maxError, 0f, spacingAccuracy);
        return (float)(_random.NextDouble() * 2.0 - 1.0) * error;
    }

    private (AttackHeight height, GuardType guardType, FightHitTracking tracking, bool present) DetectIncomingAttack(FightAIContext ctx)
    {
        if (ctx.OpponentCurrentMove != null &&
            (ctx.OpponentMovePhase == FighterMoveState.Startup || ctx.OpponentMovePhase == FighterMoveState.Active))
        {
            if (ctx.OpponentCurrentMove.attackDelivery == AttackDelivery.Melee &&
                ctx.OpponentCurrentMove.hits != null && ctx.OpponentCurrentMove.hits.Length > 0)
            {
                var hit = ctx.OpponentCurrentMove.hits[0];
                return (hit.attackHeight, hit.guardType, hit.tracking, true);
            }
        }
        if (ctx.IncomingProjectile != null && ctx.IncomingProjectile.HitDefinition != null)
            return (ctx.IncomingProjectile.HitDefinition.attackHeight, ctx.IncomingProjectile.HitDefinition.guardType,
                    ctx.IncomingProjectile.HitDefinition.tracking, true);

        return (AttackHeight.Mid, GuardType.Blockable, FightHitTracking.Linear, false);
    }

    // ── Range estimation (see class doc: "no vull que l'AI consulti overlap futur exacte") ───────

    private static float EstimateHitRange(FightMoveDefinition move)
    {
        if (move == null || move.hits == null || move.hits.Length == 0) return 1f;
        var hit = move.hits[0];
        return Mathf.Max(0.3f, hit.localOffset.x + hit.size.x * 0.5f);
    }

    private FightMoveDefinition FindMoveWithHeight(AttackHeight height)
    {
        if (_moveSet?.moves == null) return null;
        foreach (var m in _moveSet.moves)
            if (m != null && m.attackDelivery == AttackDelivery.Melee && m.hits != null && m.hits.Length > 0 && m.hits[0].attackHeight == height)
                return m;
        return null;
    }

    private FightMoveDefinition FindProjectileMove()
    {
        if (_moveSet?.moves == null) return null;
        foreach (var m in _moveSet.moves)
            if (m != null && m.attackDelivery == AttackDelivery.Projectile) return m;
        return null;
    }

    private FightComboDefinition ChooseCombo()
    {
        if (_comboSet?.combos == null) return null;
        float comboSkill = _profile != null ? _profile.comboSkill : 0.5f;
        int maxSteps = comboSkill < 0.34f ? 2 : comboSkill < 0.7f ? 3 : 5;

        var candidates = new List<FightComboDefinition>();
        foreach (var c in _comboSet.combos)
        {
            if (c == null || c.steps == null || c.steps.Length < 2 || c.steps.Length > maxSteps) continue;
            if (string.IsNullOrEmpty(c.moveId) || c.moveId == DashForwardMoveId) continue;
            candidates.Add(c);
        }
        return candidates.Count == 0 ? null : candidates[_random.Next(candidates.Count)];
    }

    // ── Execution — every intention becomes REAL AIFightInputSource calls, nothing else ──────────
    // (see class doc's NO FER TRAMPES checklist). Coroutines never touch _executionRoutine
    // themselves — only RunToCompletion (the single wrapper StartCoroutine actually launches) does,
    // avoiding any nested-coroutine race on that field.

    private void BeginIntention(FightAIIntention intention, FightAIContext ctx)
    {
        StopExecuting();
        float dirSign = ApproachSign();

        IEnumerator routine = intention switch
        {
            FightAIIntention.Jump           => JumpRoutine(),
            FightAIIntention.DashApproach   => DashRunRoutine(dirSign, false),
            FightAIIntention.RunApproach    => DashRunRoutine(dirSign, true),
            FightAIIntention.NormalAttack   => NormalAttackRoutine(),
            FightAIIntention.LowAttack      => LowAttackRoutine(),
            FightAIIntention.ComboAttack    => ComboRoutine(dirSign),
            FightAIIntention.SpecialAttack  => SpecialRoutine(dirSign),
            FightAIIntention.Punish         => (_profile != null && _profile.comboSkill > 0.5f && RollChance(0.5)) ? ComboRoutine(dirSign) : NormalAttackRoutine(),
            // BackDash reuses DashRunRoutine verbatim with the sign negated (away from the
            // opponent) — "Back,Back" is exactly "Forward,Forward" with the absolute direction
            // flipped, never a second, parallel dash implementation (see FighterMovement's own doc).
            FightAIIntention.BackDash       => DashRunRoutine(-dirSign, false),
            FightAIIntention.SideStepLeft   => SidestepRoutine(-1),
            FightAIIntention.SideStepRight  => SidestepRoutine(1),
            FightAIIntention.SideWalk       => SideWalkRoutine(RollChance(0.5) ? -1 : 1),
            _ => null,
        };

        if (routine != null)
        {
            _executionRoutine = StartCoroutine(RunToCompletion(routine));
            return;
        }

        switch (intention)
        {
            case FightAIIntention.Wait:        _inputSource.SetDirection(0f, 0f); break;
            case FightAIIntention.Approach:    _inputSource.SetDirection(dirSign, 0f); break;
            case FightAIIntention.Retreat:     _inputSource.SetDirection(-dirSign, 0f); break;
            case FightAIIntention.Guard:       _inputSource.SetDirection(-dirSign, 0f); break;
            case FightAIIntention.CrouchGuard: _inputSource.SetDirection(-dirSign, -1f); break;
            case FightAIIntention.Crouch:      _inputSource.SetDirection(0f, -1f); break;
        }
    }

    private IEnumerator RunToCompletion(IEnumerator routine)
    {
        yield return routine;
        if (_inputSource != null) _inputSource.SetDirection(0f, 0f);
        _executionRoutine = null;
    }

    private IEnumerator JumpRoutine()
    {
        // Must clear FighterMovement's own tap/hold threshold to resolve as a real Jump instead of
        // a Sidestep tap (see that class's own doc on Up/Down now being dual-purpose) — held for
        // threshold + a small margin, exactly like a human's held-but-brief jump press.
        float holdTime = (_arenaConfig != null ? _arenaConfig.directionHoldThreshold : 0.15f) + 0.05f;
        _inputSource.SetDirection(0f, 1f);
        yield return new WaitForSeconds(holdTime);
    }

    private IEnumerator SidestepRoutine(int sideSign)
    {
        // A short tap — safely under FighterMovement's own directionHoldThreshold — real input
        // through the exact same tap/hold pipeline a human uses (see class doc's own "no fer
        // trampes" checklist), never a direct Sidestep trigger.
        float threshold = _arenaConfig != null ? _arenaConfig.directionHoldThreshold : 0.15f;
        float tapTime = Mathf.Min(0.06f, threshold * 0.4f);
        _inputSource.SetDirection(0f, sideSign);
        yield return new WaitForSeconds(tapTime);
        _inputSource.SetDirection(0f, 0f);
        yield return new WaitForSeconds(0.05f);
    }

    private IEnumerator SideWalkRoutine(int sideSign)
    {
        // Double-tap the SAME vertical direction, then HOLD the second press well past the
        // threshold — exactly what FighterMovement's own double-tap+hold detection requires; a
        // genuinely real input sequence, never a direct SideWalk trigger.
        float threshold        = _arenaConfig != null ? _arenaConfig.directionHoldThreshold : 0.15f;
        float doubleTapWindow  = _arenaConfig != null ? _arenaConfig.doubleTapWindow : 0.3f;
        float tapTime = Mathf.Min(0.06f, threshold * 0.4f);
        float gap     = Mathf.Min(0.05f, doubleTapWindow * 0.3f);

        _inputSource.SetDirection(0f, sideSign);
        yield return new WaitForSeconds(tapTime);
        _inputSource.SetDirection(0f, 0f);
        yield return new WaitForSeconds(gap);
        _inputSource.SetDirection(0f, sideSign);
        // Held well past the threshold — walks sideways for a bit, then releases like a human
        // letting go, rather than sidewalking forever.
        yield return new WaitForSeconds(threshold + 0.6f);
    }

    private IEnumerator DashRunRoutine(float dirSign, bool run)
    {
        _inputSource.SetDirection(dirSign, 0f);
        yield return new WaitForSeconds(0.05f);
        _inputSource.SetDirection(0f, 0f); // brief neutral — creates the real edge the SECOND tap needs
        yield return new WaitForSeconds(0.05f);
        _inputSource.SetDirection(dirSign, 0f); // second tap -> "Forward, Forward" recognized for real
        yield return new WaitForSeconds(run ? 0.6f : 0.3f);
    }

    private IEnumerator NormalAttackRoutine()
    {
        _inputSource.SetDirection(0f, 0f);
        yield return null;
        if (RollChance(0.5)) _inputSource.RequestPunch(); else _inputSource.RequestKick();
        yield return new WaitForSeconds(0.1f);
    }

    private IEnumerator LowAttackRoutine()
    {
        _inputSource.SetDirection(0f, -1f);
        yield return new WaitForSeconds(0.05f);
        _inputSource.RequestKick();
        yield return new WaitForSeconds(0.15f);
    }

    private IEnumerator SpecialRoutine(float dirSign)
    {
        _inputSource.SetDirection(dirSign, -1f);
        yield return new WaitForSeconds(0.05f);
        _inputSource.RequestPunch();
        yield return new WaitForSeconds(0.15f);
    }

    private IEnumerator ComboRoutine(float dirSign)
    {
        var combo = ChooseCombo();
        if (combo == null)
        {
            yield return NormalAttackRoutine();
            yield break;
        }

        LastSelectedCombo = combo;
        float comboSkill = _profile != null ? _profile.comboSkill : 0.5f;
        float errorRate  = _profile != null ? _profile.errorRate : 0.2f;

        for (int i = 0; i < combo.steps.Length; i++)
        {
            LastComboStepIndex = i;
            var step = combo.steps[i];

            if (RollChance(errorRate * 0.35))
            {
                LastErrorNote = $"skipped combo input {i + 1}/{combo.steps.Length}";
                continue; // a genuinely dropped input — the recognizer simply never sees this step
            }

            float h = step.horizontal == FightHorizontalDirection.Forward ? dirSign : step.horizontal == FightHorizontalDirection.Back ? -dirSign : 0f;
            float v = step.vertical == FightVerticalDirection.Up ? 1f : step.vertical == FightVerticalDirection.Down ? -1f : 0f;
            _inputSource.SetDirection(h, v);
            yield return null;

            if (step.button == FightButton.Punch) _inputSource.RequestPunch();
            else if (step.button == FightButton.Kick) _inputSource.RequestKick();

            float gap = Mathf.Lerp(0.24f, 0.09f, comboSkill);
            if (RollChance(errorRate * 0.3))
            {
                LastErrorNote = $"mistimed combo input {i + 1}/{combo.steps.Length}";
                gap += combo.maxTimeBetweenInputs + 0.1f; // deliberately blow the window -> recognizer fails the sequence for real
            }
            yield return new WaitForSeconds(gap);
        }

        LastComboStepIndex = -1;
    }
}
