/// <summary>
/// A fighter's physical stance — the single, authoritative answer to "is this fighter standing,
/// crouching, or in the air" that Move System (allowedPostures), hit resolution (hurtbox height —
/// see FighterHurtbox), guard (StandingGuard vs CrouchGuard), future AI, and future animation all
/// read from the SAME place (FighterActor.Posture) instead of each re-deriving it independently.
/// Owned/written exclusively by FighterMovement (Player this phase) via FighterActor.SetPosture.
/// </summary>
public enum FighterPosture
{
    Standing,
    Crouching,
    Airborne,
}

/// <summary>
/// A fighter's current LOCOMOTION gait — orthogonal to FighterPosture (e.g. Run only ever applies
/// while Standing). Owned/written exclusively by FighterMovement via FighterActor.SetMovementState.
/// Purely descriptive/debug + a gating input for FightMoveDefinition.requiredMovementStates (e.g. a
/// "Run + Punch" running attack) — nothing here changes movement math itself; FighterMovement's own
/// fields (speed multipliers, locks) still do that.
///
/// SideStep/SideWalk (see FighterMovement's own doc) are both movement ALONG the fighter's SideXZ
/// axis rather than ForwardXZ — SideStep is the short, timed dodge; SideWalk is the continuous,
/// held-input version reached via a double-tap. Dash covers BOTH the forward dash and the backdash
/// (see FighterMovement.UpdateMovementState's own doc) — direction isn't encoded in the state itself,
/// same as Walk never distinguishing Forward from Back.
/// </summary>
public enum FighterMovementState
{
    Idle,
    Walk,
    Dash,
    Run,
    SideStep,
    SideWalk,
}
