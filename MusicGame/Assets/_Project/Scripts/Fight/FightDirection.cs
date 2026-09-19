using UnityEngine;

/// <summary>Horizontal direction RELATIVE to the fighter's own facing — never Left/Right. Forward
/// always means "towards the opponent", Back always means "away from the opponent", regardless of
/// which side of the arena/screen this fighter currently occupies. See FightDirectionResolver.</summary>
public enum FightHorizontalDirection
{
    Neutral,
    Forward,
    Back,
}

/// <summary>Vertical input direction — screen/world-space, not facing-relative (there's no
/// left/right ambiguity to resolve for "up"/"down").</summary>
public enum FightVerticalDirection
{
    Neutral,
    Up,
    Down,
}

/// <summary>
/// Converts a raw, ABSOLUTE input axis (as any IFightInputSource reports it — positive = screen-
/// right/up, with no idea which way this fighter is facing) into a direction relative to a given
/// IFightFacingProvider. The only place "which way am I facing" logic lives — combos are always
/// authored in Forward/Back terms (FightComboDefinition.Step), so whoever eventually provides a
/// real facing (today: DebugFightFacingProvider, always true) is the ONLY thing that needs to
/// change once real fighter positions/facing exist — this resolver and every combo definition
/// stay untouched.
/// </summary>
public static class FightDirectionResolver
{
    private const float DeadZone = 0.35f;

    public static FightHorizontalDirection ResolveHorizontal(float absoluteHorizontal, bool facingRight)
    {
        if (Mathf.Abs(absoluteHorizontal) < DeadZone) return FightHorizontalDirection.Neutral;
        bool towardsScreenRight = absoluteHorizontal > 0f;
        bool isForward = towardsScreenRight == facingRight;
        return isForward ? FightHorizontalDirection.Forward : FightHorizontalDirection.Back;
    }

    public static FightVerticalDirection ResolveVertical(float absoluteVertical)
    {
        if (absoluteVertical > DeadZone) return FightVerticalDirection.Up;
        if (absoluteVertical < -DeadZone) return FightVerticalDirection.Down;
        return FightVerticalDirection.Neutral;
    }
}
