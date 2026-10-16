using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// TEST-ONLY integrated campaign probe (player build, "-probeCampaign"): runs against the save file in
/// persistentDataPath (the DRIVER backs up the real save before and restores it after) through the REAL
/// AppBootstrap modules (PlayerProgressService, GameSession), RunAssignment, CampaignService, the fight
/// presentation/result controllers, FightSceneBootstrap, the Progression and Collection screens.
/// Writes probe_&lt;phase&gt;.txt + screenshots.
/// </summary>
public class CampaignFlowProbe : MonoBehaviour
{
    private readonly StringBuilder _report = new();
    private int _fails;
    private string _out, _phase;
    private Keyboard _kb;

    public IEnumerator Run(string outDir, string phase)
    {
        _out = outDir; _phase = phase;
        yield return Until(() => PlayerProgressService.Instance != null && GameSession.Instance != null && CampaignService.Available, 20f);
        var P = PlayerProgressService.Instance;
        var data = CampaignService.Data;
        var cfg = CampaignService.Config;
        Check(P != null && CampaignService.Available, "AppBootstrap modules + campaign available");
        if (P == null) { Finish(); yield break; }

        // ── 1. Migration of the loaded save ──
        string json = File.ReadAllText(PlayerProgressStore.FilePath);
        Log($"save: loaded v{PlayerProgressStore.LoadedVersion}, {P.CompletedSongIds.Count} completed songs, {P.FightsWon} wins, cards {CountCards(P)}");
        Check(json.Contains("\"version\": 5"), "save file rewritten as v5");
        Check(data.initialized, $"campaign initialized: migrated {data.migratedLegacyPositions} position(s), now at {(CampaignService.LapFinished ? "lap finished" : CampaignModel.CurrentPositionId(data, cfg))}, lap {data.lap}");
        Check(P.SongCycle.consumedSongIds.Count == P.CompletedSongIds.Count || P.SongCycle.cycle > 1, $"music cycle {P.SongCycle.cycle}: {P.SongCycle.consumedSongIds.Count} consumed");
        int cardsBefore = CountCards(P);

        // ── catalog ids ──
        List<string> catalog = null;
        yield return SongProgression.LoadCatalog(list => catalog = SongProgression.Ids(list));
        Log($"catalog: {catalog?.Count ?? 0} playable songs");

        // ── arena host (real fight controllers) ──
        _kb = InputSystem.AddDevice<Keyboard>();
        var host = new GameObject("[CampaignProbeHost]");
        var flow = host.AddComponent<FightFlowController>();
        host.AddComponent<FightMusicController>();
        host.AddComponent<FightMatchController>();
        host.AddComponent<FightController>();
        host.AddComponent<OpponentSelectionController>();
        host.AddComponent<VersusScreenController>();
        host.AddComponent<RoundIntroController>();
        host.AddComponent<RoundEndController>();
        host.AddComponent<MatchFarewellController>();
        var result = host.AddComponent<MatchResultController>();
        var progression = host.AddComponent<ProgressionScreenController>();
        var collection = host.AddComponent<RivalCollectionController>();
        EventBus.Publish(new GameFlowStateChangedEvent { Previous = GameFlowState.MainMenu, Current = GameFlowState.Fight });
        yield return null;
        var camGo = new GameObject("ArenaCamera", typeof(Camera));
        camGo.SetActive(false);
        var bootstrap = camGo.AddComponent<FightSceneBootstrap>();
        typeof(FightSceneBootstrap).GetField("arenaCamera", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, camGo.GetComponent<Camera>());
        camGo.SetActive(true);
        yield return null; yield return null;

        // ── 2. A normal campaign match: assignment → runner → real fight (perfect win) ──
        if (CampaignService.LapFinished) { CampaignService.StartNewLap(); Log("lap was finished in the loaded save → new lap started"); }
        string pos = CampaignModel.CurrentPositionId(data, cfg);
        var level = CampaignService.CurrentLevel;
        var status = RunAssignment.ResolveContinue(catalog, out string songId);
        var run = P.PendingRun;
        Check(status == NextSongStatus.Found && run.PositionId == pos && run.lap == data.lap,
              $"assignment for {pos}: song {songId}, rival {run.opponentId} v{run.opponentLevel}");
        Check(run.opponentLevel == level.rivalLevel || !CampaignRivalPicker.HasVersion(RosterFind(run.opponentId), level.rivalLevel),
              $"rival version = level's rival version ({level.rivalLevel})");
        Check(RunnerDifficultyOverride.Current == level.runnerDifficulty, $"runner difficulty override = {level.runnerDifficulty}");

        var r1 = CampaignService.OnRunnerFinished(FakeRunner(0.97f, 120, 120), songId);
        Check(r1.Recorded && r1.PositionId == pos && CampaignModel.Find(data, pos).bestRunner.halfStars == 10, "runner phase 0.97 → 5★ recorded on the position");
        Check(File.ReadAllText(PlayerProgressStore.FilePath).Contains(songId), "runner record persisted at once");

        yield return PlayFight(flow, true);
        Check(flow.CurrentState == FightFlowState.MatchWon, "perfect win → farewell → MatchWon");
        var c1 = CampaignService.LastCombat;
        Check(c1 != null && c1.Recorded && c1.Evaluation.Perfect && c1.Evaluation.HalfStars == 10, "combat: perfect 5★ recorded");
        Check(c1.Record.JustMastered && CampaignModel.IsMastered(CampaignModel.Find(data, pos)), $"{pos} mastered");
        Check(CampaignModel.CurrentPositionId(data, cfg) != pos && P.SongCycle.consumedSongIds.Contains(songId), "position advanced, song consumed in the music cycle");
        yield return Screenshot("result_win");

        // ── 3. A lost match: no advance, no record ──
        string pos2 = CampaignModel.CurrentPositionId(data, cfg);
        RunAssignment.ResolveContinue(catalog, out string song2);
        CampaignService.OnRunnerFinished(FakeRunner(0.6f, 100, 70), song2);
        yield return PlayFight(flow, false);
        var p2 = CampaignModel.Find(data, pos2);
        Check(flow.CurrentState == FightFlowState.MatchLost && CampaignModel.CurrentPositionId(data, cfg) == pos2, "lost fight → MatchLost, same position");
        Check(p2.lastCombat.has && !p2.lastCombat.valid && !CampaignModel.HasCombat(p2), "lost attempt kept as 'latest', never as a record");
        Check(p2.bestRunner.halfStars == StarMath.HalfStarsFor(0.6f, CampaignService.Evaluation.runnerFiveStarThreshold), "runner record of that position stands after the lost fight");
        yield return Screenshot("result_loss");

        // ── 4. Rest of the lap through the same calls FightMatchController makes on a win ──
        int cycleStart = P.SongCycle.cycle, guard = 0;
        bool cycleRenewed = false;
        while (!CampaignService.LapFinished && guard++ < 60)
        {
            var st = RunAssignment.ResolveContinue(catalog, out string s);
            if (st != NextSongStatus.Found) { Fail($"no assignment ({st})"); break; }
            CampaignService.OnRunnerFinished(FakeRunner(0.9f, 100, 95), s);
            SimulateWin(P);
            if (P.SongCycle.cycle > cycleStart) cycleRenewed = true;
        }
        Check(CampaignService.LapFinished && data.bossRevealed, $"lap {data.lap} finished; final rival revealed");
        Check(CampaignService.PendingNotice() == CampaignService.Notice.CampaignCompleted, "one-time completion popup pending");
        Check(RunAssignment.ResolveContinue(catalog, out _) == NextSongStatus.LapFinished, "CONTINUE with the lap over → LapFinished (Main Menu offers a new lap)");
        Log($"music cycle now {P.SongCycle.cycle} ({(cycleRenewed ? "renewed during the lap" : "not renewed")})");

        var popup = CampaignPopup.Create("ProbePopup");
        popup.Show(Loc.Get("Campaign.CompletedTitle"), Loc.Get("Campaign.CompletedBody"), CampaignService.Boss.LevelConfig(0)?.portrait, Color.black,
                   Loc.Get("Campaign.NewLap"), null, Loc.Get("Campaign.Later"), null);
        yield return Screenshot("popup_completed");
        popup.gameObject.SetActive(false);
        CampaignService.MarkShown(CampaignService.Notice.CampaignCompleted);

        // ── 5. New lap ──
        int lapBefore = data.lap;
        var recordsBefore = data.positions.Count(p => CampaignModel.HasRunner(p) || CampaignModel.HasCombat(p));
        CampaignService.StartNewLap();
        Check(data.lap == lapBefore + 1 && CampaignModel.CurrentPositionId(data, cfg) == cfg.Level(0).id + "#1" &&
              data.positions.Count(p => CampaignModel.HasRunner(p) || CampaignModel.HasCombat(p)) == recordsBefore && CountCards(P) >= cardsBefore,
              "new lap from L1#1, every record and card kept");

        // ── 6. Progression screen ──
        progression.Expand(cfg.Level(2).id);
        progression.Open();
        yield return null; yield return null;
        yield return Screenshot("progression");
        progression.Close();

        // ── 7. Final rival unlock: every position mastered + every normal card ──
        foreach (var o in CampaignService.Roster.opponents.Where(o => o != null))
            foreach (var l in o.levels.Where(l => l != null)) if (!P.IsDefeated(o.id, l.level)) P.RecordVictory(o, l.level);
        Check(!CampaignService.CheckBossUnlock(), "all cards but positions not mastered → still locked");
        foreach (var p in data.positions) { p.bestRunner = Perfect(); p.bestCombat = Perfect(); }
        Check(CampaignService.CheckBossUnlock() && data.bossUnlocked && CampaignService.PendingNotice() == CampaignService.Notice.BossUnlocked, "all mastered + all cards → unlocked, notice pending");
        P.Save();
        CampaignService.MarkShown(CampaignService.Notice.BossUnlocked);

        // ── 8. The three final fights (real flow, boss path: no roulette) ──
        var testArena = GameObject.CreatePrimitive(PrimitiveType.Cube);
        testArena.name = "TestArena"; testArena.transform.localScale = new Vector3(8f, 0.1f, 4f); testArena.SetActive(false);
        CampaignService.Boss.stages[1].arenaPrefab = testArena; // runtime-only: stage 2 uses a custom arena
        Check(CampaignService.PrepareBossStage(CampaignService.NextBossStage), "challenge prepared at stage 1");
        flow.RequestState(FightFlowState.OpponentSelection);
        yield return Until(() => flow.CurrentState == FightFlowState.VersusIntro, 5f);
        Check(flow.CurrentState == FightFlowState.VersusIntro && GameSession.Instance.SelectedOpponent == CampaignService.Boss.opponent, "boss stage: no roulette, straight to the Versus with the final rival");
        for (int stage = 0; stage < 3; stage++)
        {
            if (stage > 0) Check(flow.CurrentState == FightFlowState.VersusIntro, $"Continue → final fight {stage + 1}");
            bool customArena = GameObject.Find("ArenaEnvironment_TestArena") != null;
            Check(customArena == (stage == 1) && (GameObject.Find("ArenaFloorPlaceholder")?.activeSelf ?? false) == (stage != 1),
                  $"stage {stage + 1}: arena = {(stage == 1 ? "custom environment" : "placeholder floor")}, no duplicates ({GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith("ArenaEnvironment_"))} env)");
            yield return PlayFight(flow, true, alreadyInVersus: true);
            var bc = CampaignService.LastCombat;
            Check(bc.Boss && bc.BossStage == stage && P.IsDefeated(CampaignService.Boss.opponent.id, CampaignService.Boss.Stage(stage).opponentLevel), $"final fight {stage + 1} won → card v{CampaignService.Boss.Stage(stage).opponentLevel}");
            if (stage == 0) yield return Screenshot("result_boss1");
            typeof(MatchResultController).GetMethod("OnContinueClicked", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(result, null);
            yield return null; yield return null;
        }
        Check(data.boss.finalDefeated && CampaignService.PendingNotice() == CampaignService.Notice.Finale, "three in order → final rival defeated, finale pending");
        Check(data.boss.sealGranted == CampaignModel.SealEarned(data.boss, CampaignService.Boss), "seal granted exactly when every stage best is 5★");
        Check(GameObject.Find("ArenaEnvironment_TestArena") == null, "custom arena released after its stage");

        collection.Open();
        yield return null; yield return null;
        yield return Screenshot("collection");
        collection.Close();

        // ── 9. Persistence ──
        var reloaded = PlayerProgressStore.Load();
        Check(reloaded != null && reloaded.version == PlayerProgressData.CurrentVersion && reloaded.campaign.lap == data.lap &&
              reloaded.campaign.boss.finalDefeated && reloaded.campaign.bossUnlocked &&
              reloaded.campaign.positions.Count(p => p.bestRunner.has) == data.positions.Count(p => p.bestRunner.has),
              "reloaded from disk: lap, records, unlock and final-rival state identical");

        EventBus.Publish(new GameFlowStateChangedEvent { Previous = GameFlowState.Fight, Current = GameFlowState.MainMenu });
        InputSystem.RemoveDevice(_kb);
        Finish();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private IEnumerator PlayFight(FightFlowController flow, bool win, bool alreadyInVersus = false)
    {
        var session = GameSession.Instance;
        if (!alreadyInVersus)
        {
            // what the roulette commits (it only reveals the assigned rival)
            session.SelectedOpponent = session.AssignedOpponent;
            session.SelectedOpponentLevelConfig = session.AssignedOpponentLevelConfig;
            session.SelectedOpponentTier = session.AssignedOpponentTier;
            flow.RequestState(FightFlowState.VersusIntro);
        }
        yield return Until(() => FightSceneBootstrap.Instance != null && FightSceneBootstrap.Instance.OpponentVisualReady, 15f);
        yield return null;
        InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.Escape)); yield return null; yield return null;
        InputSystem.QueueStateEvent(_kb, new KeyboardState()); yield return null;
        yield return Until(() => flow.CurrentState == FightFlowState.Fighting, 15f);
        FightMatchController.Instance.DebugForceMatchResult(win);
        yield return null;
        InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.Escape)); yield return null; yield return null;
        InputSystem.QueueStateEvent(_kb, new KeyboardState()); yield return null;
        yield return Until(() => flow.CurrentState == FightFlowState.MatchWon || flow.CurrentState == FightFlowState.MatchLost, 10f);
    }

    private static void SimulateWin(PlayerProgressService P)
    {
        var session = GameSession.Instance;
        var m = new CombatAttemptMetrics();
        m.Reset(100f, 2); m.Won = true; m.AttacksStarted = 10; m.HitsLanded = 8; m.DamageTaken = 20f;
        session.SelectedOpponent = session.AssignedOpponent;
        session.SelectedOpponentLevelConfig = session.AssignedOpponentLevelConfig;
        CampaignService.OnMatchDecided(true, m);
        session.RegisterCompletedSong(save: false);
        P.RecordVictory(session.SelectedOpponent, session.SelectedOpponentLevelConfig != null ? session.SelectedOpponentLevelConfig.level : 0);
        CampaignService.AfterVictorySaved();
    }

    private static RunnerResults FakeRunner(float normalized, int available, int collected)
    {
        var perf = new GamePerformance { OverallNormalizedScore = normalized };
        perf.ByType[RingType.Kick] = new TypePerformance { Type = RingType.Kick, Available = available, Collected = collected, TimingAccuracy = normalized };
        return new RunnerResults { NormalizedScore = normalized, Performance = perf, FallCount = 0 };
    }

    private static PhaseRecordData Perfect() => new() { has = true, valid = true, halfStars = 10, normalized = 1f, score = 5000, maxScore = 5000 };

    private static int CountCards(PlayerProgressService P) =>
        CampaignService.Roster.opponents.Where(o => o != null).Sum(o => o.levels.Count(l => l != null && P.IsDefeated(o.id, l.level)));

    private static OpponentDefinition RosterFind(string id) => CampaignService.Roster.opponents.FirstOrDefault(o => o != null && o.id == id);

    private IEnumerator Screenshot(string label)
    {
        if (Application.isBatchMode) yield break;
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.Combine(_out, $"campaign_{label}.png"));
        yield return null;
    }

    private static IEnumerator Until(Func<bool> c, float timeout)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!c() && Time.realtimeSinceStartup < end) yield return null;
    }

    private void Check(bool ok, string what) { if (ok) Log("PASS  " + what); else Fail(what); }
    private void Fail(string what) { _fails++; Log("FAIL  " + what); }
    private void Log(string line) { _report.AppendLine(line); Debug.Log("[CampaignProbe] " + line); }

    private void Finish()
    {
        Log(_fails == 0 ? "RESULT PASS" : $"RESULT FAIL ({_fails})");
        File.WriteAllText(Path.Combine(_out, $"probe_{_phase}.txt"), _report.ToString(), new UTF8Encoding(false));
        Application.Quit(_fails == 0 ? 0 : 1);
    }
}
