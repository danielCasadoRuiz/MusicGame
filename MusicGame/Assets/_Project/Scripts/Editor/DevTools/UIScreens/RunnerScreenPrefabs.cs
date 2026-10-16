#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Runner group. LiveHud / EndScreen / PauseMenu are already prefabs instanced in Runner.unity (UIRegistry), so
/// they are NEVER regenerated here (that would break the scene instances): they are only PATCHED in place with
/// the elements GameplayHUD/PauseController used to add at runtime — each patch adds a child only when it is
/// missing and (re)assigns the matching view field, so running it again changes nothing.
///   LiveHud.prefab    Cell_Life / Cell_Special in the TopBar (existing 8 cells squeezed into its left 70 %)
///   EndScreen.prefab  ONE results panel: the old separate blocks (rating word + bar, raw score, per-type rows
///                     container, falls / no-fall / session texts, the right-hand RunResources box) are removed
///                     and a nested EvaluationPanel ("Evaluation") becomes the single list GameplayHUD fills;
///                     panel resized, Restart / Continue moved to its bottom edge
///   PauseMenu.prefab  CameraToggleButton (First/Third Person) below the Pause button
///   Items/RunnerPerformanceRow.prefab   one end-screen row per ring type (library slot runnerPerformanceRow) —
///                     appended to that list, so it carries a LayoutElement (patched in when missing)
/// </summary>
public static class RunnerScreenPrefabs
{
    private const string LiveHudPath = UIScreenPrefabContext.ScreensFolder + "/LiveHud.prefab";
    private const string EndScreenPath = UIScreenPrefabContext.ScreensFolder + "/EndScreen.prefab";
    private const string PauseMenuPath = UIScreenPrefabContext.ScreensFolder + "/PauseMenu.prefab";
    private const string PerformanceRowPath = UIScreenPrefabContext.ItemsFolder + "/RunnerPerformanceRow.prefab";
    private const float RowHeight = 20f;

    public static void Build(UIScreenPrefabContext ctx)
    {
        ctx.Library.runnerPerformanceRow = ctx.Prefab<RunnerPerformanceRowView>(PerformanceRowPath, BuildPerformanceRow);
        ctx.Patch(PerformanceRowPath, EnsureRowLayout); // a row generated before it lived in a layout list

        var collectibles = FindCollectiblesConfig();
        ctx.Patch(LiveHudPath, root => PatchLiveHud(root, collectibles));
        var evaluationPanel = ctx.Library.evaluationPanel;
        if (evaluationPanel == null) ctx.Log.AppendLine("  FAIL  Runner: library evaluationPanel missing — EndScreen keeps no results list");
        ctx.Patch(EndScreenPath, root => PatchEndScreen(root, evaluationPanel));
        ctx.Patch(PauseMenuPath, PatchPauseMenu);
    }

    private static MusicRunnerCollectiblesConfig FindCollectiblesConfig()
    {
        var guids = AssetDatabase.FindAssets("t:MusicRunnerCollectiblesConfig");
        return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<MusicRunnerCollectiblesConfig>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
    }

    // ── Items ────────────────────────────────────────────────────────────────

    // Port of the old GameplayHUD.BuildPerformanceRow (label colour / fill colour / texts are set per type at runtime).
    private static GameObject BuildPerformanceRow()
    {
        var go = UIScreenPrefabContext.Root("PerformanceRow", new Vector2(0f, 20f));
        var row = (RectTransform)go.transform;
        UIFactory.SetBox(row, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 20f));
        var view = go.AddComponent<RunnerPerformanceRowView>();

        var label = UIFactory.CreateText("Label", row, "KICK", 12, Color.white, TextAlignmentOptions.Left);
        UIFactory.SetBox(label.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(70f, 0f));

        var barBg = UIFactory.CreateFillBar("Bar", row, new Color(0.12f, 0.12f, 0.12f), Color.white * 0.8f, out var barFill);
        UIFactory.SetBox(barBg.rectTransform, new Vector2(0f, 0.2f), new Vector2(1f, 0.8f), new Vector2(0f, 0.5f), new Vector2(74f, 0f), new Vector2(-150f, 0f));
        barFill.fillAmount = 0.5f;

        var info = UIFactory.CreateText("Info", row, "50%  5/10", 10, Color.white, TextAlignmentOptions.Right);
        UIFactory.SetBox(info.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(70f, 0f));

