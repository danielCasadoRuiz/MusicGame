using System.Collections.Generic;
using UnityEngine;

/// <summary>Result of the runner phase just played (for the Runner end screen and the match result).</summary>
public class RunnerPhaseOutcome
{
    public PhaseEvaluation Evaluation;
    public string PositionId, SongId;
    public bool Recorded;                         // false: side run (local file / no campaign position)
    public CampaignModel.RecordOutcome Record;
}

/// <summary>Result of the combat phase just decided (match result screen).</summary>
public class CombatPhaseOutcome
{
    public PhaseEvaluation Evaluation;
    public bool Won, Boss, Recorded;
    public int BossStage = -1;
    public string PositionId;
    public CampaignModel.RecordOutcome Record;           // normal match
    public CampaignModel.AdvanceOutcome Advance;         // normal match, won
    public PhaseRecordData BossBest; public bool BossNewRecord, BossSequenceCompleted, BossNextStageAvailable;
    public RunnerPhaseOutcome RunnerOfThisMatch;          // runner of the CURRENT attempt (may be null)
}

public struct CampaignChangedEvent { public string Reason; }

/// <summary>
/// RUNTIME campaign orchestration on top of CampaignModel (rules) and PlayerProgressService (save):
///
///   assignment   RunAssignment asks PickRival / Stamp for the current position (lap, level, match)
///   runner end   OnRunnerFinished → evaluate, improve the position's runner record (saved at once)
///   match end    OnMatchDecided (inside FightMatchController's single save) → evaluate, improve the combat
///                record (wins only), advance the position, consume the song in the music cycle;
///                AfterVictorySaved → final-rival unlock / seal checks
///   laps         LapFinished / StartNewLap
///   final rival  CanChallengeBoss / PrepareBossStage / boss stage results / seal
///   notices      one-time popups (reveal, unlock, finale, seal) shown by the Main Menu at a safe point
/// </summary>
public static class CampaignService
{
    private static AppConfigSO s_app;
    private static AppConfigSO App => s_app != null ? s_app : s_app = Resources.Load<AppConfigSO>("AppConfig");

    public static CampaignConfigSO Config => App != null ? App.campaign : null;
    public static EvaluationConfigSO Evaluation => App != null && App.evaluation != null ? App.evaluation : EvaluationConfigSO.Fallback;
    public static FinalBossConfigSO Boss => Config != null ? Config.finalBoss : null;
    public static OpponentRosterSO Roster => App != null ? App.opponentRoster : null;
    private static PlayerProgressService P => PlayerProgressService.Instance;
    public static CampaignProgressData Data => P != null ? P.Campaign : null;
    public static bool Available => Config != null && Config.LevelCount > 0 && P != null;

    public static RunnerPhaseOutcome LastRunner { get; private set; }
    public static CombatPhaseOutcome LastCombat { get; private set; }

    private static readonly List<string> s_recentRivals = new();
    private static readonly System.Random s_rng = new();

    // ── Laps / position ──────────────────────────────────────────────────────────

    public static bool LapFinished => Available && CampaignModel.IsLapFinished(Data, Config);
    public static CampaignLevelDefinition CurrentLevel => Available ? CampaignModel.CurrentLevel(Data, Config) : null;

    public static void StartNewLap()
    {
        if (!Available) return;
        CampaignModel.StartNewLap(Data, Config);
        P.Save();
        Debug.Log($"[Campaign] Lap {Data.lap} started at {CampaignModel.CurrentPositionId(Data, Config)} (records and cards kept).");
        EventBus.Publish(new CampaignChangedEvent { Reason = "lap" });
    }

    // ── Assignment ───────────────────────────────────────────────────────────────

