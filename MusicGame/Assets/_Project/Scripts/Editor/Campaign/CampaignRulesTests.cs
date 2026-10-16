#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Rule tests for scoring, stars, records, campaign advance / laps, music cycles, rival coverage, final
/// rival and save migration — pure logic on in-memory data (never the real save file).
/// Tools > MusicGame > Campaign > Run Rule Tests, or batch:
///   -executeMethod CampaignRulesTests.RunFromCommandLine   (report: Logs/CampaignRulesTests.txt)
/// </summary>
public static class CampaignRulesTests
{
    private static StringBuilder s_report;
    private static int s_fails, s_passes;

    [MenuItem("Tools/MusicGame/Campaign/Run Rule Tests")]
    public static void RunMenu() => Debug.Log(Run());

    public static void RunFromCommandLine()
    {
        string report = Run();
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/CampaignRulesTests.txt", report, Encoding.UTF8);
        EditorApplication.Exit(s_fails == 0 ? 0 : 1);
    }

    public static string Run()
    {
        s_report = new StringBuilder("[CampaignRulesTests]\n");
        s_fails = s_passes = 0;
        var eval = ScriptableObject.CreateInstance<EvaluationConfigSO>();
        Stars(); Runner(eval); Combat(eval); Records(eval); Campaign(); MusicCycle(); RivalCoverage(); FinalRival(); Migration(); VersionDialogues(); Arenas();
        s_report.AppendLine($"RESULT {(s_fails == 0 ? "PASS" : "FAIL")} — {s_passes} passed, {s_fails} failed");
        return s_report.ToString();
    }

    private static void Check(bool ok, string what)
    {
        if (ok) s_passes++; else s_fails++;
        s_report.AppendLine((ok ? "  PASS  " : "  FAIL  ") + what);
    }

    private static CampaignConfigSO Config(params int[] matches)
    {
        var c = ScriptableObject.CreateInstance<CampaignConfigSO>();
        c.levels = matches.Select((m, i) => new CampaignLevelDefinition { id = "T" + (i + 1), matchCount = m, rivalLevel = i + 1 }).ToArray();
        return c;
    }

    private static PhaseRecordData Rec(int half, float norm, bool valid = true, int lap = 1, string song = "", string opp = "", int ver = 0) =>
        new() { has = true, halfStars = half, normalized = norm, valid = valid, lap = lap, songId = song, opponentId = opp, opponentLevel = ver };

    // ── Stars ────────────────────────────────────────────────────────────────

    private static void Stars()
    {
        s_report.AppendLine("-- stars");
        Check(StarMath.HalfStarsFor(0.95f, 0.95f) == 10 && StarMath.HalfStarsFor(1f, 0.95f) == 10, "normalized ≥ threshold → 5★");
        Check(StarMath.HalfStarsFor(0.9499f, 0.95f) == 9, "just under the threshold → 4.5★ (never 5 by rounding)");
        Check(StarMath.HalfStarsFor(0f, 0.95f) == 0 && StarMath.HalfStarsFor(float.NaN, 0.95f) == 0, "0 / NaN → 0★");
        var all = Enumerable.Range(0, 1001).Select(i => StarMath.HalfStarsFor(i / 1000f, 0.95f)).ToList();
        Check(all.All(h => h >= 0 && h <= 10) && all.Zip(all.Skip(1), (a, b) => b >= a).All(x => x), "0..5★ in half steps, monotonic");
        Check(StarMath.Combine(6, 10) == 8, "race 3 + fight 5 → 4");
        Check(StarMath.Combine(8, 10) == 9, "race 4 + fight 5 → 4.5");
        Check(StarMath.Combine(10, 10) == 10 && StarMath.Combine(9, 10) == 9, "5+5 → 5; 4.5+5 → 4.5 (no rounding up to mastery)");
        Check(StarMath.Combine(7, 10) == 8, "3.5 + 5 → 4 (floored to a half step)");
    }

    // ── Runner ───────────────────────────────────────────────────────────────

