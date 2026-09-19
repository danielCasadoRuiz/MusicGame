/// <summary>What TRIGGERED a round to end — see FightMatchController.EndRound's own doc. Orthogonal
/// to RoundResolution below: a TimeOut can resolve Decisive or Draw; a KO is always Decisive.</summary>
public enum RoundEndReason
{
    KO,
    TimeOut,
}

/// <summary>
/// HOW a ROUND (never the match — see MatchResolution below, a deliberately separate concept) ended.
/// See FightMatchController.EndRound's own doc.
/// </summary>
public enum RoundResolution
{
    /// <summary>A real winner existed outright — KO, or TimeOut with different health percentages.</summary>
    Decisive,
    /// <summary>TimeOut with EXACTLY equal health percentages. Awards NOBODY a round win — never
    /// resolved by MatchPointDifferential or any other tie-break at the round level (see this format's
    /// own explicit rule: a Draw simply contributes 0 round wins and the match moves on). Contrast
    /// with MatchResolution.PointsTiebreak, which only ever resolves the MATCH once every round has
    /// been played — a Draw round is always just displayed as "DRAW", full stop.</summary>
    Draw,
}

/// <summary>
/// HOW the MATCH itself was ultimately decided — deliberately kept SEPARATE from RoundResolution
/// (a per-ROUND concept, see its own doc): the final round played might well have been a Draw, yet the
/// match can still conclude decisively. Never implies the final round itself was "won" by whichever
/// side the match ultimately favors — MatchResultController uses this only to phrase the match summary
/// ("PLAYER WINS ON POINTS" etc.), while the round scoreboard (PlayerRoundsWon-OpponentRoundsWon)
/// always stays honest about what actually happened round by round. See
/// FightMatchController.NotifyRoundEndDisplayComplete's own doc for exactly when each case applies.
/// </summary>
public enum MatchResolution
{
    /// <summary>One side finished with strictly more round wins than the other — whether by reaching
    /// FightFlowConfig.roundsToWin early (e.g. 2-0 after Round 2) or simply having more wins once
    /// every round in FightFlowConfig.maxRounds was played (e.g. 1-0 after Round 3, the other slot a
    /// Draw).</summary>
    DecisiveRounds,
    /// <summary>Round wins were tied after every round was played — resolved by
    /// FightMatchController.MatchPointDifferential, accumulated across ALL rounds played (Win, Loss,
    /// AND Draw rounds alike).</summary>
    PointsTiebreak,
    /// <summary>Round wins AND MatchPointDifferential were BOTH tied (within epsilon) after every
    /// round was played — resolved by a further deterministic fallback (see
    /// FightMatchController.ResolveTiedMatch's own doc). NEVER a 4th round.</summary>
    ExactTieFallback,
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
/// for a Draw (see RoundResolution's own doc).
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
    /// <summary>FightMatchController.MatchPointDifferential's value AFTER this round (every round
    /// contributes, Draws included — see EndRound's own doc).</summary>
    public float AccumulatedPointDifferential;
}

/// <summary>Fired by FightMatchController exactly once, the instant the MATCH itself is decided — see
/// FightMatchController.NotifyRoundEndDisplayComplete's own doc. Carries just enough already-available
/// data (task's own explicit "no inventis un sistema enorme d'estadístiques" scope note) for
/// MatchResultController to show a small match summary and, on a win, the Level Up line — nothing here
/// is recomputed or re-derived downstream.</summary>
public struct MatchEndedEvent
{
    public FighterSide Winner;
    public int PlayerRoundsWon;
    public int OpponentRoundsWon;
    /// <summary>HOW the match itself was decided — see MatchResolution's own doc. Deliberately
    /// separate from the round scoreboard above (PlayerRoundsWon-OpponentRoundsWon), which always
    /// stays honest about what actually happened round by round.</summary>
    public MatchResolution Resolution;
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
