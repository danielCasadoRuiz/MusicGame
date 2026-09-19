/// <summary>
/// TEMPORARY debug/testing source for the global Player Level — until the real progression system
/// exists (see LevelContext), every OpponentDefinition.GetConfigForLevel call site across Fight
/// (OpponentSelectionController's grid/roulette, VersusScreenController, eventually the real
/// combat/AI difficulty lookup) reads Current here instead of each hardcoding its own value, so
/// wiring in the real system later is a one-line change in ONE place, not a hunt across every
/// caller. Defaults to 1 (a player who has actually started playing), not 0.
/// </summary>
public static class DebugPlayerLevel
{
    public static int Current = 1;
}
