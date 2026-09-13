#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot Editor tool: builds the three gameplay UI prefabs (LiveHud, EndScreen, PauseMenu)
/// using the exact same UIFactory calls GameplayHUD/PauseController used to run at PLAY time,
/// saves them as real .prefab assets under Assets/_Project/Prefabs/UI/, leaves connected
/// instances in the currently open scene, and wires a UIRegistry component to them.
///
/// Run it ONCE via Tools > MusicGame > Build UI Prefabs. After that, GameplayHUD/PauseController
/// find the UIRegistry at startup and just READ these prefab instances instead of building
/// anything themselves — open the .prefab assets in the Prefab editor any time afterward to
/// retouch colors/fonts/layout by hand; the game keeps working as long as the wired fields on
/// LiveHudView/EndScreenView/PauseView still point at the right children.
///
/// Safe to re-run: it deletes and rebuilds all three prefabs + the scene instances + UIRegistry
/// from scratch each time (so hand-made edits to the PREFAB ASSETS themselves are NOT preserved
/// by re-running this — it's meant to be run once to get a starting point you then hand-tune).
/// </summary>
public static class UIPrefabBuilder
{
    private const string FolderPath = "Assets/_Project/Prefabs/UI";

    [MenuItem("Tools/MusicGame/Build UI Prefabs")]
    public static void Build()
    {
        EnsureFolder();

        // Fresh Canvas/EventSystem for this bake — UIFactory's own cached static Canvas may be
        // stale/gone between Editor domain reloads, so don't rely on it; build explicit ones here.
        var canvasGO = GameObject.Find("[UI Canvas]") ?? new GameObject("[UI Canvas]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        var canvasRect = canvasGO.GetComponent<RectTransform>();

        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            new GameObject("[EventSystem]", typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

        var config = FindGameplayConfig();

        var liveHud   = BuildLiveHud(canvasRect, config);
        var endScreen = BuildEndScreen(canvasRect);
        var pauseMenu = BuildPauseMenu(canvasRect);

        // IMPORTANT: SaveAsPrefabAssetAndConnect returns the PREFAB ASSET (disk-only, never
        // instantiated into the running scene) — it also CONVERTS the passed scene GameObject
        // into a connected prefab instance, but that instance is a DIFFERENT object than the
        // returned one. UIRegistry must be wired to the ORIGINAL scene variables (liveHud/
        // endScreen/pauseMenu) below, NOT to the return value of SaveAndConnect — wiring to the
        // asset silently does nothing at runtime (no exception, the component just isn't part of
        // any live scene, so Update()/onClick never reach it).
        SaveAndConnect(liveHud,   "LiveHud.prefab");
        SaveAndConnect(endScreen, "EndScreen.prefab");
        SaveAndConnect(pauseMenu, "PauseMenu.prefab");

        WireRegistry(
            liveHud.GetComponent<LiveHudView>(),
            endScreen.GetComponent<EndScreenView>(),
            pauseMenu.GetComponent<PauseView>());

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[UIPrefabBuilder] Done — LiveHud.prefab / EndScreen.prefab / PauseMenu.prefab " +
                  $"saved under {FolderPath}, instances wired into UIRegistry in the scene. " +
                  "Remember to save the scene (Ctrl+S).");
    }

    // Best-effort — used only to pre-color the ring-type labels/rows to match the game's actual
    // config at bake time; picks the first MusicRunnerCollectiblesConfig asset found. Falls back
    // to white if none exists yet (still fully functional, just uncolored until you retouch the
    // prefab).
    private static MusicRunnerCollectiblesConfig FindGameplayConfig()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:MusicRunnerCollectiblesConfig"))
            return AssetDatabase.LoadAssetAtPath<MusicRunnerCollectiblesConfig>(AssetDatabase.GUIDToAssetPath(guid));
        return null;
    }

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(FolderPath)) return;
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs"))
            AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");
        AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "UI");
    }

    private static GameObject SaveAndConnect(GameObject sceneInstance, string fileName)
    {
        string path = $"{FolderPath}/{fileName}";
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(sceneInstance, path, InteractionMode.UserAction);
        return prefab;
    }

    private static void WireRegistry(LiveHudView liveHud, EndScreenView endScreen, PauseView pause)
    {
        var registryGO = GameObject.Find("[UI Registry]");
        if (registryGO == null) registryGO = new GameObject("[UI Registry]");
        var registry = registryGO.GetComponent<UIRegistry>() ?? registryGO.AddComponent<UIRegistry>();

        var so = new SerializedObject(registry);
        so.FindProperty("liveHud").objectReferenceValue   = liveHud;
        so.FindProperty("endScreen").objectReferenceValue = endScreen;
        so.FindProperty("pause").objectReferenceValue      = pause;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── Live HUD ──────────────────────────────────────────────────────────────

    private static GameObject BuildLiveHud(RectTransform canvas, MusicRunnerCollectiblesConfig config)
    {
        var root = UIFactory.CreateRect("LiveHUD", canvas);
        UIFactory.SetBox(root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 100f));
        var view = root.gameObject.AddComponent<LiveHudView>();

        var cells = new (string labelKey, RingType? type, System.Action<Text> assignValue, System.Action<Text> assignLabel)[]
        {
            ("HUD.Kick",   RingType.Kick,   t => view.kickValue   = t, t => view.kickLabel   = t),
            ("HUD.Snare",  RingType.Snare,  t => view.snareValue  = t, t => view.snareLabel  = t),
            ("HUD.HiHat",  RingType.HiHat,  t => view.hiHatValue  = t, t => view.hiHatLabel  = t),
            ("HUD.Beat",   RingType.Beat,   t => view.beatValue   = t, t => view.beatLabel   = t),
            ("HUD.Onset",  RingType.Onset,  t => view.onsetValue  = t, t => view.onsetLabel  = t),
            ("HUD.Impact", RingType.Impact, t => view.impactValue = t, t => view.impactLabel = t),
            ("HUD.Score",  null,            t => view.scoreValue  = t, t => view.scoreLabel  = t),
            ("HUD.Total",  null,            t => view.totalValue  = t, t => view.totalLabel  = t),
        };

        var topBar = UIFactory.CreatePanel("TopBar", root, new Color(0.03f, 0.03f, 0.03f, 0.88f));
        UIFactory.SetBox(topBar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 44f));

        for (int i = 0; i < cells.Length; i++)
        {
            var cell = UIFactory.CreateRect($"Cell_{cells[i].labelKey}", topBar.rectTransform);
            float xMin = i / 8f, xMax = (i + 1) / 8f;
            UIFactory.SetBox(cell, new Vector2(xMin, 0f), new Vector2(xMax, 1f), new Vector2(0f, 1f), new Vector2(6f, 0f), new Vector2(-6f, 0f));

            Color labelColor = cells[i].type.HasValue && config != null ? config.RingColor(cells[i].type.Value) : Color.white;
            var label = UIFactory.CreateText("Label", cell, Loc.Get(cells[i].labelKey), 12, labelColor, TextAnchor.UpperLeft);
            UIFactory.SetBox(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -3f), new Vector2(0f, 20f));
            cells[i].assignLabel(label);

            var value = UIFactory.CreateText("Value", cell, "0", 14, Color.white, TextAnchor.UpperLeft);
            UIFactory.SetBox(value.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -22f), new Vector2(0f, 20f));

            cells[i].assignValue(value);
        }

        var progressBg = UIFactory.CreateFillBar("SongProgress", root, new Color(0.10f, 0.10f, 0.10f), new Color(0.18f, 0.75f, 0.95f), out var progressFill);
        UIFactory.SetBox(progressBg.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -45f), new Vector2(0f, 4f));
        view.progressFill = progressFill;

        var tagsStrip = UIFactory.CreateRect("TagsStrip", root);
        UIFactory.SetBox(tagsStrip, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(0f, 54f));
        tagsStrip.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.03f, 0.03f, 0.75f);
        view.tagsStrip = tagsStrip.gameObject;

        var tagStyle = UIFactory.CreateText("Style", tagsStrip, "", 11, new Color(0.55f, 0.8f, 1f), TextAnchor.UpperLeft);
        UIFactory.SetBox(tagStyle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(8f, -3f), new Vector2(-8f, 16f));
        view.tagStyle = tagStyle;

        var tagVibe = UIFactory.CreateText("Vibe", tagsStrip, "", 11, Color.white, TextAnchor.UpperLeft);
        UIFactory.SetBox(tagVibe.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(8f, -19f), new Vector2(-8f, 16f));
        view.tagVibe = tagVibe;

        var tagOther = UIFactory.CreateText("Other", tagsStrip, "", 11, Color.white, TextAnchor.UpperLeft);
        UIFactory.SetBox(tagOther.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(8f, -35f), new Vector2(-8f, 16f));
        view.tagOther = tagOther;

        tagsStrip.gameObject.SetActive(false);
        return root.gameObject;
    }

    // ── End screen ────────────────────────────────────────────────────────────

    private static GameObject BuildEndScreen(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("EndScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<EndScreenView>();

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0f, 0f, 0f, 0.55f));
        UIFactory.Stretch(dim.rectTransform);

        float pw = 420f, ph = 720f;
        var panel = UIFactory.CreatePanel("Panel", root, new Color(0.04f, 0.04f, 0.04f, 0.97f));
        UIFactory.SetBox(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(pw, ph));
        var border = panel.gameObject.AddComponent<Outline>();
        border.effectColor    = new Color(0.2f, 0.2f, 0.2f, 1f);
        border.effectDistance = new Vector2(2f, -2f);

        var content = panel.rectTransform;
        float y = -16f;

        var title = UIFactory.CreateText("Title", content, Loc.Get("EndScreen.Title"), 20, Color.white);
        UIFactory.StackTop(title.rectTransform, ref y, 30f);
        view.titleText = title;

        var rating = UIFactory.CreateText("Rating", content, "", 22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIFactory.StackTop(rating.rectTransform, ref y, 32f);
        view.ratingLabel = rating;

        var ratingBar = UIFactory.CreateFillBar("RatingBar", content, new Color(0.12f, 0.12f, 0.12f), Color.white, out var ratingFill);
        UIFactory.StackTop(ratingBar.rectTransform, ref y, 16f, 46f);
        view.ratingBarFill = ratingFill;
        y -= 6f;

        var scoreSummary = UIFactory.CreateText("ScoreSummary", content, "", 12, new Color(0.6f, 0.6f, 0.6f));
        UIFactory.StackTop(scoreSummary.rectTransform, ref y, 20f);
        view.scoreSummary = scoreSummary;
        y -= 6f;

        var rows = UIFactory.CreateRect("PerformanceRows", content);
        UIFactory.StackTop(rows, ref y, 24f * 7f);
        view.performanceRowsContainer = rows;
        y -= 6f;

        var falls = UIFactory.CreateText("Falls", content, "", 13, Color.white);
        UIFactory.StackTop(falls.rectTransform, ref y, 20f);
        view.fallsText = falls;

        var noFallBonus = UIFactory.CreateText("NoFallBonus", content, "", 13, new Color(1f, 0.85f, 0.2f));
        UIFactory.StackTop(noFallBonus.rectTransform, ref y, 20f);
        view.noFallBonusText = noFallBonus;

        var session = UIFactory.CreateText("Session", content, "", 11, new Color(0.55f, 0.55f, 0.6f));
        UIFactory.StackTop(session.rectTransform, ref y, 22f);
        view.sessionText = session;

        y -= 10f;
        float btnW = 150f, btnH = 40f, gap = 16f;
        var restartBtn = UIFactory.CreateButton("RestartButton", content, Loc.Get("EndScreen.Restart"), out var restartLabel);
        UIFactory.SetBox(restartBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-(btnW + gap) / 2f, y), new Vector2(btnW, btnH));
        view.restartButton      = restartBtn;
        view.restartButtonLabel = restartLabel;

        var continueBtn = UIFactory.CreateButton("ContinueButton", content, Loc.Get("EndScreen.Continue"), out var continueLabel);
        UIFactory.SetBox(continueBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2((btnW + gap) / 2f, y), new Vector2(btnW, btnH));
        view.continueButton      = continueBtn;
        view.continueButtonLabel = continueLabel;

        return root.gameObject;
    }

    // ── Pause menu ────────────────────────────────────────────────────────────

    private static GameObject BuildPauseMenu(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("PauseMenu", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<PauseView>();

        var pauseBtn = UIFactory.CreateButton("PauseButton", root, Loc.Get("Pause.PauseButton"), out var pauseLabel);
        UIFactory.SetBox(pauseBtn.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-12f, -12f), new Vector2(84f, 28f));
        view.pauseButtonRoot  = pauseBtn.gameObject;
        view.pauseButton      = pauseBtn;
        view.pauseButtonLabel = pauseLabel;

        var overlay = UIFactory.CreateRect("PausedOverlay", root);
        UIFactory.Stretch(overlay);
        view.overlayRoot = overlay.gameObject;

        var dim = UIFactory.CreatePanel("Dim", overlay, new Color(0f, 0f, 0f, 0.6f));
        UIFactory.Stretch(dim.rectTransform);

        float pw = 240f, ph = 170f;
        var panel = UIFactory.CreatePanel("Panel", overlay, new Color(0.04f, 0.04f, 0.04f, 0.97f));
        UIFactory.SetBox(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(pw, ph));
        var border = panel.gameObject.AddComponent<Outline>();
        border.effectColor    = new Color(0.15f, 0.15f, 0.15f, 1f);
        border.effectDistance = new Vector2(3f, -3f);

        var content = panel.rectTransform;
        float y = -14f;

        var title = UIFactory.CreateText("Title", content, Loc.Get("Pause.Title"), 18, Color.white);
        UIFactory.StackTop(title.rectTransform, ref y, 28f, 0f);
        view.titleText = title;

        y = -60f;
        float btnW = pw - 40f, btnH = 34f;
        var resumeBtn = UIFactory.CreateButton("ResumeButton", content, Loc.Get("Pause.Resume"), out var resumeLabel);
        UIFactory.SetBox(resumeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        view.resumeButton      = resumeBtn;
        view.resumeButtonLabel = resumeLabel;
        y -= btnH + 12f;

        var restartBtn = UIFactory.CreateButton("RestartButton", content, Loc.Get("Pause.RestartSong"), out var restartLabel);
        UIFactory.SetBox(restartBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        view.restartButton      = restartBtn;
        view.restartButtonLabel = restartLabel;

        return root.gameObject;
    }
}
#endif
