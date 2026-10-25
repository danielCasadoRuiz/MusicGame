#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.UI;

/// <summary>
/// Play Mode probe of Song Selection's rows (the driver puts a COPY of the real save in place and restores the
/// original afterwards). Three songs of the catalog are given the three states in memory:
///   A never played (check off, selectable) · B played in an earlier lap (check on, selectable) · C used in THIS lap
///   (check on, disabled row).
/// Checks: no localization key / status text on screen, name | check | PREVIEW never overlap, the check catches no
/// click (raycast + no Selectable), a real pointer click on PREVIEW reaches it (B: check on → preview plays), C's
/// row / PREVIEW do nothing, gamepad/keyboard navigation never lands on C or the check, a NEW LAP re-enables C with
/// its check kept, a finished runner marks A played without blocking it, the played history survives save → load,
/// and a v6 save migrates its played history from the data it already has.
/// Screenshot: Logs/SongSelectionProbe/rows.png.  Batch: -executeMethod SongSelectionProbe.RunFromCommandLine
/// </summary>
[InitializeOnLoad]
public static class SongSelectionProbe
{
    private const string KeyActive = "SongSelectionProbe.Active";
    private const string ShotDir = "Logs/SongSelectionProbe";
    private static readonly StringBuilder s_log = new();
    private static int s_pass, s_fail, s_errors, s_step = -1;
    private static double s_stepStart, s_deadline;
    private static bool s_hooked;
    private static string s_a, s_b, s_c;
    private static bool s_aWon;

    private static PlayerProgressService P => PlayerProgressService.Instance;
    private static PlayerProgressData Data =>
        typeof(PlayerProgressService).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(P) as PlayerProgressData;
    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;
    private static SongSelectionController Sel => UnityEngine.Object.FindFirstObjectByType<SongSelectionController>();

    static SongSelectionProbe() { if (SessionState.GetBool(KeyActive, false)) EditorApplication.update += Tick; }