    private static void Runner(EvaluationConfigSO eval)
    {
        s_report.AppendLine("-- runner");
        var perfect = PhaseEvaluator.EvaluateRunner(1f, 100, 100, 1f, 0, eval);
        Check(perfect.Score == 5000 && perfect.MaxScore == 5000 && perfect.HalfStars == 10, "perfect run = 5000/5000, 5★");
        var over = PhaseEvaluator.EvaluateRunner(1.4f, 100, 100, 1f, 0, eval);
        Check(over.Score == 5000, "score capped at the maximum (bonus overflow clamped)");
        var none = PhaseEvaluator.EvaluateRunner(0.8f, 0, 0, 0f, 0, eval);
        Check(none.Score == 0 && none.HalfStars == 0, "no playable pickups → 0 (never a free 5★)");
        var good = PhaseEvaluator.EvaluateRunner(0.82f, 100, 90, 0.8f, 1, eval);
        Check(good.HalfStars == 8 && good.Score == 4100, $"0.82 → {good.Score} pts, {StarMath.Label(good.HalfStars)}★");
    }

    // ── Combat ───────────────────────────────────────────────────────────────

    private static void Combat(EvaluationConfigSO eval)
    {
        s_report.AppendLine("-- combat");
        var m = new CombatAttemptMetrics();
        m.Reset(100f, 2); m.Won = true; m.AttacksStarted = 40; m.HitsLanded = 10;
        var perfect = PhaseEvaluator.EvaluateCombat(m, eval);
        Check(perfect.Perfect && perfect.HalfStars == 10 && perfect.Score == 5000, "won with no damage (any accuracy) = PERFECT 5★");

        m.Reset(100f, 2); m.Won = true; m.AttacksStarted = 10; m.HitsLanded = 10; m.DamageTaken = 1f;
        var nearly = PhaseEvaluator.EvaluateCombat(m, eval);
        Check(!nearly.Perfect && nearly.HalfStars == 9, $"won, 1 damage, 100% accuracy → {StarMath.Label(nearly.HalfStars)}★ (5★ reserved for perfect)");

        // healing never erases damage: damage is summed as taken
        m.Reset(100f, 2); m.Won = true; m.AttacksStarted = 10; m.HitsLanded = 5;
        m.DamageTaken += 30f; m.DamageTaken += 20f; // two hits; a later heal does not subtract
        var healed = PhaseEvaluator.EvaluateCombat(m, eval);
        Check(Mathf.Abs(healed.Metrics.First(x => x.key == "HealthKept").value - 0.75f) < 1e-4f, "50 damage of a 200 budget → 75% kept, regardless of healing");

        m.Reset(100f, 2); m.Won = false; m.AttacksStarted = 10; m.HitsLanded = 10;
        var lost = PhaseEvaluator.EvaluateCombat(m, eval);
        Check(!lost.Valid && lost.HalfStars < 10, $"defeat: informative {StarMath.Label(lost.HalfStars)}★, not valid for records");

        m.Reset(100f, 2); m.Won = true; m.DamageTaken = 40f; m.AttacksStarted = 1000; m.HitsLanded = 5000;
        var spam = PhaseEvaluator.EvaluateCombat(m, eval);
        Check(spam.Normalized <= eval.combatNonPerfectCap + 1e-6f, "multi-hit / spam cannot exceed the non-perfect cap");

        m.Reset(100f, 2); m.Won = true; m.DamageTaken = 40f; m.AttacksStarted = 20; m.HitsLanded = 10; m.RoundsPlayed = 3;
        var a = PhaseEvaluator.EvaluateCombat(m, eval).Normalized;
        m.AttacksStarted = 200; m.HitsLanded = 100; m.RoundsPlayed = 3;
        Check(Mathf.Abs(PhaseEvaluator.EvaluateCombat(m, eval).Normalized - a) < 1e-6f, "prolonging the fight (10× actions, same ratio) does not raise the score");

        var attempt = new CombatAttemptMetrics();
        attempt.Reset(100f, 2); attempt.DamageTaken = 80f; attempt.AttacksStarted = 9; attempt.RoundsPlayed = 2;
        attempt.Reset(100f, 2); // Fight Again / new match
        Check(attempt.DamageTaken == 0f && attempt.AttacksStarted == 0 && attempt.RoundsPlayed == 0 && !attempt.Won, "metrics reset between attempts");
    }

    // ── Records ──────────────────────────────────────────────────────────────

