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

    // ── Future Player Level scaling ──────────────────────────────────────────────────────────
    // Deliberately no scaling field/curve here yet — see LevelContext's own doc: whether Player
    // Level should scale these stats directly, or only affect difficulty/AI/unlocks/visuals
    // instead, hasn't been decided. FighterStatsBuilder already accepts a LevelContext parameter,
    // so adding real scaling later is a pure addition to this config, never a reshape of it.
}
