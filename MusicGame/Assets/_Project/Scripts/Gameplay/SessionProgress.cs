/// <summary>
/// Accumulates finished-run results across a play session (survives Restart/Continue, resets
/// only when the app/scene itself restarts — it's a plain field on GameplayManager, never
/// touched by ResetRunState). Deliberately separate from any single run's own CollectionStats/
/// GamePerformance: THIS is total-across-runs, THAT is this-run-only — never mix the two.
///
/// Uses sum-of-earned / sum-of-max (not an average of each run's normalized score) so runs of
/// different songs (different maxPossibleScore) combine correctly — a weighted average, not a
/// naive mean that would treat a short/sparse song's percentage as equal in weight to a long/
/// dense one's.
/// </summary>
public class SessionProgress
{
    public int RunsCompleted { get; private set; }
    public int TotalEarnedScore { get; private set; }
    public int TotalMaxPossibleScore { get; private set; }

    public float NormalizedScore =>
        TotalMaxPossibleScore > 0 ? UnityEngine.Mathf.Clamp01((float)TotalEarnedScore / TotalMaxPossibleScore) : 0f;

    public void AddRun(GamePerformance run)
    {
        RunsCompleted++;
        TotalEarnedScore      += run.OverallEarnedScore;
        TotalMaxPossibleScore += run.OverallMaxPossibleScore;
    }
}
