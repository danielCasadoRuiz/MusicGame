using System.Collections.Generic;
using UnityEngine;

/// <summary>Result of the runner phase just played (for the Runner end screen and the match result).</summary>
public class RunnerPhaseOutcome
{
    public PhaseEvaluation Evaluation;
    public string PositionId, SongId;
    public bool Recorded;                         // false: side run (local file / no campaign position)
    public CampaignModel.RecordOutcome Record;
    /// <summary>The run's own gameplay numbers (shown next to its stars): score / reachable maximum, falls.</summary>
    public int GameScore, GameMaxScore, Falls;
    public float GameNormalized;
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

    // ── Labels (shared by the main menu, the result panels and the next-song card) ──

    /// <summary>"LEVEL 2" (or the level's own name key).</summary>
    public static string LevelName(int index)
    {
        var level = Config != null ? Config.Level(index) : null;
        if (level == null) return "";
        return string.IsNullOrEmpty(level.nameKey) ? Loc.Get("Campaign.Level", (index + 1).ToString()) : Loc.Get(level.nameKey);
    }

    /// <summary>"LEVEL 2 · match 3 of 5" for a position; "" when the level is unknown.</summary>
    public static string PositionLabel(string levelId, int matchNumber)
    {
        int index = Config != null ? Config.IndexOf(levelId) : -1;
        if (index < 0) return "";
        return Loc.Get("Campaign.PositionOf", LevelName(index), matchNumber.ToString(), Config.Level(index).matchCount.ToString());
    }

    /// <summary>The position the NEXT campaign match will be played at ("" when the lap is finished / no campaign).</summary>
    public static string CurrentPositionLabel =>
        Available && !LapFinished ? PositionLabel(CurrentLevel.id, Data.matchNumber) : "";

    /// <summary>Starts the next lap ONCE (only from a finished lap — a second call, a reopened game or a repeated button
    /// never increments again): lap + 1 from L1#1, a new song cycle (every song available again), no assigned song —
    /// the player chooses the first one (FirstSongPending). Records, stars, cards and history are kept. Saved at once.</summary>
    public static bool StartNewLap()
    {
        if (!Available) return false;
        if (!LapFinished) { Debug.Log("[Campaign] StartNewLap ignored: the current lap is not finished."); return false; }
        CampaignModel.StartNewLap(Data, Config);
        if (P.SongCycle != null) SongCycleModel.StartForLap(P.SongCycle, Data.lap);
        Data.firstSongPending = true;
        P.ClearPendingRun(save: false);
        P.Save();
        Debug.Log($"[Campaign] Lap {Data.lap} started at {CampaignModel.CurrentPositionId(Data, Config)} — first song to choose (records and cards kept).");
        EventBus.Publish(new CampaignChangedEvent { Reason = "lap" });
        return true;
    }

    /// <summary>The new lap waits for its first song to be chosen in Song Selection.</summary>
    public static bool FirstSongPending => Available && Data.firstSongPending && !LapFinished;

    /// <summary>A song was confirmed in Song Selection: the lap is now resumed through its pending run as usual.</summary>
    public static void MarkFirstSongChosen()
    {
        if (!Available || !Data.firstSongPending) return;
        Data.firstSongPending = false;
        P.Save();
    }

    /// <summary>The finished lap's prompt (next lap / later) has not been shown yet — shown once per lap.</summary>
    public static bool LapPromptPending => LapFinished && Data.lapPromptShownForLap != Data.lap;

    public static void MarkLapPromptShown()
    {
        if (!Available || Data.lapPromptShownForLap == Data.lap) return;
        Data.lapPromptShownForLap = Data.lap;
        P.Save();
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
        RunnerDifficultyOverride.Clear();
        if (!Available || run == null || run.IsBoss) return;
        int index = Config.IndexOf(run.levelId);
        if (index < 0) return;
        RunnerDifficultyOverride.Current = Config.Level(index).runnerDifficulty;
        RunnerDifficultyOverride.Value = Config.RunnerDifficultyAt(run.levelId, run.matchNumber, run.lap);
        RunnerDifficultyOverride.Position = run.PositionId;
        // The lap is part of the layout identity only when it actually changes the difficulty.
        RunnerDifficultyOverride.KeyLap = Config.lapDifficultyStep > 0f ? run.lap : 0;
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
        var outcome = new RunnerPhaseOutcome
        {
            Evaluation = eval, SongId = songId,
            GameScore = results?.Stats != null ? results.Stats.Score : 0,
            GameMaxScore = results != null ? results.MaxPossibleScore : 0,
            GameNormalized = results != null ? results.NormalizedScore : 0f,
            Falls = results != null ? results.FallCount : 0,
        };

        // Played history (Song Selection's check): any catalog song whose Runner finished, won or not. Saved with the
        // runner record below, or on its own when the run belongs to no campaign position.
        bool firstPlay = P != null && P.MarkSongPlayed(songId, save: false);

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
        else if (firstPlay) P.Save();
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

/// <summary>Campaign runner difficulty of the run being played (null = free play: the config's own value).</summary>
public static class RunnerDifficultyOverride
{
    /// <summary>Legacy Normal/Hard label of the level.</summary>
    public static RunnerDifficulty? Current;
    /// <summary>Continuous difficulty 0..1 of this campaign position (CampaignConfigSO.RunnerDifficultyAt).</summary>
    public static float? Value;
    /// <summary>Campaign position ("L2#3"); null/empty = free play.</summary>
    public static string Position;
    /// <summary>Lap number for the layout key, 0 when laps do not change the difficulty.</summary>
    public static int KeyLap;

    public static void Clear() { Current = null; Value = null; Position = null; KeyLap = 0; }
}