    /// <summary>The rival for the CURRENT position: the level's rival version, composer chosen by
    /// CampaignRivalPicker (no repeat in the level, missing cards first, recent cooldown).</summary>
    public static (OpponentDefinition, OpponentLevelConfig, int) PickRival(int recentCooldown)
    {
        var level = CurrentLevel;
        var roster = Roster;
        if (level == null || roster == null || roster.opponents == null) return (null, null, 0);
        var opponent = CampaignRivalPicker.Pick(roster.opponents, level.rivalLevel, (id, v) => P.IsDefeated(id, v),
                                                Data.currentLevelRivals, s_recentRivals, recentCooldown,
                                                Config.prioritizeMissingCards, Config.noRepeatWithinLevel, s_rng);
        if (opponent == null) return (null, null, 0);
        if (!Data.currentLevelRivals.Contains(opponent.id)) Data.currentLevelRivals.Add(opponent.id);
        s_recentRivals.Add(opponent.id);
        if (s_recentRivals.Count > 8) s_recentRivals.RemoveAt(0);
        var config = CampaignRivalPicker.Version(opponent, level.rivalLevel);
        return (opponent, config, config != null ? config.level : level.rivalLevel);
    }

    /// <summary>Marks a NORMAL assignment with the current lap + position.</summary>
    public static void Stamp(PendingRunData run)
    {
        if (run == null || !Available || LapFinished) return;
        run.lap = Data.lap;
        run.levelId = CurrentLevel.id;
        run.matchNumber = Data.matchNumber;
        run.bossStage = -1;
    }

    /// <summary>Runner difficulty of the level the assignment belongs to (pickup heights / patterns).</summary>
    public static void ApplyRunnerDifficulty(PendingRunData run)
    {
        RunnerDifficultyOverride.Current = null;
        if (!Available || run == null || run.IsBoss) return;
        int index = Config.IndexOf(run.levelId);
        if (index >= 0) RunnerDifficultyOverride.Current = Config.Level(index).runnerDifficulty;
    }

    // ── Runner phase ─────────────────────────────────────────────────────────────

    public static RunnerPhaseOutcome OnRunnerFinished(RunnerResults results, string songId)
    {
        int available = 0, collected = 0; float timingSum = 0f; int timed = 0;
        if (results?.Performance != null)
            foreach (var kv in results.Performance.ByType)
            {
                if (!RingTypes.IsMusical(kv.Key)) continue;
                available += kv.Value.Available; collected += kv.Value.Collected;
                if (kv.Value.Collected > 0) { timingSum += kv.Value.TimingAccuracy * kv.Value.Collected; timed += kv.Value.Collected; }
            }
        var eval = PhaseEvaluator.EvaluateRunner(results != null ? results.NormalizedScore : 0f, available, collected,
                                                 timed > 0 ? timingSum / timed : 0f, results != null ? results.FallCount : 0, Evaluation);
        var outcome = new RunnerPhaseOutcome { Evaluation = eval, SongId = songId };

        var run = P != null ? P.PendingRun : null;
        if (Available && run != null && run.IsSet && !run.IsBoss && run.songId == songId && !string.IsNullOrEmpty(run.PositionId))
        {
            var p = CampaignModel.Position(Data, run.levelId, run.matchNumber);
            outcome.PositionId = p.positionId;
            outcome.Record = CampaignModel.RecordRunner(p, eval.ToRecord(run.lap, songId, "", 0));
            outcome.Recorded = true;
            P.Save(); // the runner record stands even if the fight is lost later
            Debug.Log($"[Campaign] Runner {p.positionId}: {eval.Score}/{eval.MaxScore} ({StarMath.Label(eval.HalfStars)}★)" +
                      (outcome.Record.NewRecord ? " NEW RECORD" : $" — record kept {StarMath.Label(outcome.Record.Best.halfStars)}★"));
            EventBus.Publish(new CampaignChangedEvent { Reason = "runner" });
        }
        LastRunner = outcome;
        return outcome;
    }

    // ── Combat phase (called by FightMatchController; it saves once afterwards) ──

