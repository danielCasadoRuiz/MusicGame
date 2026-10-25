#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.UI;

/// <summary>
/// Focused Play Mode probe of the lap transition + song use per lap, on the save present at launch (the driver puts a
/// COPY of the real save there and restores the original afterwards):
///   migration (old cycle → this lap's own won songs, history untouched) · lap finished → prompt ONCE → "Later" →
///   menu again: no prompt · CONTINUE: next lap ONCE + Song Selection (all songs available, history shown apart) ·
///   BACK → menu, still pending · CONTINUE → Song Selection again (no new lap) · first song chosen → pending run ·
///   one real race + forced fight win → SELECT SONG: that song unavailable this lap, the others available · BACK →
///   the same result · global progress kept.
/// Batch: -executeMethod LapFlowProbe.RunFromCommandLine   (exit 0 = all passed)
/// </summary>
[InitializeOnLoad]
public static class LapFlowProbe
{
    private const string KeyActive = "LapFlowProbe.Active";
    private static readonly StringBuilder s_log = new();
    private static int s_pass, s_fail, s_errors, s_step = -1;
    private static double s_stepStart, s_deadline;
    private static bool s_hooked, s_continued;

    private static CampaignProgressData D => CampaignService.Data;
    private static PlayerProgressService P => PlayerProgressService.Instance;
    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;
    private static MusicClock Clock => MusicClock.Instance;
    private static GameplayManager Manager => UnityEngine.Object.FindFirstObjectByType<GameplayManager>();

    private static int s_completedBefore, s_recordsBefore, s_cardsBefore, s_lapAfterStart, s_lapsCompletedSim;
    private static string s_chosen;

    static LapFlowProbe() { if (SessionState.GetBool(KeyActive, false)) EditorApplication.update += Tick; }

