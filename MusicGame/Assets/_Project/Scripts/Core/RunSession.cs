/// <summary>
/// The CURRENT run: one Runner song and the fight it leads into (Fight Again stays in the same run).
/// A new RunSession starts every time a Runner run starts (GameStartedEvent — a new song, Replay Song,
/// or a restart), so everything here resets then — and only then (Runner → Fight keeps it).
///
/// Holds temporary state only; persistent progression (lives, XP, level, rival collection) lives in
/// PlayerProgressService. Statistics (…CollectedThisRun) are for results screens and are NEVER the
/// inventory: the SPECIAL inventory is `Wallet.Specials`, the life inventory is
/// PlayerProgressService.ExtraLives.
/// </summary>
public class RunSession
{
    /// <summary>1, 2, 3… since the app started (debug / logging).</summary>
    public int Index { get; }
    public string SongId { get; }
    /// <summary>OpponentDefinition.id once the roulette has committed a rival this run (else null).</summary>
    public string OpponentId { get; set; }
    public int OpponentLevel { get; set; }

    /// <summary>Authoritative temporary combat resources for this run: SPECIALs collected in the
    /// Runner go straight in, Triple/Quad combos are added when the run ends, and the fight spends
    /// from this SAME object (FightSceneBootstrap hands it to the player fighter).</summary>
    public FighterCombatResources Wallet { get; } = new(0, 0, 0);

    public int Score { get; set; }
    public float NormalizedScore { get; set; }
    public int XpEarned { get; set; }

    // ── Statistics only (never inventory) ────────────────────────────────────
    public int LivesCollectedThisRun { get; private set; }
    public int SpecialsCollectedThisRun { get; private set; }

    public RunSession(int index, string songId)
    {
        Index = index;
        SongId = songId;
    }

    /// <summary>A SPECIAL pickup was collected: inventory + statistic.</summary>
    public void CollectSpecial()
    {
        Wallet.Add(CombatResourceType.Special, 1);
        SpecialsCollectedThisRun++;
    }

    /// <summary>A LIFE pickup was collected: statistic only — the inventory is persistent
    /// (PlayerProgressService.AddLife, called by the same pickup).</summary>
    public void CountLifeCollected() => LivesCollectedThisRun++;
}
