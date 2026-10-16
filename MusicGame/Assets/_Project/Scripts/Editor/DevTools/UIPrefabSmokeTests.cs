#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor smoke test of the generated UI prefabs (no Play Mode): instantiates every library prefab under a
/// canvas — the evaluation panel under an INACTIVE parent first, like the runner end screen — fills data
/// through the real view APIs, forces Unity's layout and checks sizes/references.
/// Batch: -executeMethod UIPrefabSmokeTests.RunFromCommandLine   (Logs/UIPrefabSmokeTests.txt)
/// </summary>
public static class UIPrefabSmokeTests
{
    private static StringBuilder s_r;
    private static int s_fail;

    [MenuItem("Tools/MusicGame/UI/Run Prefab Smoke Tests")]
    public static void RunMenu() => Debug.Log(Run());

    public static void RunFromCommandLine()
    {
        string report = Run();
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/UIPrefabSmokeTests.txt", report, Encoding.UTF8);
        EditorApplication.Exit(s_fail == 0 ? 0 : 1);
    }

    private static void Check(bool ok, string what) { if (!ok) s_fail++; s_r.AppendLine((ok ? "  PASS  " : "  FAIL  ") + what); }

    public static string Run()
    {
        s_r = new StringBuilder("[UIPrefabSmokeTests]\n"); s_fail = 0;
        var lib = AssetDatabase.LoadAssetAtPath<UIPrefabLibrarySO>("Assets/_Project/Configs/UI/UIPrefabLibrary.asset");
        Check(lib != null && Resources.Load<AppConfigSO>("AppConfig").uiPrefabs == lib, "library exists and is AppConfig.uiPrefabs");
        var canvasGo = new GameObject("SmokeCanvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = (RectTransform)canvasGo.transform;
        canvas.sizeDelta = new Vector2(1920f, 1080f);
        try
        {
            // Stars: full / half / empty / pending.
            var stars = Object.Instantiate(lib.starRating, canvas);
            stars.SetHalfStars(7);
            var fills = stars.GetComponentsInChildren<Image>().Where(i => i.type == Image.Type.Filled).Select(i => i.fillAmount).ToArray();
            Check(fills.SequenceEqual(new[] { 1f, 1f, 1f, 0.5f, 0f }), $"StarRating 3.5 → {string.Join(",", fills)}");
            stars.SetHalfStars(-1);
            Check(stars.GetComponentsInChildren<Image>().Where(i => i.type == Image.Type.Filled).All(i => i.fillAmount == 0f), "pending → empty stars");

            // Evaluation panel built under an INACTIVE parent (the runner end screen case), then shown.
            var hidden = new GameObject("HiddenScreen", typeof(RectTransform));
            hidden.transform.SetParent(canvas, false);
            hidden.SetActive(false);
            var panel = Object.Instantiate(lib.evaluationPanel, hidden.transform);
            panel.AddHeader("THIS FIGHT");
            panel.AddStars("Points 4.850 / 5.000", 9);
            panel.AddNote(string.Concat(Enumerable.Repeat("Recollides 48/50 · Precisió de temps 92% · Caigudes 1 · ", 4)));
            panel.AddHighlight("NEW RECORD!");
            panel.AddWarning("Derrota: resultat informatiu, no compta com a rècord.");
            panel.AddStarsSmall("Darrera cursa", 6);
            Check(true, "panel filled while its screen is inactive (no TextMeshPro exception)");
            hidden.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Rect);
            var rows = panel.GetComponentsInChildren<EvaluationRowView>();
            float noteHeight = ((RectTransform)rows[2].transform).rect.height, lineHeight = ((RectTransform)rows[3].transform).rect.height;
            Check(rows.Length == 6, $"6 rows instantiated from their prefabs ({rows.Length})");
            Check(noteHeight > lineHeight + 10f, $"a long wrapped note makes its row taller ({noteHeight:0} vs {lineHeight:0})");
            Check(panel.Rect.rect.height > rows.Sum(r => ((RectTransform)r.transform).rect.height), $"panel grows with its rows ({panel.Rect.rect.height:0})");
            panel.Clear();
            Check(panel.GetComponentsInChildren<EvaluationRowView>().Length == 0, "Clear removes every row (no duplicates on re-show)");

            // Speech bubble: show / text / fade parts.
            var bubble = Object.Instantiate(lib.speechBubble, canvas);
            bubble.Show("Ésser de la música", "Abans del Big Bang hi va haver un compte enrere. El vaig marcar jo.", 0f, 0f);
            Check(bubble.IsVisible && bubble.BubbleRect.sizeDelta.x > 240f && bubble.BubbleRect.sizeDelta.y > 60f, $"speech bubble sized to its text ({bubble.BubbleRect.sizeDelta})");
            Check(bubble.CurrentText.StartsWith("Abans"), "bubble text set");

            // Popup, skip, language selector.
            var popup = Object.Instantiate(lib.campaignPopup, canvas);
            popup.Show("Title", "Body", null, Color.white, "OK", null);
            Check(popup.IsOpen, "popup opens (single button)");
            Check(lib.skipButton.GetComponentInChildren<LocalizedText>().key == "Fight.Skip", "skip label localized (Fight.Skip)");
            var selector = Object.Instantiate(lib.languageSelector, canvas);
            selector.Build(new[] { "auto", "ca", "en" }, m => m, _ => { });
            selector.SetSelected("ca");
            var options = selector.GetComponentsInChildren<LanguageOptionView>();
            Check(options.Length == 3 && options.Count(o => o.GetComponent<Outline>().enabled) == 1, "language selector: 3 options, one selected");

            // Progression rows + collection card.
            var screen = Object.Instantiate(lib.progressionScreen, canvas);
            Check(screen.listContent != null && screen.challengeButton != null, "ProgressionScreen references assigned");
            Check(screen.GetComponentInChildren<EvaluationPanel>(true) == null, "ProgressionScreen has no detail overlay");
            var header = Object.Instantiate(lib.progressionLevelHeader, screen.listContent);
            header.Set("► NIVELL 1", "Aquesta volta 2/5 · 1 dominades", "Cursa 4,5 · 3/5", 9, "Combat pendent", -1, "Total 4", 8);
            var row = Object.Instantiate(lib.progressionMatchRow, screen.listContent);
            row.Set(2, true, "PARTIDA 2", "Glorious Trainer — I Feel So High", 10, null, "Bach · Nivell 3", 9, 9);
            Check(row.GetComponentInChildren<Selectable>(true) == null, "match row is informative (no Button / Selectable)");
            Object.Instantiate(lib.progressionColumnHeader, screen.listContent);
            Object.Instantiate(lib.progressionBossHeader, screen.listContent).Set("Ésser de la música · bloquejat");
            Object.Instantiate(lib.progressionBossStage, screen.listContent).Set("Combat final 1 · pendent", -1);
            LayoutRebuilder.ForceRebuildLayoutImmediate(screen.listContent);
            Check(screen.listContent.childCount == 5 && screen.listContent.rect.height >= 74f + 84f + 30f, $"Progression list lays out its rows ({screen.listContent.rect.height:0})");
            Check(row.background.color == row.currentColor && row.totalStars.HalfStars == 9, "match row: current colour + stars");
            var card = Object.Instantiate(lib.rivalCard, Object.Instantiate(lib.collectionScreen, canvas).grid);
            card.Set(null, false, "???", "0 / 4", true);
            Check(card.nameText.text == "???" && card.portrait.color == card.emptyColor, "rival card fills");
        }
        catch (System.Exception e) { s_fail++; s_r.AppendLine("  FAIL  exception: " + e); }
        finally { Object.DestroyImmediate(canvasGo); }
        s_r.AppendLine(s_fail == 0 ? "RESULT PASS" : $"RESULT FAIL ({s_fail})");
        return s_r.ToString();
    }
}
#endif
