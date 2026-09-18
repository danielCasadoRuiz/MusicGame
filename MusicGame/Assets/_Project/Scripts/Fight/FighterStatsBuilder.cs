/// <summary>
/// RunnerFightResources (normalized 0..1 potential) -> FighterStats (final combat numbers):
/// stat = baseValue + curve.Evaluate(resourceValue) * maxBonus, per FightStatsConfig entry. Pure/
/// stateless, same shape as RunnerFightResourceBuilder/PlayRangeResolver.
/// </summary>
public static class FighterStatsBuilder
{
    /// <summary>
    /// levelContext is accepted but INTENTIONALLY unused for now — see LevelContext's own doc.
    /// Wiring the parameter in today means that whichever way that decision goes, this signature
    /// doesn't need to change later.
    /// </summary>
    public static FighterStats Build(RunnerFightResources resources, FightStatsConfig config, LevelContext? levelContext = null)
    {
        var stats = new FighterStats();
        if (resources == null || config == null) return stats;

        foreach (var mapping in config.ringStats)
            stats.Set(mapping.statId, mapping.baseValue + mapping.curve.Evaluate(resources.Get(mapping.statId)) * mapping.maxBonus);

        stats.Set(config.balanceStatId,
            config.balanceBaseValue + config.balanceCurve.Evaluate(resources.Get(config.balanceStatId)) * config.balanceMaxBonus);

        return stats;
    }
}
