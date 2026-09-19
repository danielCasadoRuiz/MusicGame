using System.Collections.Generic;

/// <summary>
/// The Fighter's final combat-ready numbers — what the (future) combat system actually reads.
/// Built once by FighterStatsBuilder from RunnerFightResources + FightStatsConfig, right when a
/// run ends (see GameSession).
///
/// Deliberately holds ONLY the final numbers, never the 0..1 potential that produced them (that's
/// RunnerFightResources, kept separately) — a value here is a real combat property (e.g.
/// "Strength = 137"), not "how well did the run go".
/// </summary>
public class FighterStats
{
    private readonly Dictionary<FightStatId, float> _values = new();

    public IReadOnlyDictionary<FightStatId, float> Values => _values;

    public float Get(FightStatId statId) => _values.TryGetValue(statId, out var v) ? v : 0f;

    public void Set(FightStatId statId, float value) => _values[statId] = value;

    /// <summary>Flat 100-everywhere fallback — used whenever a fighter has no real source (no
    /// completed Runner run, no OpponentLevelConfig.combatStats assigned) rather than leaving
    /// Get() silently returning 0 (which would read as "worst possible stat" to every
    /// FightCombatBalanceConfig curve, not "neutral/unset").</summary>
    public static FighterStats Default()
    {
        var stats = new FighterStats();
        foreach (FightStatId id in System.Enum.GetValues(typeof(FightStatId)))
            stats.Set(id, 100f);
        return stats;
    }
}
