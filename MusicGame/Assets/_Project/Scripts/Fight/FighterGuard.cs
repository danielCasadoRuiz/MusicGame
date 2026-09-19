using UnityEngine;

/// <summary>What a fighter is actively guarding against right now — see FighterGuard.WouldBlock's
/// own doc for exactly what each state stops.</summary>
public enum FighterGuardState
{
    None,
    StandingGuard,
    CrouchGuard,
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

    public void Initialize(FighterActor actor, FighterInputController input)
    {
        _actor = actor;
        _input = input;
    }

    private void Update()
    {
        State = ComputeState();
    }

    private FighterGuardState ComputeState()
    {
        if (_input == null || !CanGuard()) return FighterGuardState.None;
        if (_input.CurrentHorizontal != FightHorizontalDirection.Back) return FighterGuardState.None;
        return _input.CurrentVertical == FightVerticalDirection.Down
            ? FighterGuardState.CrouchGuard
            : FighterGuardState.StandingGuard;
    }

    /// <summary>Public — a future AI needs to know its own (and read the opponent's) guard
    /// legality/state too (see this phase's own "future AI" scope note).</summary>
    public bool CanGuard()
    {
        if (_actor == null) return false;
        if (_actor.Health != null && _actor.Health.IsKO) return false;
        if (_actor.HitReaction != null && (_actor.HitReaction.IsInHitStun || _actor.HitReaction.IsInBlockStun)) return false;
        // Any move actually running (Startup/Active/Recovery) is "incompatible" for V1 — simple and
        // coherent rather than an authored per-move allow-list (see this phase's own scope note).
        if (_actor.MoveController != null && _actor.MoveController.CurrentPhase != FighterMoveState.Idle) return false;
        // No air guard in V1 — committing to a jump means committing to landing, same as most
        // fighting games' own convention.
        if (_actor.Posture == FighterPosture.Airborne) return false;
        return true;
    }

    /// <summary>Called by FighterAttack/FightProjectile at the moment a hit would land — the ONLY
    /// place guard actually has a combat effect (see class doc on why arming guard itself has no
    /// separate "in range" concept).</summary>
    public bool WouldBlock(AttackHeight height, GuardType guardType)
    {
        if (guardType == GuardType.Unblockable) return false;
        return State switch
        {
            FighterGuardState.StandingGuard => height == AttackHeight.High || height == AttackHeight.Mid,
            FighterGuardState.CrouchGuard    => height == AttackHeight.Low,
            _ => false,
        };
    }
}
