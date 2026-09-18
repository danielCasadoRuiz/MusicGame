/// <summary>
/// Snapshot of GameEndedEvent's payload, captured into GameSession the instant a run finishes —
/// the single copy Fight (and any future post-Gameplay system) reads instead of reaching back into
/// GameplayManager/GameEndedEvent directly. Deliberately mirrors GameEndedEvent's own fields rather
/// than redesigning scoring — this is a carrier for the existing CollectionStats/GamePerformance
/// data, not a new scoring model.
/// </summary>
public class RunnerResults
{
    public CollectionStats Stats;
    public int             FallCount;
    public float           NormalizedScore;
    public int             MaxPossibleScore;
    public GamePerformance Performance;
}
