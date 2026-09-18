using UnityEngine;

/// <summary>
/// RunnerResults (raw run facts) -> RunnerFightResources (normalized 0..1 combat potential).
/// Pure/stateless, same shape as PlayRangeResolver — no MonoBehaviour, no side effects, easy to
/// call from anywhere (GameSession today, a Results-screen preview later).
///
/// Ratio-based throughout (CollectionRate x TimingAccuracy, never a raw count or raw score) so a
/// song that happens to generate more or fewer events of a given type is never inherently better
/// or worse — see FightStatsConfig's own doc.
/// </summary>
public static class RunnerFightResourceBuilder
{
    public static RunnerFightResources Build(RunnerResults results, FightStatsConfig config)
    {
        var resources = new RunnerFightResources();
        if (results == null || config == null || results.Performance == null) return resources;

        foreach (var mapping in config.ringStats)
        {
            bool hadSource = results.Performance.ByType.TryGetValue(mapping.sourceType, out var tp) && tp.HasData;
            float value    = hadSource ? tp.CollectionRate * tp.TimingAccuracy : config.neutralInput;

            resources.Set(new RunnerFightResources.Entry(mapping.statId, mapping.sourceType, hadSource, value));
        }

        resources.Set(new RunnerFightResources.Entry(
            config.balanceStatId, source: null, hadSource: true, value: BuildBalanceInput(results, config)));

        return resources;
    }

    private static float BuildBalanceInput(RunnerResults results, FightStatsConfig config)
    {
        float fallComponent = 1f - Mathf.Clamp01(
            config.fallCountForZeroBalance > 0 ? (float)results.FallCount / config.fallCountForZeroBalance : 0f);

        float precisionComponent = AverageTimingAccuracy(results.Performance);

        return Mathf.Clamp01(Mathf.Lerp(precisionComponent, fallComponent, config.balanceFallWeight));
    }

    // Mean TimingAccuracy across every RingType this song's timeline actually generated —
    // deliberately NOT GamePerformance.OverallNormalizedScore, which already bakes in fall
    // penalties and the no-fall bonus; reusing it here would double-count falls, since Balance
    // already weighs FallCount directly above.
    private static float AverageTimingAccuracy(GamePerformance performance)
    {
        float sum = 0f;
        int   n   = 0;
        foreach (var kv in performance.ByType)
        {
            if (!kv.Value.HasData) continue;
            sum += kv.Value.TimingAccuracy;
            n++;
        }
        return n > 0 ? sum / n : 0f;
    }
}
