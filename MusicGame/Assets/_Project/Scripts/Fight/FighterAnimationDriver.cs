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

    /// <summary>Called once, the instant a move starts.</summary>
    void PlayMoveAnimation(FightMoveDefinition move);

    /// <summary>Called every frame while Idle (no move running) — locomotion-driven, not move-driven.</summary>
    void SetLocomotion(FightHorizontalDirection direction);
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

    public void PlayMoveAnimation(FightMoveDefinition move)
    {
        CurrentState = move != null && !string.IsNullOrEmpty(move.animationState) ? move.animationState : "(move, no animationState set)";
    }

    public void SetLocomotion(FightHorizontalDirection direction)
    {
        CurrentState = direction switch
        {
            FightHorizontalDirection.Forward => "WalkForward",
            FightHorizontalDirection.Back    => "WalkBack",
            _                                 => "Idle",
        };
    }
}
