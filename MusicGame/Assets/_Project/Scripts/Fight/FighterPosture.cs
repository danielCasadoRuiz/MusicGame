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
/// </summary>
public enum FighterMovementState
{
    Idle,
    Walk,
    Dash,
    Run,
}
