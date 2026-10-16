#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Proves the "one panel" results screens (no Play Mode):
///   • EndScreen.prefab (Runner): exactly ONE EvaluationPanel, wired to EndScreenView, inside the panel; none of
///     the old separate blocks (rating word/bar, raw score, rows container, falls/no-fall/session texts, the
///     RunResources box); RunnerPerformanceRow carries a LayoutElement (it is appended to that list).
///   • MatchResult.prefab (Fight): exactly ONE EvaluationPanel; no separate rival / rounds / summary / level texts.
///   • Fit: a worst-case list (Runner: header + stars + 4 lines + note + 7 type bars + record + next; Match: a won
///     campaign match with every section) laid out by Unity stays above the buttons.
///   • Localization: every key the two panels and the main-menu header use exists in UIText (en + ca).
/// Batch: -executeMethod ResultsPanelDiagnostic.RunFromCommandLine   (Logs/ResultsPanelDiagnostic.txt, exit 0 = PASS)
/// </summary>
public static class ResultsPanelDiagnostic
{
    private const string EndScreenPath = "Assets/_Project/Prefabs/UI/EndScreen.prefab";
    private const string MatchResultPath = "Assets/_Project/Prefabs/UI/MatchResult.prefab";
    private const string LibraryPath = "Assets/_Project/Configs/UI/UIPrefabLibrary.asset";

    private static readonly string[] LegacyEndBlocks =
        { "Rating", "RatingBarBg", "RatingBar", "ScoreSummary", "PerformanceRows", "Falls", "NoFallBonus", "Session", "RunResources", "RunnerEvaluation" };
    private static readonly string[] LegacyMatchBlocks = { "Rival", "Rounds", "Summary", "Level" };

    private static readonly string[] Keys =
    {
        "EndScreen.Title", "Results.RaceStars", "Results.Score", "Results.NoFallBonus", "Results.FallsBonusLost", "Results.Falls",
        "Results.ForTheFight", "Results.Specials", "Results.SpecialsSplit", "Results.Combos", "Results.Lives", "Results.LivesCapped",
        "Results.BestRace", "Results.MatchPassed", "Results.MatchNotPassed", "Results.FightWon", "Results.FightLost", "Results.FightStars",
        "Results.Rounds", "Results.RaceHeader", "Results.MatchHeader", "Results.MatchHeaderFree", "Results.NotPassed", "Results.LivesLeft",
        "Results.MatchStars", "Results.Advanced", "Results.LevelCompletedNext", "Results.ReplayedPosition", "Results.RecordImproved",
        "Results.RecordKept", "Campaign.PositionOf", "Campaign.Level", "MainMenu.ExtraLives", "MainMenu.LapDone", "MainMenu.CampaignStatus",
        "Eval.NewRecord", "Eval.NextFight", "Eval.NotCampaign", "Eval.Perfect", "Eval.Falls", "Eval.LapCompleted", "Eval.JustMastered",
        "MatchResult.NewRival", "MatchResult.NewRivalLevel", "MatchResult.FinalHealth",
    };

    private static StringBuilder s_r;
    private static int s_fail;

    [MenuItem("Tools/MusicGame/UI/Run Results Panel Diagnostic")]
    public static void RunMenu() => Debug.Log(Run());

    public static void RunFromCommandLine()
    {
        string report = Run();
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/ResultsPanelDiagnostic.txt", report, Encoding.UTF8);
        EditorApplication.Exit(s_fail == 0 ? 0 : 1);
    }

    private static void Check(bool ok, string what) { if (!ok) s_fail++; s_r.AppendLine((ok ? "  PASS  " : "  FAIL  ") + what); }

