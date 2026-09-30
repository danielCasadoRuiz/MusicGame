/// <summary>
/// The finalized result of one Runner run, captured into GameSession the instant a run finishes —
/// the single copy Results/Fight (and any future post-Gameplay system) read instead of reaching
/// back into GameplayManager/GameEndedEvent directly. Carries GameEndedEvent's raw facts plus the
/// derived musical performance, combat resources and fighter build (see RunnerResultsBuilder).
/// </summary>
public class RunnerResults
{
    public CollectionStats Stats;
    public int             FallCount;
    public float           NormalizedScore;
    public int             MaxPossibleScore;
    public GamePerformance Performance;

    // ── Finalized by GameSession (RunnerResultsBuilder) — everything Fight/Results need, so
    //    neither ever has to reach into Runner scene objects ───────────────────────────────────

    /// <summary>The Runner's normal score (fall penalties and the no-fall bonus included) — a
    /// snapshot taken at run end (Stats is GameplayManager's live object, reset on a restart).</summary>
    public int TotalScore;

    /// <summary>Per musical type available/collected/normalized performance (N/A types included,
    /// never counted as 0) + overall performance over the types that existed.</summary>
    public MusicalPerformanceSummary Musical;

    public float OverallPerformance => Musical != null ? Musical.OverallPerformance : 0f;

    /// <summary>The run's resources — Lives, TripleCombos, QuadCombos, Specials. The ONE copy
    /// (nothing else stores them). Never part of musical performance or the combat build.</summary>
    public RunnerResourceCounts Resources;

    /// <summary>The Runner's canonical combat-build output — PunchPower, KickPower, Agility,
    /// Resistance, ImpactPower, summing to CombatBuild.Budget. Built from musical performance ONLY
    /// (never from Life/Special/combos). The ONE copy; later systems read it, never recompute it.
    /// Null when no FightStatsConfig is configured.</summary>
    public MusicCombatBuild CombatBuild;
}

/// <summary>GameEndedEvent -> finalized RunnerResults. Pure; used by GameSession (and tests).</summary>
public static class RunnerResultsBuilder
{
    public static RunnerResults Build(in GameEndedEvent e, FightStatsConfig config)
    {
        var results = new RunnerResults
        {
            Stats            = e.Stats,
            FallCount        = e.FallCount,
            NormalizedScore  = e.NormalizedScore,
            MaxPossibleScore = e.MaxPossibleScore,
            Performance      = e.Performance,
            Resources        = e.Resources,
            TotalScore       = e.Stats != null ? e.Stats.Score : 0,
        };
        results.Musical     = MusicalPerformanceBuilder.Build(e.Performance, config != null ? config.performanceTimingInfluence : 0f);
        results.CombatBuild = config != null ? MusicCombatBuildConverter.Build(results.Musical, config) : null;
        return results;
    }
}
