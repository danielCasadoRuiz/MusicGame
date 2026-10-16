using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Pure campaign RULES over CampaignProgressData + CampaignConfigSO (no singletons, no saving — the
/// caller persists). Used by CampaignService at runtime and by the batch rule tests.
/// </summary>
public static class CampaignModel
{
    // ── Position bookkeeping ─────────────────────────────────────────────────────

    /// <summary>Re-anchors the current position on the stored level id (config reordered/resized) and
    /// creates a record slot for every configured position. Records of removed levels are kept (ignored).</summary>
    public static void Normalize(CampaignProgressData d, CampaignConfigSO c)
    {
        if (d == null || c == null || c.LevelCount == 0) return;
        d.positions ??= new List<PositionRecordData>();
        d.boss ??= new BossProgressData();
        d.currentLevelRivals ??= new List<string>();
        d.lap = Mathf.Max(1, d.lap);
        if (!IsLapFinished(d, c))
        {
            int anchored = c.IndexOf(d.levelId);
            if (anchored >= 0) d.levelIndex = anchored;
            d.levelIndex = Mathf.Clamp(d.levelIndex, 0, c.LevelCount);
            if (d.levelIndex < c.LevelCount)
            {
                d.matchNumber = Mathf.Clamp(d.matchNumber, 1, c.Level(d.levelIndex).matchCount);
                d.levelId = c.Level(d.levelIndex).id;
            }
        }
        for (int i = 0; i < c.LevelCount; i++)
            for (int m = 1; m <= c.Level(i).matchCount; m++) Position(d, c.Level(i).id, m);
    }

    public static bool IsLapFinished(CampaignProgressData d, CampaignConfigSO c) => d.levelIndex >= c.LevelCount;

    public static CampaignLevelDefinition CurrentLevel(CampaignProgressData d, CampaignConfigSO c) => c.Level(d.levelIndex);

    public static string CurrentPositionId(CampaignProgressData d, CampaignConfigSO c)
    {
        var level = CurrentLevel(d, c);
        return level != null ? CampaignConfigSO.PositionId(level.id, d.matchNumber) : null;
    }

    public static PositionRecordData Position(CampaignProgressData d, string levelId, int matchNumber)
    {
        string id = CampaignConfigSO.PositionId(levelId, matchNumber);
        var p = d.positions.Find(x => x.positionId == id);
        if (p != null) return p;
        p = new PositionRecordData { positionId = id, levelId = levelId, matchNumber = matchNumber };
        d.positions.Add(p);
        return p;
    }

    public static PositionRecordData Find(CampaignProgressData d, string positionId) =>
        string.IsNullOrEmpty(positionId) ? null : d.positions.Find(x => x.positionId == positionId);

    // ── Records (each phase improved independently) ──────────────────────────────

    public struct RecordOutcome
    {
        public bool NewRecord;          // this attempt replaced the stored best
        public bool FirstRecord;        // there was no valid best before
        public bool JustMastered;       // the position became mastered with this attempt
        public PhaseRecordData Previous;
        public PhaseRecordData Best;
        public int CombinedBestHalfStars; // -1 while a phase has no valid best
    }

    public static RecordOutcome RecordRunner(PositionRecordData p, PhaseRecordData attempt) => Record(p, attempt, runner: true);
    public static RecordOutcome RecordCombat(PositionRecordData p, PhaseRecordData attempt) => Record(p, attempt, runner: false);

    private static RecordOutcome Record(PositionRecordData p, PhaseRecordData attempt, bool runner)
    {
        bool masteredBefore = IsMastered(p);
        var best = runner ? p.bestRunner : p.bestCombat;
        var outcome = new RecordOutcome { Previous = best, FirstRecord = best == null || !best.has || !best.valid };
        if (runner) p.lastRunner = attempt; else p.lastCombat = attempt;
        if (PhaseEvaluator.Improves(attempt, best))
        {
            if (runner) p.bestRunner = attempt; else p.bestCombat = attempt;
            outcome.NewRecord = true;
        }
        outcome.Best = runner ? p.bestRunner : p.bestCombat;
        outcome.JustMastered = !masteredBefore && IsMastered(p);
        outcome.CombinedBestHalfStars = CombinedHalfStars(p);
        return outcome;
    }

    public static bool HasRunner(PositionRecordData p) => p != null && p.bestRunner != null && p.bestRunner.has;
    public static bool HasCombat(PositionRecordData p) => p != null && p.bestCombat != null && p.bestCombat.has && p.bestCombat.valid;

    /// <summary>Both phase BESTS at 5★ (they may come from different laps).</summary>
    public static bool IsMastered(PositionRecordData p) =>
        HasRunner(p) && HasCombat(p) && p.bestRunner.halfStars == StarMath.Max && p.bestCombat.halfStars == StarMath.Max;

    /// <summary>(best runner + best combat) / 2 in half stars, rounded down; -1 when a phase has no best yet.</summary>
    public static int CombinedHalfStars(PositionRecordData p) =>
        HasRunner(p) && HasCombat(p) ? StarMath.Combine(p.bestRunner.halfStars, p.bestCombat.halfStars) : -1;

    // ── Advancing ────────────────────────────────────────────────────────────────

    public struct AdvanceOutcome
    {
        public string CompletedPositionId;
        public bool LevelCompleted;
        public string CompletedLevelId;
        public bool LapCompleted;
        public bool FirstCampaignCompletion; // → reveal the final rival
    }

    /// <summary>The current match was won (runner done + combat won): mark it completed and move on.
    /// Only the CURRENT position advances, so replaying a lap never inflates anything.</summary>
    public static AdvanceOutcome CompleteCurrentMatch(CampaignProgressData d, CampaignConfigSO c)
    {
        var outcome = new AdvanceOutcome();
        if (IsLapFinished(d, c)) return outcome;
        var level = CurrentLevel(d, c);
        var p = Position(d, level.id, d.matchNumber);
        p.completed = true;
        outcome.CompletedPositionId = p.positionId;

        d.matchNumber++;
        if (d.matchNumber > level.matchCount)
        {
            outcome.LevelCompleted = true;
            outcome.CompletedLevelId = level.id;
            d.levelIndex++;
            d.matchNumber = 1;
            d.currentLevelRivals.Clear();
            if (d.levelIndex >= c.LevelCount)
            {
                d.levelId = "";
                d.lapsCompleted++;
                outcome.LapCompleted = true;
                if (!d.bossRevealed) { d.bossRevealed = true; outcome.FirstCampaignCompletion = true; }
            }
            else d.levelId = c.Level(d.levelIndex).id;
        }
        return outcome;
    }

    /// <summary>New lap from L1#1, keeping every record and card.</summary>
    public static void StartNewLap(CampaignProgressData d, CampaignConfigSO c)
    {
        d.lap = Mathf.Max(1, d.lap) + 1;
        d.levelIndex = 0;
        d.matchNumber = 1;
        d.levelId = c.LevelCount > 0 ? c.Level(0).id : "";
        d.currentLevelRivals.Clear();
    }

    // ── Summaries ────────────────────────────────────────────────────────────────

    public struct LevelSummary
    {
        public string LevelId;
        public int Index, MatchCount, CompletedThisLap, CompletedEver, Mastered;
        public int RatedRunner, RatedCombat, RatedTotal;
        /// <summary>Exact averages in HALF stars over the rated positions (NaN when none rated).</summary>
        public float AvgRunner, AvgCombat, AvgTotal;
    }

    public static LevelSummary Summarize(CampaignProgressData d, CampaignConfigSO c, int levelIndex)
    {
        var level = c.Level(levelIndex);
        var s = new LevelSummary { LevelId = level.id, Index = levelIndex, MatchCount = level.matchCount };
        s.CompletedThisLap = IsLapFinished(d, c) || d.levelIndex > levelIndex ? level.matchCount
                            : d.levelIndex == levelIndex ? d.matchNumber - 1 : 0;
        float r = 0, cb = 0, t = 0;
        for (int m = 1; m <= level.matchCount; m++)
        {
            var p = Find(d, CampaignConfigSO.PositionId(level.id, m));
            if (p == null) continue;
            if (p.completed) s.CompletedEver++;
            if (IsMastered(p)) s.Mastered++;
            if (HasRunner(p)) { s.RatedRunner++; r += p.bestRunner.halfStars; }
            if (HasCombat(p)) { s.RatedCombat++; cb += p.bestCombat.halfStars; }
            int comb = CombinedHalfStars(p);
            if (comb >= 0) { s.RatedTotal++; t += (p.bestRunner.halfStars + p.bestCombat.halfStars) * 0.5f; }
        }
        s.AvgRunner = s.RatedRunner > 0 ? r / s.RatedRunner : float.NaN;
        s.AvgCombat = s.RatedCombat > 0 ? cb / s.RatedCombat : float.NaN;
        s.AvgTotal  = s.RatedTotal > 0 ? t / s.RatedTotal : float.NaN;
        return s;
    }

    public static (int mastered, int total) MasteryCount(CampaignProgressData d, CampaignConfigSO c)
    {
        int mastered = 0, total = 0;
        for (int i = 0; i < c.LevelCount; i++)
            for (int m = 1; m <= c.Level(i).matchCount; m++)
            {
                total++;
                if (IsMastered(Find(d, CampaignConfigSO.PositionId(c.Level(i).id, m)))) mastered++;
            }
        return (mastered, total);
    }

    // ── Final rival ──────────────────────────────────────────────────────────────

    public struct BossRequirements
    {
        public bool LapCompleted;
        public int Mastered, TotalPositions, Cards, CardsRequired;
        public bool Met => LapCompleted && Mastered >= TotalPositions && TotalPositions > 0 && Cards >= CardsRequired;
    }

    public static BossRequirements EvaluateBoss(CampaignProgressData d, CampaignConfigSO c, int ownedRequiredCards, int requiredCards)
    {
        var (mastered, total) = MasteryCount(d, c);
        return new BossRequirements
        {
            LapCompleted = d.lapsCompleted > 0, Mastered = mastered, TotalPositions = total,
            Cards = ownedRequiredCards, CardsRequired = requiredCards,
        };
    }

    /// <summary>Normal cards REQUIRED for the final rival: every roster composer × every rival version the
    /// campaign actually fights (CampaignLevelDefinition.rivalLevel) that the composer has authored.</summary>
    public static List<(string id, int level)> RequiredCards(IEnumerable<OpponentDefinition> roster, CampaignConfigSO c)
    {
        var versions = new HashSet<int>();
        for (int i = 0; i < c.LevelCount; i++) versions.Add(c.Level(i).rivalLevel);
        var list = new List<(string, int)>();
        foreach (var o in roster)
        {
            if (o == null || string.IsNullOrEmpty(o.id) || o.levels == null) continue;
            foreach (var l in o.levels)
                if (l != null && versions.Contains(l.level) && !list.Contains((o.id, l.level))) list.Add((o.id, l.level));
        }
        return list;
    }

    // ── Final rival stages (pure) ────────────────────────────────────────────────

    public static BossStageData BossStage(BossProgressData b, FinalBossConfigSO cfg, int index)
    {
        if (cfg == null || index < 0 || index >= cfg.StageCount) return null;
        string id = cfg.Stage(index).stageId;
        var s = b.stages.Find(x => x.stageId == id);
        if (s == null) { s = new BossStageData { stageId = id }; b.stages.Add(s); }
        return s;
    }

    public struct BossStageOutcome { public bool NewRecord, NextAvailable, SequenceCompleted, FirstFinalDefeat; public PhaseRecordData Best; }

    /// <summary>Records a stage attempt (best kept per stage across challenges) and moves the sequence:
    /// a win (any stars) opens the next stage; the last one completes the challenge.</summary>
    public static BossStageOutcome RecordBossStage(BossProgressData b, FinalBossConfigSO cfg, int index, PhaseRecordData attempt, bool won)
    {
        var o = new BossStageOutcome();
        var stage = BossStage(b, cfg, index);
        if (stage == null) return o;
        stage.lastCombat = attempt;
        if (PhaseEvaluator.Improves(attempt, stage.bestCombat)) { stage.bestCombat = attempt; o.NewRecord = true; }
        o.Best = stage.bestCombat;
        if (!won) return o;
        stage.defeated = true;
        if (index + 1 < cfg.StageCount)
        {
            b.currentStage = index + 1;
            b.challengeInProgress = true;
            o.NextAvailable = true;
        }
        else
        {
            b.currentStage = 0;
            b.challengeInProgress = false;
            b.challengesCompleted++;
            o.SequenceCompleted = true;
            if (!b.finalDefeated) { b.finalDefeated = true; b.finaleShown = false; o.FirstFinalDefeat = true; }
        }
        return o;
    }

    /// <summary>"Més enllà de la música": final rival beaten AND a 5★ best on every stage.</summary>
    public static bool SealEarned(BossProgressData b, FinalBossConfigSO cfg)
    {
        if (cfg == null || !b.finalDefeated || cfg.StageCount == 0) return false;
        for (int i = 0; i < cfg.StageCount; i++)
        {
            var s = BossStage(b, cfg, i);
            if (s == null || !s.defeated || s.bestCombat == null || !s.bestCombat.has || !s.bestCombat.valid ||
                s.bestCombat.halfStars < StarMath.Max) return false;
        }
        return true;
    }

    // ── Legacy migration (pre-v5 saves) ──────────────────────────────────────────

    /// <summary>
    /// Deterministic placement of pre-campaign history: the old save only knows HOW MANY matches were won
    /// (in order), not which campaign position they would have been. They fill positions L1#1, L1#2, … in
    /// order, marked completed + legacyUnrated (no stars: there were no metrics), and the campaign resumes
    /// right after them. If they cover the whole campaign, lap 1 counts as completed (the final rival is
    /// revealed). Nothing is invented: no stars, no records — replaying those positions scores them.
    /// </summary>
    public static void MigrateLegacy(CampaignProgressData d, CampaignConfigSO c, int legacyWins)
    {
        Normalize(d, c);
        int placed = 0;
        for (int i = 0; i < c.LevelCount && placed < legacyWins; i++)
            for (int m = 1; m <= c.Level(i).matchCount && placed < legacyWins; m++)
            {
                var p = Position(d, c.Level(i).id, m);
                p.completed = true;
                p.legacyUnrated = true;
                placed++;
                d.levelIndex = i;
                d.matchNumber = m;
                d.levelId = c.Level(i).id;
                CompleteCurrentMatch(d, c);
            }
        d.migratedLegacyPositions = placed;
        if (IsLapFinished(d, c) && d.bossRevealed) d.bossRevealPopupShown = false; // show the reveal once
    }
}

/// <summary>Rival choice for a campaign match: the level's rival VERSION fixed, the composer chosen to
/// (1) avoid repeating a composer inside the same level of the same lap, (2) prefer composers whose
/// card for that version is still missing (so the collection always progresses), (3) respect the
/// recent-opponent cooldown when there is a choice. Uniform among what is left.</summary>
public static class CampaignRivalPicker
{
    public static OpponentDefinition Pick(IReadOnlyList<OpponentDefinition> roster, int version,
                                          Func<string, int, bool> isDefeated, ICollection<string> usedInLevel,
                                          IReadOnlyList<string> recent, int cooldown, bool prioritizeMissing,
                                          bool noRepeatWithinLevel, System.Random rng)
    {
        var candidates = roster.Where(o => o != null && !string.IsNullOrEmpty(o.id) && HasVersion(o, version)).ToList();
        if (candidates.Count == 0) candidates = roster.Where(o => o != null).ToList(); // no exact version anywhere: any composer
        if (candidates.Count == 0) return null;

        var pool = candidates;
        if (noRepeatWithinLevel && usedInLevel != null)
        {
            var fresh = pool.Where(o => !usedInLevel.Contains(o.id)).ToList();
            if (fresh.Count > 0) pool = fresh;
        }
        if (prioritizeMissing && isDefeated != null)
        {
            var missing = pool.Where(o => !isDefeated(o.id, version)).ToList();
            if (missing.Count > 0) pool = missing;
        }
        if (recent != null && cooldown > 0)
        {
            var recentSet = new HashSet<string>(recent.Skip(Math.Max(0, recent.Count - cooldown)));
            var rested = pool.Where(o => !recentSet.Contains(o.id)).ToList();
            if (rested.Count > 0) pool = rested;
        }
        return pool[(rng ?? new System.Random()).Next(pool.Count)];
    }

    public static bool HasVersion(OpponentDefinition o, int version)
    {
        if (o.levels == null) return false;
        foreach (var l in o.levels) if (l != null && l.level == version) return true;
        return false;
    }

    public static OpponentLevelConfig Version(OpponentDefinition o, int version)
    {
        if (o == null) return null;
        if (o.levels != null) foreach (var l in o.levels) if (l != null && l.level == version) return l;
        return o.GetConfigForTier(version);
    }
}

/// <summary>Song variety cycles (independent of laps): a song WON in the current cycle leaves the pool;
/// when every playable song has been consumed, a new cycle starts with all of them.</summary>
public static class SongCycleModel
{
    /// <summary>Catalog songs still available in the cycle; renews the cycle first if none is left.</summary>
    public static HashSet<string> Consumed(SongCycleData cycle, IReadOnlyList<string> catalog, out bool renewed)
    {
        renewed = false;
        cycle.consumedSongIds ??= new List<string>();
        if (catalog != null && catalog.Count > 0 && catalog.All(id => cycle.consumedSongIds.Contains(id)))
        {
            cycle.cycle = Mathf.Max(1, cycle.cycle) + 1;
            cycle.consumedSongIds.Clear();
            renewed = true;
        }
        return new HashSet<string>(cycle.consumedSongIds);
    }

    public static bool Consume(SongCycleData cycle, string songId)
    {
        if (string.IsNullOrEmpty(songId)) return false;
        cycle.consumedSongIds ??= new List<string>();
        if (cycle.consumedSongIds.Contains(songId)) return false;
        cycle.consumedSongIds.Add(songId);
        return true;
    }
}
