using System.Collections.Generic;

/// <summary>
/// Persistent CAMPAIGN state (PlayerProgressData.campaign, save v5). Ids and numbers only.
///
/// Vocabulary (kept apart on purpose):
///   level (campaign level)  CampaignConfigSO.levels[i] — stable id ("L1"…), ordered
///   position                1-based match slot inside a level; "L1#3" = level L1, match 3 (the stable
///                           identity of a scorable match — its song/rival are only THIS lap's assignment)
///   lap                     campaign pass (1, 2, …); a new lap restarts at L1#1 keeping every record
///   rival version           OpponentLevelConfig.level (the collectable card)
///   player level            XP statistic (PlayerProgressData.playerLevel) — unrelated to all of the above
///   song cycle              SongCycleData — independent of laps
/// </summary>
[System.Serializable]
public class CampaignProgressData
{
    /// <summary>Current lap (1-based).</summary>
    public int lap = 1;
    /// <summary>Current position: index into CampaignConfigSO.levels (0-based) + 1-based match number.
    /// levelIndex == level count means "this lap is finished".</summary>
    public int levelIndex;
    public int matchNumber = 1;
    /// <summary>Stable id of the level at levelIndex when it was last written (survives reordering:
    /// CampaignModel re-anchors on it if the config changed).</summary>
    public string levelId = "";

    /// <summary>Composer ids already assigned in the CURRENT level of the current lap (no repeats inside a level).</summary>
    public List<string> currentLevelRivals = new();
    /// <summary>False until the campaign was first set up for this save (new profile or migrated pre-v5 save).</summary>
    public bool initialized;

    public int lapsCompleted;
    /// <summary>Per-position best results (independent per phase) + latest attempt.</summary>
    public List<PositionRecordData> positions = new();

    // ── Final rival (Ésser de la música) ────────────────────────────────────
    public bool bossRevealed;             // first campaign completed: silhouette + requirements shown
    public bool bossRevealPopupShown;     // the one-time reveal popup has been displayed
    public bool bossUnlocked;             // every position mastered + every normal card owned
    public bool bossUnlockNoticeShown;
    public BossProgressData boss = new();

    /// <summary>Save migration bookkeeping: how many positions were filled from pre-v5 history.</summary>
    public int migratedLegacyPositions;
}

/// <summary>One scorable position ("L2#4"): best runner + best combat records, each improved
/// independently and each remembering where it came from (song / rival + version + lap).</summary>
[System.Serializable]
public class PositionRecordData
{
    public string positionId = "";
    public string levelId = "";
    public int matchNumber;
    /// <summary>Completed (runner done + combat WON) at least once, in any lap.</summary>
    public bool completed;
    /// <summary>Completed before scoring existed (save migration): counts as completed, no stars yet.</summary>
    public bool legacyUnrated;
    public PhaseRecordData bestRunner = new();
    public PhaseRecordData bestCombat = new();
    public PhaseRecordData lastRunner = new();
    public PhaseRecordData lastCombat = new();
}

/// <summary>A scored phase result (runner or combat). `has` = false → nothing recorded.</summary>
[System.Serializable]
public class PhaseRecordData
{
    public bool has;
    public int score;
    public int maxScore;
    public float normalized;
    /// <summary>0..10 = 0..5 stars in half steps (integers: no rounding errors).</summary>
    public int halfStars;
    /// <summary>Combat only: won. Runner: always true once finished.</summary>
    public bool valid;
    public bool perfect;
    public int lap;
    public string songId = "";
    public string opponentId = "";
    public int opponentLevel;
    public long utcTicks;
    public List<MetricData> metrics = new();
}

[System.Serializable]
public class MetricData
{
    public string key;
    public float value;
    public float max;
}

/// <summary>The three-stage final challenge.</summary>
[System.Serializable]
public class BossProgressData
{
    /// <summary>0-based stage the CURRENT challenge is at (0..2). Persisted: a closed app resumes here.</summary>
    public int currentStage;
    public bool challengeInProgress;
    public List<BossStageData> stages = new();
    /// <summary>All three versions beaten at least once ("Rival final superat").</summary>
    public bool finalDefeated;
    public bool finaleShown;
    /// <summary>"Més enllà de la música": 5★ best record on all three stages (non-development content).</summary>
    public bool sealGranted;
    public bool sealCelebrationShown;
    public int challengesCompleted;
}

[System.Serializable]
public class BossStageData
{
    public string stageId = "";
    public bool defeated;
    public PhaseRecordData bestCombat = new();
    public PhaseRecordData lastCombat = new();
}

/// <summary>Music variety, INDEPENDENT of laps: songs WON in the current cycle are out of the pool
/// until every playable song has been won once in it; then a new cycle starts with all of them.</summary>
[System.Serializable]
public class SongCycleData
{
    public int cycle = 1;
    public List<string> consumedSongIds = new();
}