    public static CombatPhaseOutcome OnMatchDecided(bool won, CombatAttemptMetrics metrics)
    {
        var session = GameSession.Instance;
        var eval = PhaseEvaluator.EvaluateCombat(metrics, Evaluation);
        var outcome = new CombatPhaseOutcome { Evaluation = eval, Won = won };
        var run = P != null ? P.PendingRun : null;
        string opponentId = session?.SelectedOpponent != null ? session.SelectedOpponent.id : "";
        int version = session?.SelectedOpponentLevelConfig != null ? session.SelectedOpponentLevelConfig.level : 0;

        if (session != null && session.CurrentBossStage >= 0)
        {
            outcome.Boss = true;
            outcome.BossStage = session.CurrentBossStage;
            if (Available) RecordBossStage(outcome, opponentId, version);
        }
        else if (Available && run != null && run.IsSet && !run.IsBoss && !string.IsNullOrEmpty(run.PositionId))
        {
            var p = CampaignModel.Position(Data, run.levelId, run.matchNumber);
            outcome.PositionId = p.positionId;
            outcome.Record = CampaignModel.RecordCombat(p, eval.ToRecord(run.lap, run.songId, opponentId, version));
            outcome.Recorded = true;
            outcome.RunnerOfThisMatch = LastRunner != null && LastRunner.PositionId == p.positionId ? LastRunner : null;
            if (won)
            {
                // Only the CURRENT position advances (an assignment from a finished lap just records).
                if (run.PositionId == CampaignModel.CurrentPositionId(Data, Config))
                    outcome.Advance = CampaignModel.CompleteCurrentMatch(Data, Config);
                else p.completed = true;
                SongCycleModel.Consume(P.SongCycle, run.songId);
            }
        }
        Debug.Log($"[Campaign] Combat {(outcome.Boss ? $"boss stage {outcome.BossStage + 1}" : outcome.PositionId ?? "(side)")}: " +
                  $"{(won ? "WON" : "lost")} {eval.Score}/{eval.MaxScore} ({StarMath.Label(eval.HalfStars)}★{(eval.Perfect ? ", PERFECT" : "")})");
        LastCombat = outcome;
        return outcome;
    }

    /// <summary>After FightMatchController's single save (cards included): unlock / seal checks.</summary>
    public static void AfterVictorySaved()
    {
        if (!Available) return;
        bool changed = CheckBossUnlock() | CheckSeal();
        if (changed) P.Save();
        EventBus.Publish(new CampaignChangedEvent { Reason = "match" });
    }

    // ── Final rival ──────────────────────────────────────────────────────────────

    public static List<(string id, int level)> RequiredCards() =>
        Roster != null && Config != null ? CampaignModel.RequiredCards(Roster.opponents, Config) : new List<(string, int)>();

    public static CampaignModel.BossRequirements BossRequirements()
    {
        var required = RequiredCards();
        return CampaignModel.EvaluateBoss(Data, Config, P.CountOwned(required), required.Count);
    }

    /// <summary>Unlocks as soon as every requirement holds — even mid-lap. True if it changed.</summary>
    public static bool CheckBossUnlock()
    {
        if (!Available || Data.bossUnlocked || !BossRequirements().Met) return false;
        Data.bossUnlocked = true;
        Data.bossRevealed = true;
        Debug.Log("[Campaign] FINAL RIVAL UNLOCKED (every position mastered + every normal card).");
        return true;
    }

    public static bool CanChallengeBoss => Available && Data.bossUnlocked && Boss != null && Boss.opponent != null && Boss.StageCount > 0;

    public static BossStageData BossStage(int index) => Available ? CampaignModel.BossStage(Data.boss, Boss, index) : null;

    /// <summary>Stage the next challenge fight is: the one an interrupted challenge stopped at, else stage 1.</summary>
    public static int NextBossStage => Available && Data.boss.challengeInProgress ? Mathf.Clamp(Data.boss.currentStage, 0, Mathf.Max(0, Boss.StageCount - 1)) : 0;

