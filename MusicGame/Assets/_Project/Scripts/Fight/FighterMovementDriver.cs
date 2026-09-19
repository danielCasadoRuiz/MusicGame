/// <summary>
/// The boundary between Move and actual locomotion — FighterMoveController never touches a
/// Transform/Rigidbody/CharacterController directly, only these two calls. A real implementation
/// (once a real FighterActor with real 2.5D movement exists) would apply the lock/multiplier to
/// whatever reads player input for movement each frame, and translate the fighter forward by
/// ApplyLunge's distance along its own current facing — nothing about FighterMoveController or
/// FightMoveDefinition would need to change to add it.
/// </summary>
public interface IFighterMovementDriver
{
    /// <summary>Called once, the instant a move starts (and once more, unlocked, the instant it
    /// ends) — see FightMoveDefinition.movementLocked/movementMultiplier's own doc.</summary>
    void SetMovementLock(bool locked, float multiplier);

    /// <summary>Called once, at move start, if FightMoveDefinition.lungeDistance is nonzero.</summary>
    void ApplyLunge(float distance);
}

/// <summary>
/// V1 placeholder — no real Transform/locomotion exists yet. Just remembers the last values it was
/// told, for FightDebugHUD/tests to inspect. Swap FighterMoveController's driver for a real one
/// once a real FighterActor exists; nothing else in this layer changes.
/// </summary>
public class DebugFighterMovementDriver : IFighterMovementDriver
{
    public bool IsLocked { get; private set; }
    public float Multiplier { get; private set; } = 1f;
    public float LastLungeDistance { get; private set; }

    public void SetMovementLock(bool locked, float multiplier)
    {
        IsLocked = locked;
        Multiplier = multiplier;
    }

    public void ApplyLunge(float distance)
    {
        LastLungeDistance = distance;
    }
}