    private static void Records(EvaluationConfigSO eval)
    {
        s_report.AppendLine("-- records");
        var p = new PositionRecordData { positionId = "T1#1", levelId = "T1", matchNumber = 1 };
        CampaignModel.RecordRunner(p, Rec(8, 0.80f, lap: 1, song: "song_A"));
        CampaignModel.RecordCombat(p, Rec(6, 0.60f, lap: 1, opp: "mozart", ver: 1));
        var worseRunner = CampaignModel.RecordRunner(p, Rec(6, 0.62f, lap: 2, song: "song_B"));
        var betterCombat = CampaignModel.RecordCombat(p, Rec(10, 1f, lap: 2, opp: "bach", ver: 1));
        Check(!worseRunner.NewRecord && p.bestRunner.halfStars == 8 && p.bestRunner.songId == "song_A" && p.lastRunner.songId == "song_B",
              "worse race keeps the record (song_A, lap 1); latest attempt stored separately");
        Check(betterCombat.NewRecord && p.bestCombat.opponentId == "bach" && p.bestCombat.lap == 2, "better fight replaces the record with ITS rival + lap");
        Check(CampaignModel.CombinedHalfStars(p) == 9, "independent bests: race 4 + fight 5 → 4.5");
        var tie = CampaignModel.RecordCombat(p, Rec(10, 1f, lap: 3, opp: "haydn", ver: 1));
        Check(!tie.NewRecord && p.bestCombat.opponentId == "bach", "tie keeps the stored record (stable)");
        var loss = CampaignModel.RecordCombat(p, Rec(10, 1f, valid: false, lap: 4, opp: "wagner", ver: 1));
        Check(!loss.NewRecord && p.bestCombat.opponentId == "bach", "a lost fight never becomes a record");
        Check(!CampaignModel.IsMastered(p), "race 4★ → not mastered");
        var mastered = CampaignModel.RecordRunner(p, Rec(10, 0.97f, lap: 5, song: "song_C"));
        Check(mastered.JustMastered && CampaignModel.IsMastered(p), "both bests at 5★ (different laps) → mastered");
    }

    // ── Campaign / laps ──────────────────────────────────────────────────────

    private static void Campaign()
    {
        s_report.AppendLine("-- campaign");
        var c = Config(2, 3, 1); // variable counts
        var d = new CampaignProgressData();
        CampaignModel.Normalize(d, c);
        Check(d.positions.Count == 6 && CampaignModel.CurrentPositionId(d, c) == "T1#1", "6 positions for levels of 2/3/1 matches, start at T1#1");
        var ids = new List<string>();
        CampaignModel.AdvanceOutcome last = default;
        for (int i = 0; i < 6; i++) { ids.Add(CampaignModel.CurrentPositionId(d, c)); last = CampaignModel.CompleteCurrentMatch(d, c); }
        Check(string.Join(",", ids) == "T1#1,T1#2,T2#1,T2#2,T2#3,T3#1", $"advance order {string.Join(",", ids)}");
        Check(last.LapCompleted && last.FirstCampaignCompletion && d.bossRevealed && CampaignModel.IsLapFinished(d, c) && d.lapsCompleted == 1,
              "last win completes the lap → final rival revealed once");
        var noop = CampaignModel.CompleteCurrentMatch(d, c);
        Check(!noop.LapCompleted && d.lapsCompleted == 1, "nothing advances on a finished lap (no inflation)");
        CampaignModel.Position(d, "T2", 2).bestRunner = Rec(10, 1f);
        CampaignModel.StartNewLap(d, c);
        Check(d.lap == 2 && CampaignModel.CurrentPositionId(d, c) == "T1#1" && CampaignModel.Find(d, "T2#2").bestRunner.halfStars == 10 && d.positions.All(p => p.completed),
              "new lap: lap 2 at T1#1, records and completion kept");
        var again = default(CampaignModel.AdvanceOutcome);
        for (int i = 0; i < 6; i++) again = CampaignModel.CompleteCurrentMatch(d, c);
        Check(again.LapCompleted && !again.FirstCampaignCompletion && d.lapsCompleted == 2, "second lap completes without a second reveal");

        // Level summary
        var s = CampaignModel.Summarize(d, c, 1);
        Check(s.MatchCount == 3 && s.CompletedThisLap == 3 && s.RatedRunner == 1 && float.IsNaN(s.AvgCombat) && s.Mastered == 0,
              "level summary: base of the average = rated positions; pending fights shown as NaN; mastered counted per position");

        // config resized: re-anchored on the stored level id
        var d2 = new CampaignProgressData();
        CampaignModel.Normalize(d2, c);
        CampaignModel.CompleteCurrentMatch(d2, c); CampaignModel.CompleteCurrentMatch(d2, c); // → T2#1
        var reordered = ScriptableObject.CreateInstance<CampaignConfigSO>();
        reordered.levels = new[] { c.levels[1], c.levels[0], c.levels[2] };
        CampaignModel.Normalize(d2, reordered);
        Check(CampaignModel.CurrentPositionId(d2, reordered) == "T2#1", "reordered config: position follows the stable level id");
    }

