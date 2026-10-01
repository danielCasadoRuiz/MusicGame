/// <summary>
/// A fighter's temporary Power State (activated by holding Down + Punch + Kick — see
/// FighterInputController's POWER CHORD doc). Pure runtime state on FighterActor.Power: while
/// active, FighterMoveController folds Multipliers into every attack it starts. Expires on its own.
/// Future UI/VFX/animation react to PowerStateChangedEvent or poll IsActive/Remaining — nothing
/// here knows about presentation. Costs 1 x4 (QuadCombo) — paid by FighterMoveController.TryActivatePower
/// only when activation succeeds. Never refreshes, extends or stacks while active.
/// </summary>
public class FighterPowerState
{
    public bool IsActive => Remaining > 0f;
    public float Remaining { get; private set; }
    public float Duration { get; private set; }
    public FightAttackBonus Multipliers { get; private set; } = FightAttackBonus.Identity;

    /// <summary>Starts the state if it isn't already active (false otherwise — no refresh/extend/stack);
    /// publishes PowerStateChangedEvent.</summary>
    public bool TryActivate(FighterActor owner, float duration, FightAttackBonus multipliers)
    {
        if (IsActive) return false;
        Duration = UnityEngine.Mathf.Max(0.01f, duration);
        Remaining = Duration;
        Multipliers = multipliers;
        EventBus.Publish(new PowerStateChangedEvent { Fighter = owner, Active = true, Duration = Duration });
        return true;
    }

    public void Tick(FighterActor owner, float dt)
    {
        if (!IsActive) return;
        Remaining = UnityEngine.Mathf.Max(0f, Remaining - dt);
        if (IsActive) return;
        UnityEngine.Debug.Log($"[FighterPowerState] {(owner != null ? owner.Side.ToString() : "?")} POWER STATE expired");
        EventBus.Publish(new PowerStateChangedEvent { Fighter = owner, Active = false });
    }

    /// <summary>Ends it immediately (round reset / match end), publishing the change if it was active.</summary>
    public void Clear(FighterActor owner)
    {
        if (!IsActive) return;
        Remaining = 0f;
        EventBus.Publish(new PowerStateChangedEvent { Fighter = owner, Active = false });
    }
}
