using UnityEngine;

/// <summary>
/// Balances the whole Runner→Fight pipeline entirely from the Inspector — no numbers hardcoded in
/// RunnerFightResourceBuilder/FighterStatsBuilder. Same pattern as MusicRunnerScoringConfig's own
/// AnimationCurve-driven balancing (timingQualityCurve, performanceRatingCurve, fallPenaltyCurve).
/// </summary>
[CreateAssetMenu(fileName = "FightStatsConfig", menuName = "MusicGame/Fight/Fight Stats Config")]
public class FightStatsConfig : ScriptableObject
{
    [System.Serializable]
    public class RingStatMapping
    {
        public FightStatId statId;
        public RingType    sourceType;

        [Tooltip("This stat's value when the run contributed nothing at all (curve output 0).")]
        public float baseValue = 100f;
        [Tooltip("How much this stat can gain on top of baseValue for a perfect (curve output 1) contribution.")]
        public float maxBonus = 100f;
        [Tooltip("X = performanceInput (CollectionRate x TimingAccuracy, 0..1 — or neutralInput " +
                 "below if this song's timeline never generated this RingType at all). " +
                 "Y = fraction of maxBonus actually granted.")]
        public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    [Tooltip("Used in place of CollectionRate x TimingAccuracy whenever a song's timeline never " +
             "generated a given RingType at all (Available == 0) — deliberately NOT hardcoded to " +
             "0.5: with an asymmetric curve, a different input may be needed to truly read as " +
             "\"neither helped nor hurt\" for that specific stat.")]
    [Range(0f, 1f)] public float neutralInput = 0.5f;

    [Header("Ring-sourced stats (Strength/Speed/Agility/Defense/Combo/Knockback/SpecialPower)")]
    public RingStatMapping[] ringStats = new RingStatMapping[]
    {
        new RingStatMapping { statId = FightStatId.Strength,     sourceType = RingType.Kick   },
        new RingStatMapping { statId = FightStatId.Speed,        sourceType = RingType.Snare  },
        new RingStatMapping { statId = FightStatId.Agility,      sourceType = RingType.HiHat  },
        new RingStatMapping { statId = FightStatId.Defense,      sourceType = RingType.Beat   },
        new RingStatMapping { statId = FightStatId.Combo,        sourceType = RingType.Onset  },
        new RingStatMapping { statId = FightStatId.Knockback,    sourceType = RingType.Impact },
        new RingStatMapping { statId = FightStatId.SpecialPower, sourceType = RingType.Peak   },
    };

    // ── Balance — transversal: falls + overall precision, not tied to a single RingType ──────
    [Header("Balance (transversal — falls + overall precision)")]
    public FightStatId balanceStatId = FightStatId.Balance;
    public float balanceBaseValue = 100f;
    public float balanceMaxBonus  = 100f;
    [Tooltip("X = blended fall/precision input (0..1, see balanceFallWeight). Y = fraction of " +
             "balanceMaxBonus granted.")]
    public AnimationCurve balanceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("FallCount at or above this is treated as the worst case (fall component -> 0). " +
             "0 falls -> fall component = 1.")]
    public int fallCountForZeroBalance = 5;
    [Tooltip("How much the fall component weighs against overall timing precision in Balance's " +
             "input. 1 = falls only, 0 = precision only. Deliberately NOT OverallNormalizedScore " +
             "(that already bakes in fall penalties/the no-fall bonus — reusing it here would " +
             "double-count falls).")]
    [Range(0f, 1f)] public float balanceFallWeight = 0.7f;

    // ── Music → combat BUILD — see MusicCombatBuildConverter ─────────────────────────────────────
    // 2026-10-25: DIRECT and CUMULATIVE. Every collected musical bonus adds its own fixed points to the stats it feeds;
    // nothing is normalised, redistributed or taken from another stat. Replaces the old global budget split by
    // 65/35-style weights (its YAML may linger in the asset; nothing reads it).
    [Header("Music build — performance summary")]
    [Tooltip("0 = performance is pure collection percentage (Collected / Available). >0 blends in the " +
             "collected pickups' timing accuracy: CollectionRate x Lerp(1, TimingAccuracy, this). Summary only — the " +
             "build below counts collected pickups.")]
    [Range(0f, 1f)] public float performanceTimingInfluence = 0f;

    /// <summary>What ONE collected bonus of `source` adds to each of the four build stats (points).</summary>
    [System.Serializable]
    public class BonusContribution
    {
        public RingType source;
        [Min(0f)] public float punchPower;
        [Min(0f)] public float kickPower;
        [Min(0f)] public float resistance;
        [Min(0f)] public float agility;

        public float Get(FightStatId stat) => stat switch
        {
            FightStatId.PunchPower => punchPower,
            FightStatId.KickPower => kickPower,
            FightStatId.Resistance => resistance,
            FightStatId.Agility => agility,
            _ => 0f,
        };
    }

    [Header("Music build — direct contributions (points per collected bonus)")]
    [Tooltip("Every stat starts here with no bonus at all: the fighter is very weak but fully functional.")]
    [Min(0f)] public float baseStatPoints = 2f;
    [Tooltip("Ceiling of a stat (base included). min(cap, base + sum) — monotonic: more bonuses never lower a stat. " +
             "Matches the combat curves' last key (FightCombatBalanceConfig).")]
    [Min(1f)] public float maxStatPoints = 40f;
    [Tooltip("Specialists feed one stat; Onset feeds all four a little; Impact feeds both attacks. Not every bonus totals " +
             "the same: they appear with very different frequencies. Bonuses not listed (Peak) feed nothing.")]
    public BonusContribution[] contributions = DefaultContributions();

    // Initial values from the real offer of the 20 playable songs (60 s played window, BonusDensityReport per type):
    // Kick 11–126 (≈80), Snare 14–137 (≈65), HiHat 1–41 (≈12), Onset 3–88 (≈30); Beat and Impact 0 today (Beat only
    // comes from the synthetic beat fill, off; Impact from macro impacts, absent from the played windows). Aim: a good
    // run (~75 % collected) on an average song ≈ 20–30 in its strong stats, a poor one (~25 %) ≈ 8–12; dense songs hit
    // the cap. Rare bonuses give more per pickup.
    public static BonusContribution[] DefaultContributions() => new[]
    {
        new BonusContribution { source = RingType.Snare,  punchPower = 0.35f },
        new BonusContribution { source = RingType.Kick,   kickPower = 0.33f },
        new BonusContribution { source = RingType.HiHat,  agility = 1.5f },
        new BonusContribution { source = RingType.Beat,   resistance = 0.35f },
        new BonusContribution { source = RingType.Onset,  punchPower = 0.25f, kickPower = 0.25f, resistance = 0.25f, agility = 0.25f },
        new BonusContribution { source = RingType.Impact, punchPower = 1.5f, kickPower = 1.5f },
    };

    /// <summary>The contribution row of a bonus type (null: it feeds no stat).</summary>
    public BonusContribution Contribution(RingType source)
    {
        if (contributions != null) foreach (var c in contributions) if (c != null && c.source == source) return c;
        return null;
    }

    // ── Future Player Level scaling ──────────────────────────────────────────────────────────
    // Deliberately no scaling field/curve here yet — see LevelContext's own doc: whether Player
    // Level should scale these stats directly, or only affect difficulty/AI/unlocks/visuals
    // instead, hasn't been decided. FighterStatsBuilder already accepts a LevelContext parameter,
    // so adding real scaling later is a pure addition to this config, never a reshape of it.
}
