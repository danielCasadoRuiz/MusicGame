/// <summary>
/// Consumable/special combat resources (extra lives, revives, shields, heals, special attacks) —
/// completely independent of run performance. NOT derived from RunnerFightResources/FighterStats,
/// and not populated by the Runner at all in V1 (every count starts at its default of 0).
///
/// Kept as its own type from day one specifically so a future source (purchases, meta-
/// progression, drops, Player Level rewards...) can populate this without reshaping FighterStats
/// or RunnerFightResources — and so a future decision about whether these are spent per-fight or
/// accumulate across fights has a single, obvious place to live.
/// </summary>
public class FightResources
{
    public int ExtraLives;
    public int Revives;
    public int Shields;
    public int Heals;
    public int SpecialAttacks;
}
