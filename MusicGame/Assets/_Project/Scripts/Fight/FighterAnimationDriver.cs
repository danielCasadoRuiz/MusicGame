/// <summary>
/// The boundary between Move and Animation — FighterMoveController never touches an Animator or
/// any animation-clip/state string directly; it only calls these two methods. A real implementation
/// (once fighter prefabs actually have an Animator) would translate PlayMoveAnimation's
/// move.animationState into a real Animator.Play/SetTrigger call, and SetLocomotion's direction
/// into a blend-tree parameter — nothing about FighterMoveController or FightMoveDefinition would
/// need to change to add it.
/// </summary>
public interface IFighterAnimationDriver
{
    /// <summary>Whatever the driver considers "showing" right now — purely for debug display
    /// (FightDebugHUD) until a real Animator exists to actually query.</summary>
    string CurrentState { get; }

    /// <summary>Called once, the instant a move starts. `gameplayDuration` = the move's authoritative
    /// Startup+Active+Recovery length (after stat timing scale) — the animation adapts to it.</summary>
    /// <summary>`gameplayDuration` = the move's BASE length; `speedScale` = the fighter's attack speed (Agility),
    /// applied on top of the clip fitting so a more agile fighter's strikes visibly play faster.</summary>
    void PlayMoveAnimation(FightMoveDefinition move, float gameplayDuration, float speedScale = 1f);

    /// <summary>Same, with the limb SIDE that must strike (+1 right / −1 left / 0 as authored): a sided strike clip
    /// thrown with its other side plays mirrored. Returns the side that really strikes (0 = not a sided strike).</summary>
    int PlayMoveAnimation(FightMoveDefinition move, float gameplayDuration, float speedScale, int side);

    /// <summary>Raises the guard pose (CombatRole.Block) over `raiseTime` — the fighter's Agility-scaled guard
    /// raise time (FighterGuard.RaiseTime): the pose is fully up exactly when the guard starts blocking.</summary>
    void PlayGuard(float raiseTime);

    /// <summary>The guard pose for `state` on `side` (+1 right / −1 left, the fighter's own frame; 0 = the central
    /// third-person guard). Falls back to the central Block pose when the set has no sided clip.</summary>
    void PlayGuard(float raiseTime, FighterGuardState state, int side);

    /// <summary>A sided presentation role (Sidestep, HitReactionSide): `side` +1 / −1 picks the clip as authored or
    /// mirrored. `duration` > 0 fits the clip to it. Repeated calls with the same role and side keep it playing.</summary>
    void PlaySided(CombatRole role, int side, float duration = -1f);

    /// <summary>Shows a non-move role (HitReaction, Knockdown, Downed, GetUp, Block, Victory...). `duration`
    /// > 0 fits the clip to that many seconds (clamped); otherwise natural speed.</summary>
    void PlayRole(CombatRole role, float duration = -1f);

    /// <summary>Called every frame while Idle (no move running) — locomotion-driven, not move-driven.
    /// `state`/`posture` (FighterMovement's own) pick walk / run / jump; `turning` = the body is still
    /// rotating towards a new facing (cross-up).</summary>
    void SetLocomotion(FightHorizontalDirection direction, FighterMovementState state, FighterPosture posture, bool turning);
}

/// <summary>
/// V1 placeholder — no Animator/clips exist yet (no real FighterActor exists at all — see this
/// phase's own scope notes). Just remembers the last state name as a plain string, for
/// FightDebugHUD to show ("current animation/debug state"). Swap FighterMoveController's driver
/// for a real Animator-backed one once fighter prefabs exist; nothing else in this layer changes.
/// </summary>
public class DebugFighterAnimationDriver : IFighterAnimationDriver
{
    public string CurrentState { get; private set; } = "Idle";

    public void PlayGuard(float raiseTime) => CurrentState = CombatRole.Block.ToString();
    public void PlayGuard(float raiseTime, FighterGuardState state, int side) => CurrentState = CombatRole.Block.ToString();
    public void PlaySided(CombatRole role, int side, float duration = -1f) => CurrentState = role.ToString();

    public void PlayMoveAnimation(FightMoveDefinition move, float gameplayDuration, float speedScale = 1f)
    {
        CurrentState = move != null && !string.IsNullOrEmpty(move.animationState) ? move.animationState : "(move, no animationState set)";
    }

    public int PlayMoveAnimation(FightMoveDefinition move, float gameplayDuration, float speedScale, int side)
    {
        PlayMoveAnimation(move, gameplayDuration, speedScale);
        return side;
    }

    public void PlayRole(CombatRole role, float duration = -1f) => CurrentState = role.ToString();

    public void SetLocomotion(FightHorizontalDirection direction, FighterMovementState state, FighterPosture posture, bool turning)
    {
        CurrentState = posture == FighterPosture.Airborne ? "Jump" : state == FighterMovementState.Run ? "Run" : direction switch
        {
            FightHorizontalDirection.Forward => "WalkForward",
            FightHorizontalDirection.Back    => "WalkBack",
            _                                 => "Idle",
        };
    }
}
