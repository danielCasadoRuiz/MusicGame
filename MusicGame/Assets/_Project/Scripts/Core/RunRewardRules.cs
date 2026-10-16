/// <summary>
/// What a Runner run's resources are FOR, and the rules that keep repeating a match from duplicating them
/// (pure — exercised by CampaignRulesTests):
///
///   run score     → the run's stars (PhaseEvaluator: score ÷ the song's reachable maximum) = the RACE half of
///                   the campaign match record. Never a currency, never accumulated.
///   LIFE pickup   → +1 persistent extra life (PlayerProgressService.ExtraLives), whose one use is Fight Again
///                   after a lost fight. Granted once per match: a replay / restart / resumed run of the SAME
///                   assignment only grants the pickups beyond the best earlier attempt (GrantsLife).
///   SPECIAL pickup→ +1 SPECIAL in this run's fight wallet (RunSession.Wallet) — spent on the fight's special
///                   move; the wallet starts empty on every new run, so a replay cannot stack them.
///   Triple/Quad   → x3 / x4 technique charges in the same fight wallet.
///
/// Campaign records / advance / cards / completed songs have their own once-only rules (CampaignModel:
/// only the CURRENT position advances, a loss never records; cards and song history are sets).
/// </summary>
public static class RunRewardRules
{
    /// <summary>True when the `collectedThisRun`-th LIFE pickup of this run is a NEW life for the match:
    /// earlier attempts of the same match already granted `alreadyGranted`.</summary>
    public static bool GrantsLife(int collectedThisRun, int alreadyGranted) =>
        collectedThisRun > System.Math.Max(0, alreadyGranted);

    /// <summary>Specials collected and specials available for the fight are the same number at the end of a
    /// run (the wallet is only spent in the fight) — shown once in that case.</summary>
    public static bool SameSpecialCount(int collected, int available) => collected == available;
}
