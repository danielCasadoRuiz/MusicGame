using UnityEngine;

/// <summary>
/// Balances the whole Runner→Fight pipeline entirely from the Inspector — no numbers hardcoded in
/// RunnerFightResourceBuilder/FighterStatsBuilder. Same pattern as MusicRunnerScoringConfig's own
/// AnimationCurve-driven balancing (timingQualityCurve, performanceRatingCurve, fallPenaltyCurve).
/// </summary>
public enum BuildStatFallback
{
    /// <summary>The run's overall musical performance (default — the player is judged on what existed).</summary>
    OverallPerformance,
    /// <summary>FightStatsConfig.neutralInput.</summary>
    Neutral,
}

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

    // ── Music → combat BUILD (fixed budget) — see MusicCombatBuildConverter ──────────────────
    [System.Serializable]
    public class SourceWeight
    {
        public RingType source;
        [Min(0f)] public float weight = 1f;
    }

    [System.Serializable]
    public class BuildStatMapping
    {
        public FightStatId statId;
        [Tooltip("Musical sources and their weights. Weights of sources the song doesn't contain are " +
                 "redistributed over the ones it does (never counted as 0).")]
        public SourceWeight[] sources;
    }

    [Header("Music build — musical performance normalization")]
    [Tooltip("0 = performance is pure collection percentage (Collected / Available). >0 blends in the " +
             "collected pickups' timing accuracy: CollectionRate x Lerp(1, TimingAccuracy, this).")]
    [Range(0f, 1f)] public float performanceTimingInfluence = 0f;

    [Header("Music build — total combat power (depends ONLY on how well the player played)")]
    [Min(0f)] public float maxCombatBudget = 100f;
    [Tooltip("X = overall musical performance (0..1, mean over the types that existed). Y = fraction of " +
             "maxCombatBudget earned.")]
    public AnimationCurve performanceToBudget = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Music build — distribution (depends on WHAT was collected)")]
    [Tooltip("0 = the budget is split only by each stat's source performance; 1 = also fully by how " +
             "present each stat's sources are in the song (a snare-heavy song leans towards punches). " +
             "Never changes the total — only the split.")]
    [Range(0f, 1f)] public float compositionInfluence = 0.5f;
    [Tooltip("Only when NONE of a stat's configured sources exist in the song: the 'quality' that stat " +
             "competes with for its SHARE of the budget. OverallPerformance (default) = the run's overall " +
             "musical performance; Neutral = neutralInput. Its emphasis is 0 (the song has none of its " +
             "sources), so with compositionInfluence c its raw weight is quality x (1 - c). It only ever " +
             "changes the split — never the total, which is always exactly the budget (see " +
             "MusicCombatBuildConverter). Never NaN; 0 only if the chosen quality is itself 0.")]
    public BuildStatFallback missingSourceFallback = BuildStatFallback.OverallPerformance;

    public BuildStatMapping[] buildStats = new BuildStatMapping[]
    {
        new BuildStatMapping { statId = FightStatId.PunchPower, sources = new[] {
            new SourceWeight { source = RingType.Snare, weight = 0.65f }, new SourceWeight { source = RingType.Onset,  weight = 0.35f } } },
        new BuildStatMapping { statId = FightStatId.KickPower, sources = new[] {
            new SourceWeight { source = RingType.Kick,  weight = 0.65f }, new SourceWeight { source = RingType.Beat,   weight = 0.35f } } },
        new BuildStatMapping { statId = FightStatId.Agility, sources = new[] {
            new SourceWeight { source = RingType.HiHat, weight = 0.65f }, new SourceWeight { source = RingType.Onset,  weight = 0.35f } } },
        new BuildStatMapping { statId = FightStatId.Resistance, sources = new[] {
            new SourceWeight { source = RingType.Beat,  weight = 0.55f }, new SourceWeight { source = RingType.Impact, weight = 0.45f } } },
        new BuildStatMapping { statId = FightStatId.ImpactPower, sources = new[] {
            new SourceWeight { source = RingType.Impact, weight = 0.65f }, new SourceWeight { source = RingType.Kick,  weight = 0.35f } } },
    };

    // ── Future Player Level scaling ──────────────────────────────────────────────────────────
    // Deliberately no scaling field/curve here yet — see LevelContext's own doc: whether Player
    // Level should scale these stats directly, or only affect difficulty/AI/unlocks/visuals
    // instead, hasn't been decided. FighterStatsBuilder already accepts a LevelContext parameter,
    // so adding real scaling later is a pure addition to this config, never a reshape of it.
}