    // ── Music cycles ─────────────────────────────────────────────────────────

    private static void MusicCycle()
    {
        s_report.AppendLine("-- music cycle");
        var catalog = new List<string> { "a", "b", "c" };
        var cycle = new SongCycleData();
        SongCycleModel.Consume(cycle, "a"); SongCycleModel.Consume(cycle, "b");
        var consumed = SongCycleModel.Consumed(cycle, catalog, out bool renewed);
        Check(!renewed && consumed.SetEquals(new[] { "a", "b" }) && cycle.cycle == 1, "inside a cycle the won songs stay out");
        SongCycleModel.Consume(cycle, "c");
        consumed = SongCycleModel.Consumed(cycle, catalog, out renewed);
        Check(renewed && consumed.Count == 0 && cycle.cycle == 2, "all songs used → cycle 2 with every song available again");
        Check(!SongCycleModel.Consume(cycle, "") && SongCycleModel.Consume(cycle, "b") && !SongCycleModel.Consume(cycle, "b"), "consume is idempotent");
    }

    // ── Rival coverage (collection completable) ──────────────────────────────

    private static void RivalCoverage()
    {
        s_report.AppendLine("-- rival coverage");
        var roster = new List<OpponentDefinition>();
        for (int i = 0; i < 12; i++)
        {
            var o = ScriptableObject.CreateInstance<OpponentDefinition>();
            o.id = "c" + i;
            o.levels = Enumerable.Range(1, 4).Select(l => new OpponentLevelConfig { level = l }).ToArray();
            roster.Add(o);
        }
        var c = Config(5, 5, 6, 6);
        var defeated = new HashSet<(string, int)>();
        var rng = new System.Random(7);
        int laps = 0; bool repeatsInLevel = false;
        while (defeated.Count < 48 && laps < 10)
        {
            laps++;
            for (int li = 0; li < c.LevelCount; li++)
            {
                var used = new List<string>();
                for (int m = 0; m < c.Level(li).matchCount; m++)
                {
                    var pick = CampaignRivalPicker.Pick(roster, c.Level(li).rivalLevel, (id, v) => defeated.Contains((id, v)), used, null, 0, true, true, rng);
                    if (used.Contains(pick.id)) repeatsInLevel = true;
                    used.Add(pick.id);
                    defeated.Add((pick.id, c.Level(li).rivalLevel)); // the player wins every match
                }
            }
        }
        Check(defeated.Count == 48 && laps == 3, $"missing-first selection completes the 48 cards in {laps} laps (worst level: 12 composers / 5 matches = 3)");
        Check(!repeatsInLevel, "no composer repeated inside a level of a lap");
        Check(CampaignModel.RequiredCards(roster, c).Count == 48, "required normal cards = composers × versions the campaign fights");
        var c2 = Config(5, 5);
        Check(CampaignModel.RequiredCards(roster, c2).Count == 24, "a campaign fighting only versions 1–2 requires only those 24 cards");
    }

    // ── Final rival ──────────────────────────────────────────────────────────

