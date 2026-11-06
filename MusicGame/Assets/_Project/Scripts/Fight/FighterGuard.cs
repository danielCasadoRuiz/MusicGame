using UnityEngine;

/// <summary>What a fighter is actively guarding against right now — see FighterGuard.WouldBlock's
/// own doc for exactly what each state stops.</summary>
public enum FighterGuardState
{
    None,
    /// <summary>MID guard — Back (neutral vertical). Blocks Mid attacks.</summary>
    StandingGuard,
    /// <summary>LOW guard — Back + Down (crouched hurtbox, so High attacks also whiff over it).</summary>
    CrouchGuard,
    /// <summary>HIGH guard — Back + Up. Blocks High attacks.</summary>
    HighGuard,
}

/// <summary>
/// Player and Opponent share this EXACT component — universal like FighterHealth/FighterHitReaction,
/// even though only the Player has real input driving it this phase (a future AIFightInputSource-
/// driven Opponent would guard through the identical path).
///
/// RULE (deliberately simple — no proximity/frame-perfect detection, per this phase's own explicit
/// ask): holding Back ALWAYS both retreats (FighterMovement's own existing Back handling is
/// untouched) AND arms guard, at the same time — exactly like a real 2D fighting game lets you walk
/// backward while blocking. There is no separate "is an attack actually incoming" check; guard is
/// simply "is Back (or Down+Back) currently held, and is this fighter allowed to guard right now"
/// (see CanGuard). Whether that guard state actually stops a given hit is a separate, later query
/// (WouldBlock, called by FighterAttack/FightProjectile at the moment of overlap) — so a fighter
/// arms guard by holding Back regardless of whether anything is actually in range, exactly like
/// holding Back has always meant "retreat" regardless of whether retreating matters right now.
///
/// V1 TRIANGLE (see FightHitDefinition.attackHeight/guardType's own doc):
///   StandingGuard (Back)        blocks High, Mid   — never Low.
///   CrouchGuard   (Down+Back)   blocks Low          — never Mid; High simply never reaches a
///                                                      crouching hurtbox in the first place (see
///                                                      FighterHurtbox's own crouch-height doc), so
///                                                      no explicit "let High through" rule is needed.
///   Unblockable moves ignore guard entirely, regardless of state.
/// </summary>
public class FighterGuard : MonoBehaviour
{
    private FighterActor _actor;
    private FighterInputController _input;

    public FighterGuardState State { get; private set; } = FighterGuardState.None;
    /// <summary>FIRST PERSON (user design 2026-11-05): the side guarded, in this fighter's own frame (+1 right / −1 left,
    /// = the screen side in the first-person view); 0 = no side (third person / the AI: the guard covers both sides).</summary>
    public int Side { get; private set; }
    /// <summary>The side the guard COVERS right now (+1 right / −1 left / 0 centre) — both views (user design 2026-11-05):
    /// first person = the side key held (Side); third person and the AI = the guard turns by itself to the side the
    /// opponent's strike comes from, as soon as that strike starts. A changed side only covers after SideReactTime.</summary>
    public int CoverSide { get; private set; }
    private float _coverSince = float.NegativeInfinity;
    /// <summary>Debug: why the last sided strike was or wasn't stopped.</summary>
    public string LastSideInfo { get; private set; } = "";
    /// <summary>Seconds to turn the guard to a new side (Agility-scaled; always shorter than a strike's startup at equal speed).</summary>
    public float SideReactTime
    {
        get
        {
            var b = Balance;
            if (b == null || _actor == null) return 0f;
            return b.guardSideReactTime / Mathf.Max(0.3f, b.ComputeModifiers(_actor.BuildStats).AttackSpeedMultiplier);
        }
    }
    // When the current guard INPUT began: a guard pressed after a strike started may feint it (CanCancelFor).
    private float _inputSince = float.NegativeInfinity;
    /// <summary>Tests only: a held guard (and its side) as if the keys were held. None = the real input.</summary>
    public FighterGuardState DebugForcedState { get; set; }
    public int DebugForcedSide { get; set; }
    private FighterGuardState _lastInput = FighterGuardState.None;

    // GUARD SPEED (2026-11-04): a guard only blocks once it has been held for RaiseTime — the defender's
    // FightCombatBalanceConfig.guardRaiseTime divided by its own attack speed (Agility). A fast attacker's
    // short startup can land before a slow defender's guard is up; a fast defender raises it sooner.
    private float _guardSince = float.NegativeInfinity;
    private static FightCombatBalanceConfig s_balance;
    private static FightCombatBalanceConfig Balance =>
        s_balance != null ? s_balance : (s_balance = Resources.Load<AppConfigSO>("AppConfig")?.combatBalance);