        view.label = label;
        view.barFill = barFill;
        view.info = info;
        EnsureRowLayout(go);
        return go;
    }

    // Height inside the end screen's results list (a VerticalLayoutGroup that controls child heights).
    private static void EnsureRowLayout(GameObject row)
    {
        var le = row.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le == null) le = row.AddComponent<UnityEngine.UI.LayoutElement>();
        le.minHeight = le.preferredHeight = RowHeight;
    }

    // ── LiveHud: LIFE / SPECIAL cells ───────────────────────────────────────────

    private static void PatchLiveHud(GameObject root, MusicRunnerCollectiblesConfig collectibles)
    {
        var view = root.GetComponent<LiveHudView>();
        if (view == null || view.scoreValue == null) return;
        var bar = view.scoreValue.rectTransform.parent.parent as RectTransform; // TopBar
        if (bar == null) return;

        if (bar.Find("Cell_Life") == null)
        {
            // The top bar's existing cells move into its left 70 %; LIFE and SPECIAL become two more cells of the
            // SAME bar; the right end stays free for Pause + First/Third Person (PauseMenu).
            var cells = new List<RectTransform>();
            foreach (var v in new[] { view.kickValue, view.snareValue, view.hiHatValue, view.beatValue, view.onsetValue, view.impactValue, view.scoreValue, view.totalValue })
                if (v != null) cells.Add((RectTransform)v.rectTransform.parent);
            cells.Sort((x, y) => x.anchorMin.x.CompareTo(y.anchorMin.x));
            const float existingShare = 0.70f, resourceCell = 0.075f;
            for (int i = 0; i < cells.Count; i++)
            {
                cells[i].anchorMin = new Vector2(existingShare * i / cells.Count, cells[i].anchorMin.y);
                cells[i].anchorMax = new Vector2(existingShare * (i + 1) / cells.Count, cells[i].anchorMax.y);
            }
            var lifeColor = collectibles != null ? collectibles.lifeColor : Color.white;
            var specialColor = collectibles != null ? collectibles.specialColor : new Color(1f, 0.15f, 0.85f);
            BuildBarCell(bar, "Cell_Life", "HUD.Life", lifeColor, existingShare, existingShare + resourceCell);
            BuildBarCell(bar, "Cell_Special", "HUD.Special", specialColor, existingShare + resourceCell, existingShare + 2f * resourceCell);
        }

        view.lifeLabel = Text(bar, "Cell_Life/Label");
        view.lifeValue = Text(bar, "Cell_Life/Value");
        view.specialLabel = Text(bar, "Cell_Special/Label");
        view.specialValue = Text(bar, "Cell_Special/Value");
    }

    // Same Label/Value structure and offsets as the bar's existing cells.
    private static void BuildBarCell(RectTransform bar, string name, string labelKey, Color labelColor, float xMin, float xMax)
    {
        var cell = UIFactory.CreateRect(name, bar);
        UIFactory.SetBox(cell, new Vector2(xMin, 0f), new Vector2(xMax, 1f), new Vector2(0f, 1f), new Vector2(6f, 0f), new Vector2(-6f, 0f));
        var label = UIScreenPrefabContext.Localized(UIFactory.CreateText("Label", cell, "", 12, labelColor, TextAlignmentOptions.TopLeft), labelKey);
        UIFactory.SetBox(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -3f), new Vector2(0f, 20f));
        var value = UIFactory.CreateText("Value", cell, "0", 14, Color.white, TextAlignmentOptions.TopLeft);
        UIFactory.SetBox(value.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -22f), new Vector2(0f, 20f));
        value.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
    }

    // ── EndScreen: ONE results panel ────────────────────────────────────────────

    private const float EndPanelWidth = 600f, EndPanelHeight = 780f;

    // Blocks merged into the single list (deleted when present; nothing in Runner.unity overrides them).
    private static readonly string[] LegacyEndScreenBlocks =
        { "Rating", "RatingBarBg", "RatingBar", "ScoreSummary", "PerformanceRows", "Falls", "NoFallBonus", "Session" };

    private static void PatchEndScreen(GameObject root, EvaluationPanel evaluationPrefab)
    {
        var view = root.GetComponent<EndScreenView>();
        if (view == null) return;
        var rt = (RectTransform)root.transform;
        var panel = rt.Find("Panel") as RectTransform;
        if (panel == null) return;

        foreach (var block in LegacyEndScreenBlocks)
        {
            var t = panel.Find(block);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
        var box = rt.Find("RunResources");
        if (box != null) Object.DestroyImmediate(box.gameObject);

        panel.sizeDelta = new Vector2(EndPanelWidth, EndPanelHeight);

        // The single results list, right under the title.
        var list = panel.Find("Evaluation");
        if (list == null && evaluationPrefab != null)
        {
            var nested = UIScreenPrefabContext.Nested(evaluationPrefab, panel);
            nested.name = "Evaluation";
            list = nested.transform;
        }
        if (list != null)
        {
            var lrt = (RectTransform)list;
            UIFactory.SetBox(lrt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -54f), new Vector2(-32f, 0f));
            var bg = list.GetComponent<UnityEngine.UI.Image>();
            if (bg != null) bg.color = new Color(0f, 0f, 0f, 0f); // the panel is the background
            view.evaluationPanel = list.GetComponent<EvaluationPanel>();
        }

        // Buttons on the panel's bottom edge.
        BottomButton(panel, "RestartButton", -83f);
        BottomButton(panel, "ContinueButton", 83f);
    }

    private static void BottomButton(RectTransform panel, string name, float x)
    {
        var b = panel.Find(name) as RectTransform;
        if (b == null) return;
        var bottom = new Vector2(0.5f, 0f);
        UIFactory.SetBox(b, bottom, bottom, bottom, new Vector2(x, 20f), new Vector2(150f, 40f));
    }

    // ── PauseMenu: First/Third Person toggle ────────────────────────────────────

    // OUTSIDE the Runner's 44 px top bar, directly below the Pause button, right-aligned. Its label text/icon are
    // set at runtime from the current camera mode (PauseController.RefreshCameraToggleDisplay).
    private static void PatchPauseMenu(GameObject root)
    {
        var view = root.GetComponent<PauseView>();
        if (view == null) return;
        var rt = (RectTransform)root.transform;

        if (rt.Find("CameraToggleButton") == null)
        {
            var btn = UIFactory.CreateButton("CameraToggleButton", rt, "Pause.ThirdPerson", out var label);
            label.fontSize = 12;
            UIFactory.SetBox(btn.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-12f, -46f), new Vector2(120f, 28f));
            btn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
            label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        }

        var toggle = rt.Find("CameraToggleButton");
        view.cameraToggleButtonRoot = toggle.gameObject;
        view.cameraToggleButton = toggle.GetComponent<UnityEngine.UI.Button>();
        if (view.cameraToggleLabel == null) view.cameraToggleLabel = Text(toggle, "CameraToggleButtonLabel");
    }

    private static TextMeshProUGUI Text(Transform parent, string path)
    {
        var t = parent.Find(path);
        return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
    }
}
#endif