    private static void FinalRival()
    {
        s_report.AppendLine("-- final rival");
        var cfg = ScriptableObject.CreateInstance<FinalBossConfigSO>();
        var b = new BossProgressData();
        var o1 = CampaignModel.RecordBossStage(b, cfg, 0, Rec(6, 0.6f), true);
        var o2 = CampaignModel.RecordBossStage(b, cfg, 1, Rec(4, 0.4f, valid: false), false);
        Check(o1.NextAvailable && b.currentStage == 1 && !o2.NextAvailable && b.currentStage == 1, "stage 1 won (3★ is enough) → stage 2; a loss stays on stage 2");
        CampaignModel.RecordBossStage(b, cfg, 1, Rec(10, 1f), true);
        var o3 = CampaignModel.RecordBossStage(b, cfg, 2, Rec(8, 0.8f), true);
        Check(o3.SequenceCompleted && o3.FirstFinalDefeat && b.finalDefeated && b.currentStage == 0 && !b.challengeInProgress, "three wins in order → final rival defeated");
        Check(!CampaignModel.SealEarned(b, cfg), "defeated without 5★ everywhere → no seal (final superat ≠ domini)");
        CampaignModel.RecordBossStage(b, cfg, 0, Rec(10, 1f, lap: 2), true);
        CampaignModel.RecordBossStage(b, cfg, 1, Rec(6, 0.6f, lap: 2), true);
        CampaignModel.RecordBossStage(b, cfg, 2, Rec(10, 1f, lap: 3), true);
        Check(CampaignModel.BossStage(b, cfg, 1).bestCombat.halfStars == 10, "a worse later stage result keeps the stage record");
        Check(CampaignModel.SealEarned(b, cfg), "5★ bests on all three stages (from different challenges) → seal");

        // unlock requirements
        var c = Config(1, 1);
        var d = new CampaignProgressData();
        CampaignModel.Normalize(d, c);
        var req = CampaignModel.EvaluateBoss(d, c, 48, 48);
        Check(!req.Met, "no lap completed → locked");
        CampaignModel.CompleteCurrentMatch(d, c); CampaignModel.CompleteCurrentMatch(d, c);
        foreach (var p in d.positions) { p.bestRunner = Rec(10, 1f); p.bestCombat = Rec(10, 1f); }
        Check(!CampaignModel.EvaluateBoss(d, c, 47, 48).Met, "every position mastered but one card missing → locked");
        Check(CampaignModel.EvaluateBoss(d, c, 48, 48).Met, "lap done + all mastered + all cards → unlocked");
        d.positions[0].bestCombat = Rec(9, 0.94f);
        Check(!CampaignModel.EvaluateBoss(d, c, 48, 48).Met, "one fight at 4.5★ → locked");
    }

    // ── Version-specific dialogue lines (final rival) ────────────────────────

    private static void VersionDialogues()
    {
        s_report.AppendLine("-- version dialogues");
        var t = ScriptableObject.CreateInstance<UnityEngine.Localization.Tables.StringTable>();
        t.SharedData = ScriptableObject.CreateInstance<UnityEngine.Localization.Tables.SharedTableData>();
        for (int v = 1; v <= 3; v++) for (int i = 1; i <= 10; i++) t.AddEntry(ComposerDialogueKeys.Key("boss", ComposerDialogueCategory.Intro, v, i), $"v{v} {i}");
        t.AddEntry(ComposerDialogueKeys.Key("boss", ComposerDialogueCategory.Intro, 1), "generic");
        var v2 = ComposerDialogueService.GetVariantKeys(t, "boss", ComposerDialogueCategory.Intro, 2);
        Check(v2.Count == 10 && v2.All(k => k.Contains(".L2.")), "version 2 uses only its 10 own lines");
        var v4 = ComposerDialogueService.GetVariantKeys(t, "boss", ComposerDialogueCategory.Intro, 4);
        Check(v4.Count == 1 && v4[0].EndsWith("Intro.001"), "a version without own lines falls back to the generic ones");
        Check(ComposerDialogueKeys.TryParse("Composer.boss.Dialogue.Victory.L3.007", out var id, out var cat, out int idx, out int ver) &&
              id == "boss" && cat == ComposerDialogueCategory.Victory && idx == 7 && ver == 3, "key Composer.boss.Dialogue.Victory.L3.007 parses (version 3, index 7)");
        Check(ComposerDialogueKeys.TryParse("Composer.bach.Dialogue.Intro.001", out _, out _, out _, out int gv) && gv == 0, "normal composer keys stay generic");
    }

    // ── Arena selection ──────────────────────────────────────────────────────