    /// <summary>Seconds this fighter needs to raise a guard (Agility-scaled).</summary>
    public float RaiseTime
    {
        get
        {
            var b = Balance;
            if (b == null || _actor == null) return 0f;
            return b.guardRaiseTime / Mathf.Max(0.3f, b.ComputeModifiers(_actor.BuildStats).AttackSpeedMultiplier);
        }
    }
    /// <summary>The held guard is actually up (held at least RaiseTime).</summary>
    public bool IsRaised => State != FighterGuardState.None && Time.time - _guardSince >= RaiseTime;

    public void Initialize(FighterActor actor, FighterInputController input)
    {
        _actor = actor;
        _input = input;
    }

    private void Update()
    {
        var next = ComputeState();
        if (next != FighterGuardState.None && !IsBlockMoveActive && _actor != null && _actor.MoveController != null)
            _actor.MoveController.TryCancelFor(_inputSince); // guard wins over the tail of an attack (or feints a fresh one)
        if (State == FighterGuardState.None && next != FighterGuardState.None) _guardSince = Time.time;
        State = next;
        UpdateCoverSide();
    }

    // Which side the guard covers (see CoverSide): the chosen side in first person, else the side of the opponent's
    // strike as soon as it starts (the defender sees the shoulder / hip load). The turn itself takes SideReactTime.
    private void UpdateCoverSide()
    {
        if (State == FighterGuardState.None) { CoverSide = 0; return; }
        int want = Side != 0 ? Side : IncomingSideNow();
        if (want == 0 || want == CoverSide) return;
        // A held guard turning by itself first has to SEE the strike (perception); a first-person side key or a guard
        // pressed just now is already the reaction.
        bool auto = Side == 0 && Time.time - _guardSince > 0.02f;
        CoverSide = want;
        _coverSince = Time.time + (auto ? Perception : 0f);
    }

    private float Perception
    {
        get
        {
            var b = Balance;
            if (b == null || _actor == null) return 0f;
            return b.guardSidePerception / Mathf.Max(0.3f, b.ComputeModifiers(_actor.BuildStats).AttackSpeedMultiplier);
        }
    }

    /// <summary>Where the opponent's running strike will arrive, in THIS fighter's frame (its right hand → my left).</summary>
    private int IncomingSideNow()
    {
        var opp = _actor != null ? _actor.Opponent : null;
        var mc = opp != null ? opp.MoveController : null;
        if (mc == null || mc.CurrentMove == null || mc.CurrentStrikeSide == 0) return 0;
        if (mc.CurrentPhase != FighterMoveState.Startup && mc.CurrentPhase != FighterMoveState.Active) return 0;
        return -mc.CurrentStrikeSide;
    }

    private FighterGuardState ComputeState()
    {
        // A Block MOVE (CombatRole.Block) guards for its whole duration, standing.
        if (IsBlockMoveActive) { Side = 0; return FighterGuardState.StandingGuard; }
        int side = 0;
        var wanted = DebugForcedState != FighterGuardState.None ? DebugForcedState : InputState(out side);
        if (DebugForcedState != FighterGuardState.None) side = DebugForcedSide;
        if (wanted != _lastInput) { _inputSince = Time.time; _lastInput = wanted; }
        Side = side;
        if (wanted == FighterGuardState.None || !CanGuard()) { Side = 0; return FighterGuardState.None; }
        return wanted;
    }

    // What the held directions ask for, before legality.
    private FighterGuardState InputState(out int side)
    {
        side = 0;
        if (_input == null) return FighterGuardState.None;
        // FIRST PERSON: hold Left / Right = guard THAT side (mid); + Up = that side high, + Down = that side low.
        if (_input.ViewRelative)
        {
            if (_input.CurrentSide == 0) return FighterGuardState.None;
            // Own strike starting / striking: the side key aims (or switches) the strike, it is not a guard.
            var own = _actor != null ? _actor.MoveController : null;
            if (own != null && (own.CurrentPhase == FighterMoveState.Startup || own.CurrentPhase == FighterMoveState.Active)) return FighterGuardState.None;
            side = _input.CurrentSide;
            return _input.CurrentVertical == FightVerticalDirection.Up   ? FighterGuardState.HighGuard
                 : _input.CurrentVertical == FightVerticalDirection.Down ? FighterGuardState.CrouchGuard
                                                                         : FighterGuardState.StandingGuard;
        }
        // Down alone (or Down + Back) = crouching LOW guard at once (user design 2026-11-04); Down + Forward is not a guard.
        if (_input.CurrentVertical == FightVerticalDirection.Down)
            return _input.CurrentHorizontal == FightHorizontalDirection.Forward ? FighterGuardState.None : FighterGuardState.CrouchGuard;
        if (_input.CurrentHorizontal != FightHorizontalDirection.Back) return FighterGuardState.None;
        return _input.CurrentVertical == FightVerticalDirection.Up ? FighterGuardState.HighGuard   // HIGH: Back + Up
                                                                    : FighterGuardState.StandingGuard; // MID: Back
    }