    /// <summary>Sets up GameSession + the pending assignment for a final-rival stage (no runner: these are
    /// combat-only trials with their own song and arena). Persisted: a closed app resumes the challenge here.</summary>
    public static bool PrepareBossStage(int stage)
    {
        var session = GameSession.Instance;
        if (!CanChallengeBoss || session == null) return false;
        var config = Boss.LevelConfig(stage);
        if (config == null) { Debug.LogError($"[Campaign] Final rival stage {stage + 1}: version {Boss.Stage(stage)?.opponentLevel} missing on '{Boss.opponent.name}'."); return false; }
        Data.boss.currentStage = stage;
        Data.boss.challengeInProgress = true;
        var run = new PendingRunData { opponentId = Boss.opponent.id, opponentLevel = config.level, opponentTier = config.level, bossStage = stage, lap = Data.lap };
        P.SetPendingRun(run);
        session.SetBossFight(stage, Boss.opponent, config, Boss.Stage(stage).song, Boss.Stage(stage).arena);
        Debug.Log($"[Campaign] Final rival stage {stage + 1}/{Boss.StageCount} ({Boss.Stage(stage).stageId}) prepared.");
        return true;
    }

    private static void RecordBossStage(CombatPhaseOutcome outcome, string opponentId, int version)
    {
        var cfgStage = Boss != null ? Boss.Stage(outcome.BossStage) : null;
        if (cfgStage == null) return;
        var rec = outcome.Evaluation.ToRecord(Data.lap, cfgStage.song != null ? cfgStage.song.name : "", opponentId, version);
        var o = CampaignModel.RecordBossStage(Data.boss, Boss, outcome.BossStage, rec, outcome.Won);
        outcome.BossNewRecord = o.NewRecord;
        outcome.BossBest = o.Best;
        outcome.BossNextStageAvailable = o.NextAvailable;
        outcome.BossSequenceCompleted = o.SequenceCompleted;
        outcome.Recorded = true;
        if (o.SequenceCompleted) P.ClearPendingRun(save: false);
    }

    /// <summary>"Més enllà de la música": all stages beaten with a 5★ best.</summary>
    public static bool CheckSeal()
    {
        if (!Available || Data.boss.sealGranted || !CampaignModel.SealEarned(Data.boss, Boss)) return false;
        Data.boss.sealGranted = true;
        Data.boss.sealCelebrationShown = false;
        Debug.Log("[Campaign] SEAL GRANTED — Més enllà de la música.");
        return true;
    }

    // ── One-time notices (shown by the Main Menu, a safe point of the flow) ─────

    public enum Notice { None, CampaignCompleted, BossUnlocked, Finale, Seal }

    /// <summary>The next notice to show, or None. A completed lap that already showed its reveal still
    /// offers a new lap through LapFinished (Main Menu), not through a notice.</summary>
    public static Notice PendingNotice()
    {
        if (!Available) return Notice.None;
        if (Data.bossRevealed && !Data.bossRevealPopupShown) return Notice.CampaignCompleted;
        if (Data.bossUnlocked && !Data.bossUnlockNoticeShown) return Notice.BossUnlocked;
        if (Data.boss.finalDefeated && !Data.boss.finaleShown) return Notice.Finale;
        if (Data.boss.sealGranted && !Data.boss.sealCelebrationShown) return Notice.Seal;
        return Notice.None;
    }

    public static void MarkShown(Notice notice)
    {
        if (!Available) return;
        switch (notice)
        {
            case Notice.CampaignCompleted: Data.bossRevealPopupShown = true; break;
            case Notice.BossUnlocked: Data.bossUnlockNoticeShown = true; break;
            case Notice.Finale: Data.boss.finaleShown = true; break;
            case Notice.Seal: Data.boss.sealCelebrationShown = true; break;
            default: return;
        }
        P.Save();
    }
}

/// <summary>Campaign-level runner difficulty for the run being played (null = the config's own value).</summary>
public static class RunnerDifficultyOverride
{
    public static RunnerDifficulty? Current;
}