    public static void RunFromCommandLine()
    {
        SessionState.SetBool(KeyActive, true);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (10f, "boot: three songs, three states", () => P != null && Sel != null && Entries().Count >= 3 && CampaignService.Config != null, () =>
        {
            var ids = Entries().Select(SongProgression.IdOf).ToList();
            s_a = ids[0]; s_b = ids[1]; s_c = ids[2];
            s_aWon = P.IsSongCompleted(s_a);
            var d = Data;
            foreach (var id in new[] { s_a, s_b, s_c }) { d.playedSongIds.Remove(id); d.songCycle.consumedSongIds.Remove(id); }
            d.playedSongIds.Add(s_b);                                           // B: played before, not this lap
            d.playedSongIds.Add(s_c); d.songCycle.consumedSongIds.Add(s_c);     // C: played + used in this lap
            Log($"A {s_a} (never) · B {s_b} (earlier lap) · C {s_c} (this lap) — lap {CampaignService.Data.lap}");
            AppFlowController.Instance.RequestState(GameFlowState.SongSelection);
        }),
        (2f, "the three rows", () => Flow == GameFlowState.SongSelection, () =>
        {
            Directory.CreateDirectory(ShotDir);
            ScreenCapture.CaptureScreenshot($"{ShotDir}/rows.png");
            CheckRow("A never played: check off, selectable, PREVIEW usable", s_a, played: false, selectable: true);
            CheckRow("B played in an earlier lap: check on, still selectable, PREVIEW usable", s_b, played: true, selectable: true);
            CheckRow("C used in this lap: check on, disabled row (dimmed name, row + PREVIEW off)", s_c, played: true, selectable: false);
            var c = Row(s_c);
            Check("C's disabled style is the row's, not the check's (dimmed name, full-colour check)",
                  c.label.text.StartsWith("<alpha=#66>") && c.playedCheck.color == c.playedOnColor);

            var screen = UnityEngine.Object.FindFirstObjectByType<SongSelectionView>();
            var keyLike = new Regex(@"\b[A-Z][A-Za-z]+\.[A-Z][A-Za-z]+\b");
            var shown = screen.GetComponentsInChildren<TextMeshProUGUI>(false).Where(t => t.isActiveAndEnabled && keyLike.IsMatch(t.text)).Select(t => $"{t.name}='{t.text}'").ToList();
            Check("no localization key or status badge shown on the screen", shown.Count == 0, string.Join(", ", shown));
            Check("no row keeps the old status text", Rows().All(r => r.transform.Find("CompletedBadge") == null));

            int overlaps = 0;
            foreach (var r in Rows())
            {
                Rect name = ScreenRect(r.label.rectTransform), check = ScreenRect(r.playedCheck.rectTransform), prev = ScreenRect(r.playPauseButton.transform as RectTransform);
                if (name.xMax > check.xMin || check.xMax > prev.xMin || !Inside(check, ScreenRect(r.transform as RectTransform))) overlaps++;
            }
            Check("name | check | PREVIEW in separate columns on every row", overlaps == 0, $"{overlaps} row(s) overlapping");
            Check("the check is informative: raycast target off, no Selectable",
                  Rows().All(r => !r.playedCheck.raycastTarget && r.playedCheck.GetComponentInParent<Selectable>() == r.button && r.playedCheck.GetComponent<Selectable>() == null));

            // Real pointer path: what the EventSystem hits at the centre of each PREVIEW / check.
            Check("A pointer on PREVIEW hits PREVIEW (rows A and B)", HitsPreview(s_a) && HitsPreview(s_b), $"A→{TopHit(Center(Row(s_a).playPauseButton))}, B→{TopHit(Center(Row(s_b).playPauseButton))}");
            Check("A pointer on the check falls through to its row (never blocks)", TopHitObject(Center(Row(s_b).playedCheck)) is { } h && h.GetComponentInParent<SongRowView>() == Row(s_b) && h != Row(s_b).playedCheck.gameObject,
                  TopHit(Center(Row(s_b).playedCheck)));
            Click(Center(Row(s_b).playPauseButton));
        }),
        (4f, "PREVIEW with the check on", null, () =>
        {
            var player = SongPreviewPlayer.Instance;
            Check("clicking PREVIEW on B (check on) plays its preview and selects it", player != null && player.IsPlaying && player.PlayingId == SongCatalog.AddressForId(s_b) && SelectedId() == s_b,
                  $"playing {player?.IsPlaying} '{player?.PlayingId}', selected {SelectedId()}");
            Click(Center(Row(s_c).playPauseButton));
            Click(Center(Row(s_c).button));
        }),
        (1f, "C does nothing", null, () =>
        {
            var player = SongPreviewPlayer.Instance;
            Check("C's PREVIEW and row do nothing (B still selected and previewing)", SelectedId() == s_b && player.IsPlaying && player.PlayingId == SongCatalog.AddressForId(s_b));

            var cSel = new Selectable[] { Row(s_c).button, Row(s_c).playPauseButton };
            var live = Rows().SelectMany(r => new Selectable[] { r.button, r.playPauseButton }).Where(s => s.IsInteractable()).ToList();
            var bad = live.Where(s => new[] { s.FindSelectableOnUp(), s.FindSelectableOnDown(), s.FindSelectableOnLeft(), s.FindSelectableOnRight() }.Any(t => cSel.Contains(t))).ToList();
            Check("keyboard / pad navigation never lands on C (disabled) — and the check is not navigable", bad.Count == 0, string.Join(", ", bad.Select(s => s.name)));
            var a = Row(s_a);
            // Automatic navigation, unchanged by the check (not a Selectable): rows step up / down; PREVIEW sits inside its
            // row's rect, so "right" from a row finds nothing (as before) — logged, not asserted.
            Check("navigation: row A ↓ lands on another live row", a.button.FindSelectableOnDown() is Button down && down.IsInteractable(), a.button.FindSelectableOnDown()?.transform.parent?.name);
            Log($"   nav row A: ↑{Name(a.button.FindSelectableOnUp())} ↓{Name(a.button.FindSelectableOnDown())} →{Name(a.button.FindSelectableOnRight())} · " +
                $"PREVIEW A: ←{Name(a.playPauseButton.FindSelectableOnLeft())} ↑{Name(a.playPauseButton.FindSelectableOnUp())} ↓{Name(a.playPauseButton.FindSelectableOnDown())}");
            SongPreviewPlayer.Instance?.Stop();

            // NEW LAP: finish the lap and start the next one (the real campaign call).
            var d = CampaignService.Data;
            d.levelIndex = CampaignService.Config.LevelCount; d.levelId = ""; d.matchNumber = 1;
            P.ClearPendingRun(save: false);
            Check("new lap started", CampaignService.StartNewLap(), $"lap {d.lap}");
            Invoke("RefreshCompletedRows");
        }),
        (1f, "after the new lap", null, () =>
        {
            CheckRow("new lap: C selectable again, its check kept", s_c, played: true, selectable: true);
            CheckRow("new lap: B unchanged (check on, selectable)", s_b, played: true, selectable: true);
            CheckRow("new lap: A unchanged (check off)", s_a, played: false, selectable: true);
            Check("new lap kept the played history, emptied this lap's use", P.IsSongPlayed(s_b) && P.IsSongPlayed(s_c) && !SongProgression.IsUsedThisLap(s_c));

            // A finished runner (not won) marks A played — and never blocks it.
            CampaignService.OnRunnerFinished(null, s_a);
            Invoke("RefreshCompletedRows");
            CheckRow("A's runner finished (fight not won): check on, still selectable", s_a, played: true, selectable: true);
            Check("a finished runner is not a win (won history untouched)", P.IsSongCompleted(s_a) == s_aWon);

            // Persistence: save → load the file → same history, same per-lap use.
            P.Save();
            var loaded = PlayerProgressStore.Load();
            Check("saved + loaded: played history kept (A, B, C)", loaded != null && new[] { s_a, s_b, s_c }.All(loaded.playedSongIds.Contains) && loaded.version == PlayerProgressData.CurrentVersion,
                  $"v{loaded?.version}, {loaded?.playedSongIds.Count} played");
            Check("saved + loaded: this lap's use kept apart (C not used in the new lap)", loaded != null && !loaded.songCycle.consumedSongIds.Contains(s_c));

            // Migration: a v6 save gets its played history from what it already records.
            var old = new PlayerProgressData { version = 6 };
            old.completedSongIds.Add("won");
            old.songCycle.consumedSongIds.Add("lap");
            old.campaign.positions.Add(new PositionRecordData { lastRunner = new PhaseRecordData { has = true, songId = "lost" } });
            PlayerProgressStore.Migrate(old);
            Check("v6 → v7: played history = won + used this lap + every record's song", old.playedSongIds.SequenceEqual(new[] { "won", "lap", "lost" }), string.Join(",", old.playedSongIds));
            var cur = new PlayerProgressData();
            cur.completedSongIds.Add("won");
            PlayerProgressStore.Migrate(cur);
            Check("a v7 save is never reseeded", cur.playedSongIds.Count == 0);
        }),
    };

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T Field<T>(object o, string n) => (T)o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);
    private static void Invoke(string method) => Sel.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Sel, null);
    private static List<IResourceLocation> Entries() => Field<List<IResourceLocation>>(Sel, "_entries");
    private static List<SongRowView> Rows() => Field<List<SongRowView>>(Sel, "_rows");
    private static SongRowView Row(string id) => Rows()[Entries().FindIndex(l => SongProgression.IdOf(l) == id)];
    private static string SelectedId()
    {
        int i = Field<int>(Sel, "_selectedCatalogIndex");
        return i >= 0 && i < Entries().Count ? SongProgression.IdOf(Entries()[i]) : null;
    }

    private static string Name(Selectable s) => s == null ? "none" : s is Button b && b.GetComponent<SongRowView>() != null ? b.name : $"{s.transform.parent?.name}/{s.name}";

    private static void CheckRow(string name, string id, bool played, bool selectable)
    {
        var r = Row(id);
        bool ok = r.Played == played && r.playedCheck.color == (played ? r.playedOnColor : r.playedOffColor) && r.playedCheck.gameObject.activeInHierarchy
                  && r.button.interactable == selectable && r.playPauseButton.interactable == selectable && r.playPauseButton.gameObject.activeInHierarchy;
        Check(name, ok, $"played {r.Played}, row {r.button.interactable}, preview {r.playPauseButton.interactable}");
    }

    private static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    private static bool Inside(Rect a, Rect b) => a.xMin >= b.xMin - 0.5f && a.xMax <= b.xMax + 0.5f && a.yMin >= b.yMin - 0.5f && a.yMax <= b.yMax + 0.5f;
    private static Vector2 Center(Component c) => ScreenRect((RectTransform)c.transform).center;

    private static GameObject TopHitObject(Vector2 screen)
    {
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, hits);
        return hits.Count > 0 ? hits[0].gameObject : null;
    }

    private static string TopHit(Vector2 screen) => TopHitObject(screen) is { } g ? $"{g.transform.parent?.name}/{g.name}" : "nothing";
    private static bool HitsPreview(string id) => TopHitObject(Center(Row(id).playPauseButton)) is { } g && g.GetComponentInParent<Button>() == Row(id).playPauseButton;

    // A real click: pointer down/up/click sent to whatever the raycast hits first (the same path as a mouse).
    private static void Click(Vector2 screen)
    {
        var target = TopHitObject(screen);
        if (target == null) return;
        var ped = new PointerEventData(EventSystem.current) { position = screen, button = PointerEventData.InputButton.Left };
        var down = ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerDownHandler);
        ped.pointerPress = down; ped.rawPointerPress = target;
        ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerClickHandler);
    }

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
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_deadline = now + 180; }
            if (now > s_deadline) { Log($"TIMEOUT at step {s_step} ({(s_step < Steps.Length ? Steps[s_step].name : "")}) flow={Flow}"); Finish(2); return; }
            if (s_step >= Steps.Length) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = Steps[s_step];
            if (now - s_stepStart < step.wait) return;
            if (step.until != null && !step.until()) return;
            Log($"-- {step.name} (flow={Flow})");
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

    private static void Log(string m) { s_log.AppendLine(m); Debug.Log("[SongSelProbe] " + m); }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors (exit {code})");
        File.WriteAllText("Logs/SongSelectionProbe.txt", s_log.ToString());
        EditorApplication.Exit(code);
    }
}
#endif
