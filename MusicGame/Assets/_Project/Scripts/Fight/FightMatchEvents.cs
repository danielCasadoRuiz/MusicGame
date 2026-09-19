/// <summary>What TRIGGERED a round to end — see FightMatchController.EndRound's own doc. Orthogonal
/// to RoundResolution below: a TimeOut can resolve Decisive, DrawResolvedByPoints, or TrueDraw; a KO
/// is always Decisive.</summary>
public enum RoundEndReason
{
    KO,
    TimeOut,
}

/// <summary>
/// HOW a round's winner was actually determined — see FightMatchController.EndRound's own doc on the
/// tie-break policy.
/// </summary>
public enum RoundResolution
{
    /// <summary>A real winner existed outright — KO, or TimeOut with different health percentages.</summary>
    Decisive,
    /// <summary>TimeOut with EXACTLY equal health percentages, resolved using
    /// FightMatchController.MatchPointDifferential accumulated from PREVIOUS rounds (never this
    /// round's own — see that property's own doc).</summary>
    DrawResolvedByPoints,
    /// <summary>The exceptional case: equal health percentages AND zero accumulated differential —
    /// nobody wins, the same round number repeats (see FightMatchController's own doc on why this
    /// is a fallback, not sudden death).</summary>
    TrueDraw,
}

/// <summary>Fired by FightMatchController the instant a round's timer actually starts (Fighting
/// begins) — RoundIntroController's own "ROUND {n}" text already shows this same number a moment
/// earlier via SetRound; this event is for anything else (debug, future systems) that needs to know
/// a round is now live.</summary>
public struct RoundStartedEvent
{
    public int RoundNumber;
}

/// <summary>
/// Fired by FightMatchController exactly once per round, the instant it decides the round is over
/// (KO or the timer reaching 0) — see FightMatchController.EndRound's own doc. Winner is null ONLY
/// for a TrueDraw (see RoundResolution's own doc) — a DrawResolvedByPoints round always carries a
/// real Winner, same as a Decisive one.
/// </summary>
public struct RoundEndedEvent
{
    public int RoundNumber;
    public RoundEndReason Reason;
    public RoundResolution Resolution;
    public FighterSide? Winner;
    public float PlayerHealthPercent;
    public float OpponentHealthPercent;
    /// <summary>This round's OWN playerHealthPercent - opponentHealthPercent — see
    /// FightMatchController.MatchPointDifferential's own doc.</summary>
    public float RoundDifferential;
    /// <summary>FightMatchController.MatchPointDifferential's value AFTER this round (unchanged for
    /// a TrueDraw, whose own RoundDifferential is always 0 anyway).</summary>
    public float AccumulatedPointDifferential;
}

/// <summary>Fired by FightMatchController exactly once, the instant a side reaches
/// FightFlowConfig.roundsToWin — see FightMatchController.NotifyRoundEndDisplayComplete's own doc.
/// Carries just enough already-available data (task's own explicit "no inventis un sistema enorme
/// d'estadístiques" scope note) for MatchResultController to show a small match summary and, on a
/// win, the Level Up line — nothing here is recomputed or re-derived downstream.</summary>
public struct MatchEndedEvent
{
    public FighterSide Winner;
    public int PlayerRoundsWon;
    public int OpponentRoundsWon;
    /// <summary>How the FINAL round ended — KO or TimeOut.</summary>
    public RoundEndReason LastRoundReason;
    /// <summary>Final round's own health percentages (0..1) — same values RoundEndedEvent already
    /// carried for that round.</summary>
    public float PlayerHealthPercent;
    public float OpponentHealthPercent;
    public float MatchPointDifferential;
    /// <summary>GameSession.PlayerLevel immediately BEFORE this match's outcome was applied. Equal
    /// to NewPlayerLevel when Winner == Opponent (a loss never changes PlayerLevel — see
    /// GameSession.LevelUp's own doc).</summary>
    public int OldPlayerLevel;
    /// <summary>GameSession.PlayerLevel immediately AFTER — already decided by the time this event
    /// fires (see FightMatchController.NotifyRoundEndDisplayComplete's own doc on why the Level Up
    /// decision itself never waits for Continue).</summary>
    public int NewPlayerLevel;
}