    private static void Arenas()
    {
        s_report.AppendLine("-- arenas");
        var o = ScriptableObject.CreateInstance<OpponentDefinition>();
        o.id = "arena_test";
        o.arenas = new List<UnityEngine.AddressableAssets.AssetReferenceGameObject>();
        for (int i = 0; i < 3; i++) o.arenas.Add(new UnityEngine.AddressableAssets.AssetReferenceGameObject(System.Guid.NewGuid().ToString("N")));
        var seen = new HashSet<string>(); string last = null; bool repeat = false;
        for (int i = 0; i < 300; i++)
        {
            var pick = ArenaSelector.Pick(o);
            if (pick.AssetGUID == last) repeat = true;
            last = pick.AssetGUID; seen.Add(last);
        }
        Check(!repeat && seen.Count == 3, "3 arenas: random, all used, never the same twice in a row");
        var one = ScriptableObject.CreateInstance<OpponentDefinition>();
        one.id = "arena_one";
        one.arenas = new List<UnityEngine.AddressableAssets.AssetReferenceGameObject> { o.arenas[0], null, new UnityEngine.AddressableAssets.AssetReferenceGameObject("") };
        Check(ArenaSelector.Pick(one) == o.arenas[0] && ArenaSelector.Pick(one) == o.arenas[0], "single valid arena (null / empty entries ignored) is reused");
        var none = ScriptableObject.CreateInstance<OpponentDefinition>();
        Check(ArenaSelector.Pick(none) == null && ArenaSelector.Pick(null) == null, "no arenas → null (default arena / placeholder floor)");
    }

    // ── Save migration ───────────────────────────────────────────────────────

    private static void Migration()
    {
        s_report.AppendLine("-- migration");
        const string v4 = "{\"version\":4,\"xp\":1234,\"playerLevel\":5,\"extraLives\":3,\"tierProgress\":{\"completedSongs\":7}," +
                          "\"defeatedOpponents\":[{\"opponentId\":\"mozart\",\"levels\":[1,2]}],\"fightsWon\":7," +
                          "\"completedSongIds\":[\"song_001\",\"song_002\",\"song_003\",\"song_004\",\"song_005\",\"song_006\",\"song_007\"]," +
                          "\"seedSongId\":\"song_007\",\"songRoute\":{\"songIds\":[],\"signature\":\"x\"}," +
                          "\"pendingRun\":{\"songId\":\"song_008\",\"opponentId\":\"bach\",\"opponentLevel\":2,\"opponentTier\":2}}";
        var data = JsonUtility.FromJson<PlayerProgressData>(v4);
        PlayerProgressStore.Migrate(data);
        Check(data.xp == 1234 && data.extraLives == 3 && data.fightsWon == 7 && data.defeatedOpponents[0].levels.SequenceEqual(new[] { 1, 2 }),
              "XP, lives, wins and cards kept");
        Check(data.songCycle.cycle == 1 && data.songCycle.consumedSongIds.Count == 7, "won songs become the consumed part of music cycle 1");
        Check(data.pendingRun.IsSet && data.pendingRun.songId == "song_008" && data.pendingRun.bossStage == -1 && !data.pendingRun.IsBoss,
              "pending assignment kept (normal match, not a boss stage)");
        Check(!data.campaign.initialized, "campaign placement deferred to PlayerProgressService (needs the config)");
        var c = Config(5, 5, 6, 6);
        CampaignModel.MigrateLegacy(data.campaign, c, 7);
        Check(data.campaign.migratedLegacyPositions == 7 && CampaignModel.CurrentPositionId(data.campaign, c) == "T2#3",
              "7 old wins → T1#1..T2#2 completed, resume at T2#3");
        var unrated = data.campaign.positions.Where(p => p.completed).ToList();
        Check(unrated.Count == 7 && unrated.All(p => p.legacyUnrated && !p.bestRunner.has && !p.bestCombat.has), "migrated positions: completed, rating pending (no stars invented)");
        var full = new CampaignProgressData();
        CampaignModel.MigrateLegacy(full, c, 40);
        Check(full.lapsCompleted == 1 && full.bossRevealed && !full.bossRevealPopupShown && CampaignModel.IsLapFinished(full, c),
              "an old save with ≥ a whole campaign of wins: lap 1 completed, reveal shown once");
        var fresh = JsonUtility.FromJson<PlayerProgressData>(JsonUtility.ToJson(new PlayerProgressData()));
        PlayerProgressStore.Migrate(fresh);
        Check(fresh.pendingRun.bossStage == -1 && fresh.songCycle.cycle == 1 && fresh.version == PlayerProgressData.CurrentVersion, "fresh v5 profile round-trips");
    }
}
#endif