    public static void RunFromCommandLine()
    {
        SessionState.SetBool(KeyActive, true);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (12f, "boot: migration of the loaded save", null, () =>
        {
            var cyc = P.SongCycle;
            Log($"save: lap {D.lap} {CampaignService.CurrentPositionLabel}, lapsCompleted {D.lapsCompleted}, history {P.CompletedSongIds.Count}, cycle {cyc.cycle} lap {cyc.lap} used [{string.Join(",", cyc.consumedSongIds)}]");
            Check("song use bound to the current lap (from this lap's records, not the history)", cyc.lap == D.lap && cyc.consumedSongIds.Count < P.CompletedSongIds.Count,
                  $"{cyc.consumedSongIds.Count} used this lap vs {P.CompletedSongIds.Count} in the history");
            s_completedBefore = P.CompletedSongIds.Count;
            s_recordsBefore = D.positions.Count(p => p.bestRunner.has || p.bestCombat.has);
            s_cardsBefore = CardCount();
            // Simulate the lap's last win: the lap is now finished (nothing else touched).
            D.levelIndex = CampaignService.Config.LevelCount; D.levelId = ""; D.matchNumber = 1; D.lapsCompleted++;
            s_lapsCompletedSim = D.lapsCompleted;
            P.ClearPendingRun(save: false);
            P.Save();
            AppFlowController.Instance.RequestState(GameFlowState.SongSelection);
        }),
        (1.5f, "menu after the finished lap", null, () => AppFlowController.Instance.RequestState(GameFlowState.MainMenu)),
        (2f, "prompt shown once", null, () =>
        {
            var popup = Popup();
            Check("finished lap → the next-lap prompt is shown", popup != null && popup.IsOpen && D.lapPromptShownForLap == D.lap, $"open {popup?.IsOpen}, shownForLap {D.lapPromptShownForLap}");
            ClickSecondary(popup); // LATER
        }),
        (1f, "away and back", null, () => AppFlowController.Instance.RequestState(GameFlowState.SongSelection)),
        (1.5f, "menu again", null, () => AppFlowController.Instance.RequestState(GameFlowState.MainMenu)),
        (2f, "no repeated prompt", null, () =>
        {
            Check("'Later' leaves the lap pending; the prompt is NOT shown again", (Popup() == null || !Popup().IsOpen) && CampaignService.LapFinished && D.lap == s_lapAfterStartBase());
            Check("persisted: prompt already shown for this lap", SavedText().Contains($"\"lapPromptShownForLap\": {D.lap}"));
            Find<MainMenuView>().playButton.onClick.Invoke(); // CONTINUE
        }),
        (0f, "CONTINUE → next lap + Song Selection", () => Flow == GameFlowState.SongSelection, () =>
        {
            s_lapAfterStart = D.lap;
            Check("CONTINUE started the next lap ONCE, first song to choose", D.lap == s_lapAfterStartBase() + 1 && CampaignService.FirstSongPending && !CampaignService.LapFinished
                  && D.lapsCompleted == s_lapsCompletedSim && !P.PendingRun.IsSet, $"lap {D.lap}, pending {CampaignService.FirstSongPending}, run {P.PendingRun.IsSet}");
        }),
        (2f, "every song available in the new lap", null, () =>
        {
            var (avail, total, hist) = Rows();
            Check("new lap: every song selectable again; history shown apart (not blocking)", total > 0 && avail == total && hist == PlayedInCatalog(), $"{avail}/{total} selectable, {hist} played checks (history {PlayedInCatalog()})");
            Find<SongSelectionView>().backButton.onClick.Invoke(); // BACK
        }),
        (0f, "BACK → menu", () => Flow == GameFlowState.MainMenu, null),
        (2f, "menu: still pending", null, () =>
        {
            Check("BACK keeps the new lap pending (no prompt, no new lap)", (Popup() == null || !Popup().IsOpen) && D.lap == s_lapAfterStart && CampaignService.FirstSongPending);
            Find<MainMenuView>().playButton.onClick.Invoke();
        }),
        (0f, "CONTINUE → Song Selection again", () => Flow == GameFlowState.SongSelection, () =>
        {
            Check("CONTINUE reopens the first-song choice (same lap, nothing asked)", D.lap == s_lapAfterStart && CampaignService.FirstSongPending);
            string t = SavedText();
            Check("persisted once: lap + first-song pending", t.Contains($"\"lap\": {s_lapAfterStart},") && t.Contains("\"firstSongPending\": true"));
        }),
        (1.5f, "choose the first song", () => FirstRow() != null, () => FirstRow().onClick.Invoke()),
        (1f, "play it", null, () => { s_chosen = SelectedSongId(); Find<SongSelectionView>().playButton.onClick.Invoke(); }),
        (0f, "race starts", () => Clock != null && Manager != null && Manager.IsRunning && Clock.HasPlaybackStarted, () =>
        {
            Check("first song chosen: pending run of the new lap, no longer 'first song pending'", !CampaignService.FirstSongPending && P.PendingRun.songId == s_chosen && P.PendingRun.lap == s_lapAfterStart,
                  $"run {P.PendingRun.songId} lap {P.PendingRun.lap}");
            RunnerHazardSystem.Instance?.ClearGeneratedForTest();
            Clock.StartPlayback(Manager.SongPlayEnd - 1.5f, MusicTimeDiscontinuityReason.Seek);
            Manager.ResyncCursors(Clock.SongTime);
            s_deadline = EditorApplication.timeSinceStartup + 300;
        }),
        (0f, "end → Fight", () => Flow == GameFlowState.Fight || ClickContinue(), null),
        (0f, "fight", () => FightFlowController.Instance != null && FightFlowController.Instance.CurrentState == FightFlowState.Fighting, () => FightMatchController.Instance.DebugForceMatchResult(true)),
        (0f, "result", () => FightFlowController.Instance.CurrentState == FightFlowState.MatchWon, null),
        (1.5f, "SELECT SONG", null, () =>
        {
            Check("the win advanced the new lap once", CampaignService.CurrentPositionLabel.Length > 0 && D.lap == s_lapAfterStart && D.matchNumber == 2, CampaignService.CurrentPositionLabel);
            Find<MatchResultView>().selectSongButton.onClick.Invoke();
        }),
        (0f, "song selection after the win", () => Flow == GameFlowState.SongSelection, null),
        (2f, "the played song is out, the others in", null, () =>
        {
            var (avail, total, _) = Rows();
            Check("the song won in this lap is unavailable; every other song selectable", !RowAvailable(s_chosen) && avail == total - 1, $"{avail}/{total} selectable, '{s_chosen}' available {RowAvailable(s_chosen)}");
            Find<SongSelectionView>().backButton.onClick.Invoke();
        }),
        (0f, "BACK → the same result", () => Flow == GameFlowState.Fight, null),
        (2f, "global progress", null, () =>
        {
            Check("BACK returns to the match result", FightFlowController.Instance.CurrentState == FightFlowState.MatchWon && Find<MatchResultView>().root.activeInHierarchy);
            Check("global history, records and cards kept (nothing erased)", P.CompletedSongIds.Count >= s_completedBefore
                  && D.positions.Count(p => p.bestRunner.has || p.bestCombat.has) >= s_recordsBefore && CardCount() >= s_cardsBefore,
                  $"history {P.CompletedSongIds.Count} (≥{s_completedBefore}), records ≥{s_recordsBefore}, cards {CardCount()} (≥{s_cardsBefore})");
        }),
    };