    public static string Run()
    {
        s_r = new StringBuilder("[ResultsPanelDiagnostic]\n"); s_fail = 0;
        var lib = AssetDatabase.LoadAssetAtPath<UIPrefabLibrarySO>(LibraryPath);
        Check(lib != null && lib.evaluationPanel != null && lib.runnerPerformanceRow != null, "library: evaluationPanel + runnerPerformanceRow assigned");
        if (lib == null) return Done();

        var row = lib.runnerPerformanceRow;
        var le = row != null ? row.GetComponent<LayoutElement>() : null;
        Check(le != null && le.preferredHeight > 0f, "RunnerPerformanceRow has a LayoutElement height (list item)");

        var canvasGo = new GameObject("ResultsDiagCanvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = (RectTransform)canvasGo.transform;
        canvas.sizeDelta = new Vector2(1920f, 1080f);
        try
        {
            RunnerEndScreen(lib, canvas);
            MatchResult(canvas);
        }
        finally { Object.DestroyImmediate(canvasGo); }

        Localization();
        return Done();
    }

    private static string Done()
    {
        s_r.AppendLine($"RESULT {(s_fail == 0 ? "PASS" : "FAIL")} — {s_fail} failure(s)");
        return s_r.ToString();
    }

    // ── Runner end screen ────────────────────────────────────────────────────

    private static void RunnerEndScreen(UIPrefabLibrarySO lib, RectTransform canvas)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(EndScreenPath);
        Check(asset != null, "EndScreen.prefab exists");
        if (asset == null) return;
        var view = asset.GetComponent<EndScreenView>();
        var panels = asset.GetComponentsInChildren<EvaluationPanel>(true);
        Check(view != null && view.evaluationPanel != null, "EndScreenView.evaluationPanel wired");
        Check(panels.Length == 1, $"EndScreen: exactly one results list (found {panels.Length})");
        var all = asset.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
        var legacy = LegacyEndBlocks.Where(all.Contains).ToList();
        Check(legacy.Count == 0, "EndScreen: no separate legacy blocks" + (legacy.Count > 0 ? " — still has " + string.Join(", ", legacy) : ""));
        if (view == null || view.evaluationPanel == null) return;

        var inst = Object.Instantiate(asset, canvas, false);
        inst.SetActive(true);
        var iv = inst.GetComponent<EndScreenView>();
        var p = iv.evaluationPanel;
        p.AddHeader("LEVEL 3 · match 4 of 6");
        p.AddStars("Race rating  NEW RECORD!", 7);
        p.AddLine("Score 12,345 / 20,000 (62%)");
        p.AddNote("Pickups 48/50  ·  Timing 92%");
        p.AddLine("Falls 2 — no-fall bonus (+20%) lost");
        for (int i = 0; i < 7; i++) { var r = p.AddItem(lib.runnerPerformanceRow, "Row_" + i); r?.Set("KICK", Color.cyan, 0.7f, "70%  7/10"); }
        p.AddLine("FOR THE FIGHT:  2 specials  ·  x3 combos 1 · x4 combos 0");
        p.AddLine("Lives +1 (2 collected; the rest were already earned in an earlier attempt of this match)  ·  3 extra lives for Fight Again");
        p.AddStarsSmall("Best race of this match", 8);
        p.AddHighlight("The fight is next.");
        var listRt = p.Rect;
        LayoutRebuilder.ForceRebuildLayoutImmediate(listRt);
        var panelRt = (RectTransform)listRt.parent;
        float listBottom = -listRt.anchoredPosition.y + listRt.rect.height;           // from the panel's top edge
        float buttonsTop = panelRt.rect.height - ButtonTopFromBottom(panelRt, iv.restartButton);
        Check(listBottom <= buttonsTop, $"EndScreen worst case fits: list ends {listBottom:0} px from the panel top, buttons start at {buttonsTop:0}");
        Check(p.transform.childCount == 16, $"EndScreen list holds every line in ONE place ({p.transform.childCount} rows, expected 16)");
        Object.DestroyImmediate(inst);
    }

    private static float ButtonTopFromBottom(RectTransform panel, Button b)
    {
        if (b == null) return 0f;
        var rt = (RectTransform)b.transform;
        return rt.anchoredPosition.y + rt.rect.height * (1f - rt.pivot.y);
    }

    // ── Match result ─────────────────────────────────────────────────────────

    private static void MatchResult(RectTransform canvas)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(MatchResultPath);
        Check(asset != null, "MatchResult.prefab exists");
        if (asset == null) return;
        var view = asset.GetComponent<MatchResultView>();
        var panels = asset.GetComponentsInChildren<EvaluationPanel>(true);
        Check(view != null && view.evaluationPanel != null && panels.Length == 1, $"MatchResult: exactly one results list, wired (found {panels.Length})");
        var names = asset.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
        var legacy = LegacyMatchBlocks.Where(names.Contains).ToList();
        Check(legacy.Count == 0, "MatchResult: no separate rival / rounds / summary / level texts" + (legacy.Count > 0 ? " — still has " + string.Join(", ", legacy) : ""));
        if (view == null || view.evaluationPanel == null) return;

        var inst = Object.Instantiate(asset, canvas, false);
        inst.SetActive(true);
        var iv = inst.GetComponent<MatchResultView>();
        var p = iv.evaluationPanel;
        p.AddHeader("FIGHT · won vs Mozart (version 3)");
        p.AddStars("Fight rating  PERFECT!  NEW RECORD!", 10);
        p.AddLine("Rounds 2–1  ·  KO  ·  Health — You: 64%  Rival: 0%  ·  wins on points");
        p.AddNote("Health kept 80%  ·  Accuracy 61%  ·  Hits 22/36");
        p.AddHeader("RACE · A very long song title that might wrap");
        p.AddStars("Race rating", 7);
        p.AddNote("Score 12,345 / 20,000 (62%)  ·  Falls 2");
        p.AddHeader("MATCH · LEVEL 3 · match 4 of 6");
        p.AddStars("Match rating (race + fight)", 8);
        p.AddHighlight("Level completed! Next: LEVEL 4 · match 1 of 6");
        p.AddStarsSmall("Match record improved", 8);
        p.AddHighlight("MATCH MASTERED!");
        p.AddHighlight("NEW RIVAL UNLOCKED — Mozart — Level 3");
        var listRt = p.Rect;
        LayoutRebuilder.ForceRebuildLayoutImmediate(listRt);
        var root = (RectTransform)inst.transform;
        float listBottom = -listRt.anchoredPosition.y + listRt.rect.height;            // from the screen's top edge
        float buttonsTop = root.rect.height - ButtonTopFromBottom(root, iv.continueButton);
        Check(listBottom <= buttonsTop, $"MatchResult worst case fits: list ends {listBottom:0} px from the top, buttons start at {buttonsTop:0}");
        Object.DestroyImmediate(inst);
    }

    // ── Localization ─────────────────────────────────────────────────────────

    private static void Localization()
    {
        var ui = UnityEditor.Localization.LocalizationEditorSettings.GetStringTableCollection("UIText");
        Check(ui != null, "UIText collection exists");
        if (ui == null) return;
        foreach (var table in ui.StringTables)
        {
            var missing = Keys.Where(k => { var e = table.GetEntry(k); return e == null || string.IsNullOrEmpty(e.Value); }).ToList();
            Check(missing.Count == 0, $"UIText {table.LocaleIdentifier.Code}: every results / menu key present" + (missing.Count > 0 ? " — missing " + string.Join(", ", missing) : ""));
        }
    }
}
#endif
