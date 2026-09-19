using UnityEngine;

/// <summary>
/// A hand-authored FighterStats snapshot — how an Opponent gets combat stats WITHOUT ever running
/// the Runner (see OpponentLevelConfig.combatStats' own doc). Deliberately NOT AIDifficultyProfile:
/// physical combat capability and AI skill are independent axes (a rival can be strong with a weak
/// AI, or the reverse) — see this phase's own scope note.
///
/// Same base-~100 scale as a real Runner-built FighterStats (see FighterStatsBuilder) so
/// FightCombatBalanceConfig's curves apply identically regardless of which of the two ever produced
/// a given FighterStats instance.
/// </summary>
[CreateAssetMenu(fileName = "FighterStatsProfile", menuName = "MusicGame/Fight/Fighter Stats Profile")]
public class FighterStatsProfileSO : ScriptableObject
{
    [System.Serializable]
    public class StatEntry
    {
        public FightStatId statId;
        public float value = 100f;
    }

    public StatEntry[] stats = new StatEntry[]
    {
        new StatEntry { statId = FightStatId.Strength,     value = 100f },
        new StatEntry { statId = FightStatId.Speed,        value = 100f },
        new StatEntry { statId = FightStatId.Agility,      value = 100f },
        new StatEntry { statId = FightStatId.Defense,      value = 100f },
        new StatEntry { statId = FightStatId.Combo,        value = 100f },
        new StatEntry { statId = FightStatId.Knockback,    value = 100f },
        new StatEntry { statId = FightStatId.SpecialPower, value = 100f },
        new StatEntry { statId = FightStatId.Balance,      value = 100f },
    };

    /// <summary>A fresh FighterStats built from this profile's authored values — any statId with no
    /// entry here simply stays at FighterStats.Get's own default (0), so an incomplete profile is
    /// easy to spot instead of silently reading as 100.</summary>
    public FighterStats Build()
    {
        var result = new FighterStats();
        if (stats != null)
            foreach (var entry in stats)
                result.Set(entry.statId, entry.value);
        return result;
    }
}