    private static int s_lapBase = -1;
    private static int s_lapAfterStartBase() { if (s_lapBase < 0) s_lapBase = D.lap; return s_lapBase; }

    private static int CardCount()
    {
        var f = typeof(PlayerProgressService).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic);
        var data = f?.GetValue(P) as PlayerProgressData;
        return data?.defeatedOpponents?.Count ?? -1;
    }

    private static string SavedText() => File.Exists(PlayerProgressStore.FilePath) ? File.ReadAllText(PlayerProgressStore.FilePath) : "";

    private static CampaignPopup Popup() => UnityEngine.Object.FindObjectsByType<CampaignPopup>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

    private static void ClickSecondary(CampaignPopup popup)
    {
        var b = typeof(CampaignPopup).GetField("_secondary", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(popup) as Button;
        b?.onClick.Invoke();
    }

    private static SongSelectionController Sel => UnityEngine.Object.FindFirstObjectByType<SongSelectionController>();
    private static T Field<T>(object o, string n) => (T)o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);

    private static (int avail, int total, int history) Rows()
    {
        var buttons = Field<List<Button>>(Sel, "_rowButtons");
        var rows = Field<List<SongRowView>>(Sel, "_rows");
        int hist = 0;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Played && buttons[i].interactable) hist++;
        return (buttons.Count(b => b.interactable), buttons.Count, hist);
    }

    private static int PlayedInCatalog() =>
        Field<List<IResourceLocation>>(Sel, "_entries").Count(l => P.IsSongPlayed(SongProgression.IdOf(l)));

    private static bool RowAvailable(string songId)
    {
        var entries = Field<List<IResourceLocation>>(Sel, "_entries");
        var buttons = Field<List<Button>>(Sel, "_rowButtons");
        for (int i = 0; i < entries.Count; i++) if (SongProgression.IdOf(entries[i]) == songId) return buttons[i].interactable;
        return false;
    }

    private static Button FirstRow() => Field<List<Button>>(Sel, "_rowButtons").FirstOrDefault(b => b.interactable);

    private static string SelectedSongId()
    {
        int idx = Field<int>(Sel, "_selectedCatalogIndex");
        var entries = Field<List<IResourceLocation>>(Sel, "_entries");
        return idx >= 0 && idx < entries.Count ? SongProgression.IdOf(entries[idx]) : null;
    }

    private static bool ClickContinue()
    {
        if (s_continued || Manager == null || Manager.IsRunning) return false;
        var end = Find<EndScreenView>();
        if (end == null || !end.continueButton.gameObject.activeInHierarchy) return false;
        s_continued = true;
        end.continueButton.onClick.Invoke();
        return false;
    }

    private static T Find<T>() where T : Component =>
        UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

    private static void Tick()
    {
        if (!SessionState.GetBool(KeyActive, false)) { EditorApplication.update -= Tick; return; }
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        try
        {
            if (!s_hooked)
            {
                s_hooked = true;
                Application.logMessageReceived += (msg, _, type) => { if (type == LogType.Exception || type == LogType.Error) { s_errors++; s_log.AppendLine($"  [{type}] {msg}"); } };
            }
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_deadline = now + 300; s_lapAfterStartBase(); }
            if (now > s_deadline) { Log($"TIMEOUT at step {s_step} ({(s_step < Steps.Length ? Steps[s_step].name : "")}) flow={Flow}"); Finish(2); return; }
            if (s_step >= Steps.Length) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = Steps[s_step];
            if (now - s_stepStart < step.wait) return;
            if (step.until != null && !step.until()) return;
            Log($"-- {step.name} (flow={Flow}, lap={D?.lap}, {CampaignService.CurrentPositionLabel})");
            step.act?.Invoke();
            s_step++; s_stepStart = now;
        }
        catch (Exception e) { Log("EXCEPTION " + e); Finish(2); }
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    private static void Log(string m) { s_log.AppendLine(m); Debug.Log("[LapProbe] " + m); }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors (exit {code})");
        File.WriteAllText("Logs/LapFlowProbe.txt", s_log.ToString());
        EditorApplication.Exit(code);
    }
}
#endif