    /// <summary>Public — a future AI needs to know its own (and read the opponent's) guard
    /// legality/state too (see this phase's own "future AI" scope note).</summary>
    public bool IsBlockMoveActive =>
        _actor != null && _actor.MoveController != null && _actor.MoveController.CurrentMove != null &&
        _actor.MoveController.CurrentMove.role == CombatRole.Block &&
        (_actor.HitReaction == null || !_actor.HitReaction.IsInKnockdownFlow);

    public bool CanGuard()
    {
        if (_actor == null) return false;
        if (_actor.Health != null && _actor.Health.IsKO) return false;
        if (_actor.HitReaction != null && (_actor.HitReaction.IsInHitStun || _actor.HitReaction.IsInBlockStun)) return false;
        // Any move actually running (Startup/Active/Recovery) is "incompatible" for V1 — simple and
        // coherent rather than an authored per-move allow-list (see this phase's own scope note).
        // ...except an attack already past its strike: guarding cancels the rest of its Recovery (see Update).
        if (_actor.MoveController != null && !_actor.MoveController.CanCancelFor(_inputSince)) return false;
        // No air guard in V1 — committing to a jump means committing to landing, same as most
        // fighting games' own convention.
        if (_actor.Posture == FighterPosture.Airborne) return false;
        return true;
    }

    /// <summary>Called by FighterAttack/FightProjectile at the moment a hit would land — the ONLY
    /// place guard actually has a combat effect (see class doc on why arming guard itself has no
    /// separate "in range" concept).</summary>
    public bool WouldBlock(AttackHeight height, GuardType guardType) => WouldBlock(height, guardType, 0);

    /// <summary>`incomingSide` = where the strike arrives in THIS fighter's frame (+1 right / −1 left / 0 central).
    /// A sided (first-person) guard only stops strikes on its own side.</summary>
    public bool WouldBlock(AttackHeight height, GuardType guardType, int incomingSide) => WouldBlock(height, guardType, incomingSide, false);

    /// <summary>`fromAbove` = the attacker strikes from a jump (falling onto the defender): only the HIGH guard stops it.</summary>
    public bool WouldBlock(AttackHeight height, GuardType guardType, int incomingSide, bool fromAbove)
    {
        if (guardType == GuardType.Unblockable) return false;
        // An explicit Block MOVE keeps its old full standing cover (High + Mid).
        if (IsBlockMoveActive) return height == AttackHeight.High || height == AttackHeight.Mid;
        if (fromAbove)
        {
            if (!IsRaised || State != FighterGuardState.HighGuard) { LastSideInfo = "from above: only the high guard stops it"; return false; }
        }
        else if (!IsRaised || !Blocks(State, height)) return false;
        if (incomingSide != 0)
        {
            float turned = Time.time - _coverSince, need = SideReactTime;
            if (CoverSide != incomingSide) { LastSideInfo = $"wrong side (covers {CoverSide}, strike from {incomingSide})"; return false; }
            if (turned < need) { LastSideInfo = $"late: guard turned {turned:0.00}s < {need:0.00}s"; return false; }
            LastSideInfo = $"in time: {turned:0.00}s ≥ {need:0.00}s";
        }
        return true;
    }

    /// <summary>The guard-height matrix (pure): each guard blocks ONLY its own height; a wrong
    /// guard lets the attack connect normally. (A High attack vs LOW guard usually whiffs anyway:
    /// the crouched hurtbox is below it.)</summary>
    public static bool Blocks(FighterGuardState state, AttackHeight height)
    {
        return state switch
        {
            FighterGuardState.HighGuard     => height == AttackHeight.High,
            FighterGuardState.StandingGuard => height == AttackHeight.Mid,
            FighterGuardState.CrouchGuard   => height == AttackHeight.Low,
            _ => false,
        };
    }
}
