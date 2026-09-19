#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot Editor tool: builds all fifteen runtime UI screens — the three gameplay prefabs
/// (LiveHud, EndScreen, PauseMenu), the seven Frontend/Fight screens (IntroScreen, MainMenu,
/// SongSelection, AnalyzingScreen, CountdownScreen, FinishBanner, FightHud), and the five Fight-flow
/// screens (OpponentSelection, VersusScreen, RoundIntro, RoundEnd, MatchResult, NextSongTransition) — using the exact
/// same UIFactory calls each screen's own controller used to run at PLAY time (with the same
/// ThemeColorReceiver/ThemeTextReceiver wiring, so the saved prefabs come out theme-ready too),
/// saves them as real .prefab assets under Assets/_Project/Prefabs/UI/, leaves connected instances
/// in the currently open scene, and wires a UIRegistry component to them.
///
/// Run it ONCE via Tools > MusicGame > Build UI Prefabs. After that, every screen controller finds
/// the UIRegistry at startup and just READS the matching prefab instance instead of building
/// anything itself — open the .prefab assets in the Prefab editor any time afterward to retouch
/// colors/fonts/layout by hand; the game keeps working as long as the wired fields on each *View
/// component still point at the right children. Song Selection and Opponent Selection are the two
/// partial exceptions: only their static chrome is baked — the catalog rows / opponent grid cells
/// stay dynamic runtime population either way.
///
/// Safe to re-run: it rebuilds all fifteen prefabs + the scene instances + UIRegistry from scratch
/// each time (so hand-made edits to the PREFAB ASSETS themselves are NOT preserved by re-running
/// this — it's meant to be run once to get a starting point you then hand-tune).
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

        var introScreen     = BuildIntroScreen(canvasRect);
        var mainMenu        = BuildMainMenu(canvasRect);
        var songSelection   = BuildSongSelection(canvasRect);
        var analyzingScreen = BuildAnalyzingScreen(canvasRect);
        var countdownScreen = BuildCountdownScreen(canvasRect);
        var finishBanner    = BuildFinishBanner(canvasRect);
        var fightHud        = BuildFightHud(canvasRect);

        var opponentSelection = BuildOpponentSelection(canvasRect);
        var versusScreen      = BuildVersusScreen(canvasRect);
        var roundIntro        = BuildRoundIntro(canvasRect);
        var roundEnd          = BuildRoundEnd(canvasRect);
        var matchResult       = BuildMatchResult(canvasRect);
        var nextSongTransition = BuildNextSongTransition(canvasRect);

        // IMPORTANT: SaveAsPrefabAssetAndConnect returns the PREFAB ASSET (disk-only, never
        // instantiated into the running scene) — it also CONVERTS the passed scene GameObject
        // into a connected prefab instance, but that instance is a DIFFERENT object than the
        // returned one. UIRegistry must be wired to the ORIGINAL scene variables below, NOT to the
        // return value of SaveAndConnect — wiring to the asset silently does nothing at runtime (no
        // exception, the component just isn't part of any live scene, so Update()/onClick never
        // reach it).
        SaveAndConnect(liveHud,   "LiveHud.prefab");
        SaveAndConnect(endScreen, "EndScreen.prefab");
        SaveAndConnect(pauseMenu, "PauseMenu.prefab");
        SaveAndConnect(introScreen,     "IntroScreen.prefab");
        SaveAndConnect(mainMenu,        "MainMenu.prefab");
        SaveAndConnect(songSelection,   "SongSelection.prefab");
        SaveAndConnect(analyzingScreen, "AnalyzingScreen.prefab");
        SaveAndConnect(countdownScreen, "CountdownScreen.prefab");
        SaveAndConnect(finishBanner,    "FinishBanner.prefab");
        SaveAndConnect(fightHud,        "FightHud.prefab");
        SaveAndConnect(opponentSelection, "OpponentSelection.prefab");
        SaveAndConnect(versusScreen,      "VersusScreen.prefab");
        SaveAndConnect(roundIntro,        "RoundIntro.prefab");
        SaveAndConnect(roundEnd,          "RoundEnd.prefab");
        SaveAndConnect(matchResult,       "MatchResult.prefab");
        SaveAndConnect(nextSongTransition, "NextSongTransition.prefab");

        WireRegistry(
            liveHud.GetComponent<LiveHudView>(),
            endScreen.GetComponent<EndScreenView>(),
            pauseMenu.GetComponent<PauseView>(),
            introScreen.GetComponent<IntroScreenView>(),
            mainMenu.GetComponent<MainMenuView>(),
            songSelection.GetComponent<SongSelectionView>(),
            analyzingScreen.GetComponent<AnalyzingScreenView>(),
            countdownScreen.GetComponent<CountdownScreenView>(),
            finishBanner.GetComponent<FinishBannerView>(),
            fightHud.GetComponent<FightHudView>(),
            opponentSelection.GetComponent<OpponentSelectionView>(),
            versusScreen.GetComponent<VersusScreenView>(),
            roundIntro.GetComponent<RoundIntroView>(),
            roundEnd.GetComponent<RoundEndView>(),
            matchResult.GetComponent<MatchResultView>(),
            nextSongTransition.GetComponent<NextSongTransitionView>());

        // These screens are ALWAYS-ACTIVE at runtime until their own Show()/Hide() toggles them
        // (Awake() flips them off) — but starting hidden in the EDITED scene, saved into the
        // prefab as its default state, keeps the Editor's Scene view uncluttered and matches how
        // they'll actually look the instant Play begins.
        introScreen.SetActive(false);
        mainMenu.SetActive(false);
        songSelection.SetActive(false);
        analyzingScreen.SetActive(false);
        countdownScreen.SetActive(false);
        finishBanner.SetActive(false);
        fightHud.SetActive(false);
        opponentSelection.SetActive(false);
        versusScreen.SetActive(false);
        roundIntro.SetActive(false);
        roundEnd.SetActive(false);
        matchResult.SetActive(false);
        nextSongTransition.SetActive(false);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[UIPrefabBuilder] Done — 16 UI prefabs saved under " +
                  $"{FolderPath}, instances wired into UIRegistry in the scene. " +
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

    private static void WireRegistry(LiveHudView liveHud, EndScreenView endScreen, PauseView pause,
        IntroScreenView introScreen, MainMenuView mainMenu, SongSelectionView songSelection,
        AnalyzingScreenView analyzing, CountdownScreenView countdown, FinishBannerView finishBanner,
        FightHudView fightHud, OpponentSelectionView opponentSelection, VersusScreenView versusScreen,
        RoundIntroView roundIntro, RoundEndView roundEnd, MatchResultView matchResult,
        NextSongTransitionView nextSongTransition)
    {
        var registryGO = GameObject.Find("[UI Registry]");
        if (registryGO == null) registryGO = new GameObject("[UI Registry]");
        var registry = registryGO.GetComponent<UIRegistry>() ?? registryGO.AddComponent<UIRegistry>();

        var so = new SerializedObject(registry);
        so.FindProperty("liveHud").objectReferenceValue   = liveHud;
        so.FindProperty("endScreen").objectReferenceValue = endScreen;
        so.FindProperty("pause").objectReferenceValue      = pause;
        so.FindProperty("introScreen").objectReferenceValue   = introScreen;
        so.FindProperty("mainMenu").objectReferenceValue      = mainMenu;
        so.FindProperty("songSelection").objectReferenceValue = songSelection;
        so.FindProperty("analyzing").objectReferenceValue     = analyzing;
        so.FindProperty("countdown").objectReferenceValue     = countdown;
        so.FindProperty("finishBanner").objectReferenceValue  = finishBanner;
        so.FindProperty("fightHud").objectReferenceValue      = fightHud;
        so.FindProperty("opponentSelection").objectReferenceValue = opponentSelection;
        so.FindProperty("versusScreen").objectReferenceValue      = versusScreen;
        so.FindProperty("roundIntro").objectReferenceValue        = roundIntro;
        so.FindProperty("roundEnd").objectReferenceValue          = roundEnd;
        so.FindProperty("matchResult").objectReferenceValue       = matchResult;
        so.FindProperty("nextSongTransition").objectReferenceValue = nextSongTransition;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── Live HUD ──────────────────────────────────────────────────────────────

    private static GameObject BuildLiveHud(RectTransform canvas, MusicRunnerCollectiblesConfig config)
    {
        var root = UIFactory.CreateRect("LiveHUD", canvas);
        UIFactory.SetBox(root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 100f));
        var view = root.gameObject.AddComponent<LiveHudView>();

        var cells = new (string labelKey, RingType? type, System.Action<TextMeshProUGUI> assignValue, System.Action<TextMeshProUGUI> assignLabel)[]
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
        topBar.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        for (int i = 0; i < cells.Length; i++)
        {
            var cell = UIFactory.CreateRect($"Cell_{cells[i].labelKey}", topBar.rectTransform);
            float xMin = i / 8f, xMax = (i + 1) / 8f;
            UIFactory.SetBox(cell, new Vector2(xMin, 0f), new Vector2(xMax, 1f), new Vector2(0f, 1f), new Vector2(6f, 0f), new Vector2(-6f, 0f));

            Color labelColor = cells[i].type.HasValue && config != null ? config.RingColor(cells[i].type.Value) : Color.white;
            var label = UIFactory.CreateText("Label", cell, Loc.Get(cells[i].labelKey), 12, labelColor, TextAlignmentOptions.TopLeft);
            UIFactory.SetBox(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -3f), new Vector2(0f, 20f));
            cells[i].assignLabel(label);

            var value = UIFactory.CreateText("Value", cell, "0", 14, Color.white, TextAlignmentOptions.TopLeft);
            UIFactory.SetBox(value.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -22f), new Vector2(0f, 20f));

            // Score/Total (type == null) are Theme-driven text; the per-RingType cells are colored
            // from MusicRunnerCollectiblesConfig instead — a separate, non-Theme color system.
            if (!cells[i].type.HasValue)
                value.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

            cells[i].assignValue(value);
        }

        var progressBg = UIFactory.CreateFillBar("SongProgress", root, new Color(0.10f, 0.10f, 0.10f), new Color(0.18f, 0.75f, 0.95f), out var progressFill);
        UIFactory.SetBox(progressBg.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -45f), new Vector2(0f, 4f));
        view.progressFill = progressFill;
        progressFill.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Accent);

        var tagsStrip = UIFactory.CreateRect("TagsStrip", root);
        UIFactory.SetBox(tagsStrip, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(0f, 54f));
        tagsStrip.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.03f, 0.03f, 0.75f);
        view.tagsStrip = tagsStrip.gameObject;
        tagsStrip.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        // Style tag keeps its own hardcoded tint — a data-category indicator, not UI chrome.
        var tagStyle = UIFactory.CreateText("Style", tagsStrip, "", 11, new Color(0.55f, 0.8f, 1f), TextAlignmentOptions.TopLeft);
        UIFactory.SetBox(tagStyle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(8f, -3f), new Vector2(-8f, 16f));
        view.tagStyle = tagStyle;

        var tagVibe = UIFactory.CreateText("Vibe", tagsStrip, "", 11, Color.white, TextAlignmentOptions.TopLeft);
        UIFactory.SetBox(tagVibe.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(8f, -19f), new Vector2(-8f, 16f));
        view.tagVibe = tagVibe;
        tagVibe.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        var tagOther = UIFactory.CreateText("Other", tagsStrip, "", 11, Color.white, TextAlignmentOptions.TopLeft);
        UIFactory.SetBox(tagOther.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(8f, -35f), new Vector2(-8f, 16f));
        view.tagOther = tagOther;
        tagOther.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

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
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        float pw = 420f, ph = 720f;
        var panel = UIFactory.CreatePanel("Panel", root, new Color(0.04f, 0.04f, 0.04f, 0.97f));
        UIFactory.SetBox(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(pw, ph));
        var border = panel.gameObject.AddComponent<Outline>();
        border.effectColor    = new Color(0.2f, 0.2f, 0.2f, 1f);
        border.effectDistance = new Vector2(2f, -2f);
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        var content = panel.rectTransform;
        float y = -16f;

        var title = UIFactory.CreateText("Title", content, Loc.Get("EndScreen.Title"), 20, Color.white);
        UIFactory.StackTop(title.rectTransform, ref y, 30f);
        view.titleText = title;
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        // Rating label/bar are deliberately NOT theme receivers — PopulateEndScreen colors them
        // from the SCORE (a red-to-green gradient), not from the Theme.
        var rating = UIFactory.CreateText("Rating", content, "", 22, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.StackTop(rating.rectTransform, ref y, 32f);
        view.ratingLabel = rating;

        var ratingBar = UIFactory.CreateFillBar("RatingBar", content, new Color(0.12f, 0.12f, 0.12f), Color.white, out var ratingFill);
        UIFactory.StackTop(ratingBar.rectTransform, ref y, 16f, 46f);
        view.ratingBarFill = ratingFill;
        y -= 6f;

        var scoreSummary = UIFactory.CreateText("ScoreSummary", content, "", 12, new Color(0.6f, 0.6f, 0.6f));
        UIFactory.StackTop(scoreSummary.rectTransform, ref y, 20f);
        view.scoreSummary = scoreSummary;
        scoreSummary.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        y -= 6f;

        var rows = UIFactory.CreateRect("PerformanceRows", content);
        UIFactory.StackTop(rows, ref y, 24f * 7f);
        view.performanceRowsContainer = rows;
        y -= 6f;

        var falls = UIFactory.CreateText("Falls", content, "", 13, Color.white);
        UIFactory.StackTop(falls.rectTransform, ref y, 20f);
        view.fallsText = falls;
        falls.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        var noFallBonus = UIFactory.CreateText("NoFallBonus", content, "", 13, new Color(1f, 0.85f, 0.2f));
        UIFactory.StackTop(noFallBonus.rectTransform, ref y, 20f);
        view.noFallBonusText = noFallBonus;
        noFallBonus.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Positive, UIFontToken.Body);

        var session = UIFactory.CreateText("Session", content, "", 11, new Color(0.55f, 0.55f, 0.6f));
        UIFactory.StackTop(session.rectTransform, ref y, 22f);
        view.sessionText = session;
        session.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        y -= 10f;
        float btnW = 150f, btnH = 40f, gap = 16f;
        var restartBtn = UIFactory.CreateButton("RestartButton", content, Loc.Get("EndScreen.Restart"), out var restartLabel);
        UIFactory.SetBox(restartBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-(btnW + gap) / 2f, y), new Vector2(btnW, btnH));
        view.restartButton      = restartBtn;
        view.restartButtonLabel = restartLabel;
        restartBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        restartLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        var continueBtn = UIFactory.CreateButton("ContinueButton", content, Loc.Get("EndScreen.Continue"), out var continueLabel);
        UIFactory.SetBox(continueBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2((btnW + gap) / 2f, y), new Vector2(btnW, btnH));
        view.continueButton      = continueBtn;
        view.continueButtonLabel = continueLabel;
        continueBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        continueLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);

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
        pauseBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        pauseLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        var overlay = UIFactory.CreateRect("PausedOverlay", root);
        UIFactory.Stretch(overlay);
        view.overlayRoot = overlay.gameObject;

        var dim = UIFactory.CreatePanel("Dim", overlay, new Color(0f, 0f, 0f, 0.6f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        float pw = 240f, ph = 216f; // +46 over the original 2-button height, room for MainMenu below Restart
        var panel = UIFactory.CreatePanel("Panel", overlay, new Color(0.04f, 0.04f, 0.04f, 0.97f));
        UIFactory.SetBox(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(pw, ph));
        var border = panel.gameObject.AddComponent<Outline>();
        border.effectColor    = new Color(0.15f, 0.15f, 0.15f, 1f);
        border.effectDistance = new Vector2(3f, -3f);
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        var content = panel.rectTransform;
        float y = -14f;

        var title = UIFactory.CreateText("Title", content, Loc.Get("Pause.Title"), 18, Color.white);
        UIFactory.StackTop(title.rectTransform, ref y, 28f, 0f);
        view.titleText = title;
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        y = -60f;
        float btnW = pw - 40f, btnH = 34f;
        var resumeBtn = UIFactory.CreateButton("ResumeButton", content, Loc.Get("Pause.Resume"), out var resumeLabel);
        UIFactory.SetBox(resumeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        view.resumeButton      = resumeBtn;
        view.resumeButtonLabel = resumeLabel;
        resumeBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        resumeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        y -= btnH + 12f;

        var restartBtn = UIFactory.CreateButton("RestartButton", content, Loc.Get("Pause.RestartSong"), out var restartLabel);
        UIFactory.SetBox(restartBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        view.restartButton      = restartBtn;
        view.restartButtonLabel = restartLabel;
        restartBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        restartLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        y -= btnH + 12f;

        var mainMenuBtn = UIFactory.CreateButton("MainMenuButton", content, Loc.Get("Pause.MainMenu"), out var mainMenuLabel);
        UIFactory.SetBox(mainMenuBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        view.mainMenuButton      = mainMenuBtn;
        view.mainMenuButtonLabel = mainMenuLabel;
        mainMenuBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        mainMenuLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        return root.gameObject;
    }

    // ── Intro screen ──────────────────────────────────────────────────────────

    private static GameObject BuildIntroScreen(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("IntroScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<IntroScreenView>();
        view.root = root.gameObject;

        var canvasGroup = root.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        view.canvasGroup = canvasGroup;

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var skipButton = dim.gameObject.AddComponent<Button>();
        skipButton.transition = Selectable.Transition.None;
        view.skipButton = skipButton;

        var title = UIFactory.CreateText("Title", root, Loc.Get("Intro.Title"), 42, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(1000f, 80f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.titleText = title;

        return root.gameObject;
    }

    // ── Main menu ─────────────────────────────────────────────────────────────

    private static GameObject BuildMainMenu(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("MainMenuScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<MainMenuView>();
        view.root = root.gameObject;

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var title = UIFactory.CreateText("Title", root, Loc.Get("MainMenu.Title"), 36, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -100f), new Vector2(900f, 60f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.titleText = title;

        var profileIcon = UIFactory.CreatePanel("ProfileIcon", root, Color.gray);
        UIFactory.SetBox(profileIcon.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -24f), new Vector2(48f, 48f));
        profileIcon.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Secondary);

        float btnW = 260f, btnH = 54f, gap = 18f;
        float y = -20f;

        var playBtn = UIFactory.CreateButton("PlayButton", root, Loc.Get("MainMenu.Play"), out var playLabel);
        UIFactory.SetBox(playBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        playBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        playLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        view.playButton      = playBtn;
        view.playButtonLabel = playLabel;
        y -= btnH + gap;

        var settingsBtn = UIFactory.CreateButton("SettingsButton", root, Loc.Get("MainMenu.Settings"), out var settingsLabel);
        UIFactory.SetBox(settingsBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        settingsBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        settingsLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        view.settingsButton      = settingsBtn;
        view.settingsButtonLabel = settingsLabel;
        y -= btnH + gap;

        var quitBtn = UIFactory.CreateButton("QuitButton", root, Loc.Get("MainMenu.Quit"), out var quitLabel);
        UIFactory.SetBox(quitBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        quitBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        quitLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        view.quitButton      = quitBtn;
        view.quitButtonLabel = quitLabel;

        var settingsPanel = UIFactory.CreateRect("SettingsPanel", root);
        UIFactory.SetBox(settingsPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(420f, 220f));
        var panelBg = settingsPanel.gameObject.AddComponent<Image>();
        panelBg.color = new Color(0.05f, 0.05f, 0.05f, 0.97f);
        settingsPanel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);
        view.settingsPanel = settingsPanel.gameObject;

        var settingsTitle = UIFactory.CreateText("Title", settingsPanel, Loc.Get("Settings.Title"), 20, Color.white);
        UIFactory.SetBox(settingsTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -16f), new Vector2(380f, 30f));
        settingsTitle.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);
        view.settingsTitleText = settingsTitle;

        var volumeLabel = UIFactory.CreateText("VolumeLabel", settingsPanel, Loc.Get("Settings.Volume"), 14, new Color(0.8f, 0.8f, 0.8f), TextAlignmentOptions.Left);
        UIFactory.SetBox(volumeLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 20f), new Vector2(360f, 24f));
        volumeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        view.volumeLabelText = volumeLabel;

        var volumeSlider = UIFactory.CreateSlider("VolumeSlider", settingsPanel, 1f, new Color(0.18f, 0.75f, 0.95f));
        UIFactory.SetBox(volumeSlider.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -15f), new Vector2(360f, 16f));
        volumeSlider.fillRect.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Accent);
        view.volumeSlider = volumeSlider;

        var closeBtn = UIFactory.CreateButton("CloseButton", settingsPanel, Loc.Get("Settings.Close"), out var closeLabel);
        UIFactory.SetBox(closeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 20f), new Vector2(140f, 40f));
        closeBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        closeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        view.closeButton      = closeBtn;
        view.closeButtonLabel = closeLabel;

        settingsPanel.gameObject.SetActive(false);
        return root.gameObject;
    }

    // ── Song selection ────────────────────────────────────────────────────────

    // Only the STATIC chrome is baked — catalog rows stay dynamic runtime population into the
    // (empty) songListRoot container either way (Section 9 of the plan).
    private static GameObject BuildSongSelection(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("SongSelectionScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<SongSelectionView>();
        view.root = root.gameObject;

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var title = UIFactory.CreateText("Title", root, Loc.Get("SongSelection.Title"), 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -50f), new Vector2(900f, 50f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.titleText = title;

        var songListScroll = UIFactory.CreateScrollRect("SongList", root, out var songListRoot, 10f);
        UIFactory.SetBox(songListScroll.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -120f), new Vector2(560f, 400f));
        view.songListRoot = songListRoot;

        // Standalone button right below the list — NOT one of its rows (see SongSelectionView's
        // own doc).
        var playYourSongBtn = UIFactory.CreateButton("PlayYourSongButton", root, Loc.Get("SongSelection.PlayYourSong"), out var playYourSongLabel);
        UIFactory.SetBox(playYourSongBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -530f), new Vector2(560f, 50f));
        playYourSongLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        view.playYourSongButton = playYourSongBtn; view.playYourSongLabel = playYourSongLabel;

        var streamingRow = UIFactory.CreateRect("StreamingRow", root);
        UIFactory.SetBox(streamingRow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 150f), new Vector2(560f, 44f));

        string[] keys = { "SongSelection.Spotify", "SongSelection.YouTubeMusic", "SongSelection.AmazonMusic" };
        float w = (560f - 2f * 10f) / 3f;
        Button[] streamingButtons = new Button[3];
        TextMeshProUGUI[] streamingLabels = new TextMeshProUGUI[3];
        for (int i = 0; i < keys.Length; i++)
        {
            var btn = UIFactory.CreateButton("Streaming_" + keys[i], streamingRow, Loc.Get(keys[i]) + " (" + Loc.Get("SongSelection.ComingSoon") + ")", out var label);
            label.fontSize = 11;
            UIFactory.SetBox(btn.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(i * (w + 10f), 0f), new Vector2(w, 0f));
            btn.interactable = false;
            btn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
            label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
            streamingButtons[i] = btn;
            streamingLabels[i]  = label;
        }
        view.spotifyButton = streamingButtons[0]; view.spotifyLabel = streamingLabels[0];
        view.youtubeMusicButton = streamingButtons[1]; view.youtubeMusicLabel = streamingLabels[1];
        view.amazonMusicButton = streamingButtons[2]; view.amazonMusicLabel = streamingLabels[2];

        var backBtn = UIFactory.CreateButton("BackButton", root, Loc.Get("SongSelection.Back"), out var backLabel);
        UIFactory.SetBox(backBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-150f, 40f), new Vector2(180f, 44f));
        backBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        backLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        view.backButton = backBtn; view.backButtonLabel = backLabel;

        var playBtn = UIFactory.CreateButton("PlayButton", root, Loc.Get("SongSelection.Play"), out var playLabel);
        UIFactory.SetBox(playBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(150f, 40f), new Vector2(180f, 44f));
        playBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        playLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        view.playButton = playBtn; view.playButtonLabel = playLabel;

        var statusText = UIFactory.CreateText("Status", root, "", 12, new Color(0.75f, 0.75f, 0.75f));
        UIFactory.SetBox(statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 90f), new Vector2(500f, 24f));
        view.statusText = statusText;

        return root.gameObject;
    }

    // ── Analyzing screen ──────────────────────────────────────────────────────

    private static GameObject BuildAnalyzingScreen(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("AnalyzingScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<AnalyzingScreenView>();
        view.root = root.gameObject;

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 0.96f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var title = UIFactory.CreateText("Title", root, Loc.Get("Analyzing.Title"), 26, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 40f), new Vector2(900f, 40f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.titleText = title;

        var tip = UIFactory.CreateText("Tip", root, "", 16, new Color(0.75f, 0.75f, 0.8f), TextAlignmentOptions.Center);
        UIFactory.SetBox(tip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -10f), new Vector2(900f, 30f));
        tip.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Secondary, UIFontToken.Body);
        view.tipText = tip;

        var barBg = UIFactory.CreateFillBar("Progress", root, new Color(1f, 1f, 1f, 0.12f), new Color(0.18f, 0.75f, 0.95f), out var progressFill);
        UIFactory.SetBox(barBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -60f), new Vector2(500f, 6f));
        progressFill.fillAmount = 0f;
        progressFill.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Accent);
        view.progressFill = progressFill;

        return root.gameObject;
    }

    // ── Countdown screen ──────────────────────────────────────────────────────

    private static GameObject BuildCountdownScreen(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("CountdownScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<CountdownScreenView>();
        view.root = root.gameObject;

        var numberText = UIFactory.CreateText("Number", root, "", 96, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(numberText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(400f, 200f));
        numberText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
        view.numberText = numberText;

        return root.gameObject;
    }

    // ── Finish banner ─────────────────────────────────────────────────────────

    private static GameObject BuildFinishBanner(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("FinishBanner", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<FinishBannerView>();
        view.root = root.gameObject;

        var text = UIFactory.CreateText("Text", root, Loc.Get("Countdown.Finish"), 96, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(700f, 200f));
        text.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
        view.text = text;

        return root.gameObject;
    }

    // ── Fight HUD ─────────────────────────────────────────────────────────────

    private static GameObject BuildFightHud(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("FightScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<FightHudView>();
        view.root = root.gameObject;

        var topBar = UIFactory.CreatePanel("TopBar", root, new Color(0.02f, 0.02f, 0.05f, 0.55f));
        UIFactory.SetBox(topBar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 90f));
        topBar.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        var playerName = UIFactory.CreateText("PlayerName", topBar.rectTransform, Loc.Get("Fight.PlayerName"), 18, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        UIFactory.SetBox(playerName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(20f, -12f), new Vector2(320f, 24f));
        playerName.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);
        view.playerNameText = playerName;

        var playerHealthBg = UIFactory.CreateFillBar("PlayerHealth", topBar.rectTransform, new Color(0.12f, 0.12f, 0.12f), new Color(0.3f, 0.85f, 0.3f), out var playerHealthFill);
        UIFactory.SetBox(playerHealthBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(20f, -42f), new Vector2(320f, 18f));
        playerHealthFill.fillAmount = 1f;
        playerHealthFill.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Positive);
        view.playerHealthFill = playerHealthFill;

        var playerRoundPips = UIFactory.CreateText("PlayerRoundPips", topBar.rectTransform, "", 16, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        UIFactory.SetBox(playerRoundPips.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(20f, -64f), new Vector2(320f, 20f));
        playerRoundPips.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        view.playerRoundPipsText = playerRoundPips;

        var opponentName = UIFactory.CreateText("OpponentName", topBar.rectTransform, Loc.Get("Fight.RivalUnknown"), 18, Color.white, TextAlignmentOptions.TopRight, FontStyles.Bold);
        UIFactory.SetBox(opponentName.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-90f, -12f), new Vector2(320f, 24f));
        opponentName.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);
        view.opponentNameText = opponentName;

        var opponentHealthBg = UIFactory.CreateFillBar("OpponentHealth", topBar.rectTransform, new Color(0.12f, 0.12f, 0.12f), new Color(0.9f, 0.3f, 0.25f), out var opponentHealthFill);
        UIFactory.SetBox(opponentHealthBg.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-90f, -42f), new Vector2(320f, 18f));
        opponentHealthFill.fillAmount = 1f;
        opponentHealthFill.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Negative);
        opponentHealthFill.fillOrigin = (int)Image.OriginHorizontal.Right;
        view.opponentHealthFill = opponentHealthFill;

        var opponentRoundPips = UIFactory.CreateText("OpponentRoundPips", topBar.rectTransform, "", 16, Color.white, TextAlignmentOptions.TopRight, FontStyles.Bold);
        UIFactory.SetBox(opponentRoundPips.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-90f, -64f), new Vector2(320f, 20f));
        opponentRoundPips.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        view.opponentRoundPipsText = opponentRoundPips;

        var timer = UIFactory.CreateText("Timer", topBar.rectTransform, "", 28, Color.white, TextAlignmentOptions.Top, FontStyles.Bold);
        UIFactory.SetBox(timer.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -12f), new Vector2(140f, 36f));
        timer.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.timerText = timer;

        var pauseBtn = UIFactory.CreateButton("PauseButton", topBar.rectTransform, Loc.Get("Fight.Pause"), out var pauseLabel);
        pauseLabel.fontSize = 12;
        UIFactory.SetBox(pauseBtn.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-15f, -15f), new Vector2(60f, 60f));
        pauseBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        pauseLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        view.pauseButton = pauseBtn; view.pauseButtonLabel = pauseLabel;

        var pausePanel = UIFactory.CreateRect("PauseMenu", root);
        UIFactory.Stretch(pausePanel);
        view.pausePanel = pausePanel.gameObject;

        var dim = UIFactory.CreatePanel("Dim", pausePanel, new Color(0f, 0f, 0f, 0.75f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var pauseTitle = UIFactory.CreateText("Title", pausePanel, Loc.Get("Fight.Paused"), 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(pauseTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 60f), new Vector2(400f, 50f));
        pauseTitle.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.pauseTitleText = pauseTitle;

        var resumeBtn = UIFactory.CreateButton("ResumeButton", pausePanel, Loc.Get("Fight.Resume"), out var resumeLabel);
        UIFactory.SetBox(resumeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -10f), new Vector2(220f, 48f));
        resumeBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        resumeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        view.resumeButton = resumeBtn; view.resumeButtonLabel = resumeLabel;

        var mainMenuBtn = UIFactory.CreateButton("MainMenuButton", pausePanel, Loc.Get("Fight.MainMenu"), out var mainMenuLabel);
        UIFactory.SetBox(mainMenuBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -70f), new Vector2(220f, 48f));
        mainMenuBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        mainMenuLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        view.mainMenuButton = mainMenuBtn; view.mainMenuButtonLabel = mainMenuLabel;

        pausePanel.gameObject.SetActive(false);

        // Joystick (movement) + Punch/Kick — visible ONLY during FightFlowState.Fighting, on
        // real/simulated mobile (see FightController.ActivateHud). Reuses VirtualJoystick/a fresh
        // TouchActionButton per instance, wired to FightTouchInputState — NOT Runner's own
        // TouchInputState (see VirtualJoystick's own doc on why this is "reuse the component, wire
        // a new instance" rather than duplicating the drag math or driving Runner's static from Fight).
        var mobileControls = UIFactory.CreateRect("MobileControls", root);
        UIFactory.Stretch(mobileControls);
        view.mobileControlsRoot = mobileControls.gameObject;

        const float joySize = 180f;
        var joystickBg = UIFactory.CreatePanel("JoystickBackground", mobileControls, new Color(1f, 1f, 1f, 0.15f));
        UIFactory.SetBox(joystickBg.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(40f, 40f), new Vector2(joySize, joySize));
        var joystickHandle = UIFactory.CreatePanel("JoystickHandle", joystickBg.rectTransform, new Color(1f, 1f, 1f, 0.4f));
        joystickHandle.rectTransform.sizeDelta        = new Vector2(joySize * 0.45f, joySize * 0.45f);
        joystickHandle.rectTransform.anchoredPosition = Vector2.zero;
        view.joystickBackground = joystickBg.rectTransform;
        view.joystickHandle     = joystickHandle.rectTransform;
        var joystick = joystickBg.gameObject.AddComponent<VirtualJoystick>();
        joystick.Initialize(joystickBg.rectTransform, joystickHandle.rectTransform,
            v => { FightTouchInputState.Horizontal = v.x; FightTouchInputState.Vertical = v.y; },
            driveTouchInputState: false);

        const float actionSize = 110f;
        var punchBtn = UIFactory.CreatePanel("PunchButton", mobileControls, new Color(1f, 1f, 1f, 0.25f));
        UIFactory.SetBox(punchBtn.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-170f, 40f), new Vector2(actionSize, actionSize));
        var punchLabel = UIFactory.CreateText("Label", punchBtn.rectTransform, Loc.Get("Mobile.Punch"), 16, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.Stretch(punchLabel.rectTransform);
        punchBtn.gameObject.AddComponent<TouchActionButton>().Initialize(() => FightTouchInputState.PunchRequested = true);
        view.punchButton = punchBtn.gameObject;
        view.punchButtonLabel = punchLabel;

        var kickBtn = UIFactory.CreatePanel("KickButton", mobileControls, new Color(1f, 1f, 1f, 0.25f));
        UIFactory.SetBox(kickBtn.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-40f, 40f), new Vector2(actionSize, actionSize));
        var kickLabel = UIFactory.CreateText("Label", kickBtn.rectTransform, Loc.Get("Mobile.Kick"), 16, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.Stretch(kickLabel.rectTransform);
        kickBtn.gameObject.AddComponent<TouchActionButton>().Initialize(() => FightTouchInputState.KickRequested = true);
        view.kickButton = kickBtn.gameObject;
        view.kickButtonLabel = kickLabel;

        mobileControls.gameObject.SetActive(false);

        return root.gameObject;
    }

    // ── Opponent Selection ────────────────────────────────────────────────────

    private static GameObject BuildOpponentSelection(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("OpponentSelectionScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<OpponentSelectionView>();
        view.root = root.gameObject;

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var title = UIFactory.CreateText("Title", root, Loc.Get("OpponentSelection.Title"), 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -50f), new Vector2(900f, 50f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.titleText = title;

        // Cells themselves stay dynamic runtime population (OpponentSelectionController, one per
        // OpponentRosterSO entry) — only the empty grid container + its GridLayoutGroup are baked.
        // FixedColumnCount=4 gives today's 8-opponent roster its 2x4 layout, but adding/removing
        // opponents just reflows the same grid — nothing here assumes exactly 8.
        var grid = UIFactory.CreateRect("Grid", root);
        UIFactory.SetBox(grid, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2((180f + 16f) * 4f, (220f + 16f) * 2f));
        var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize        = new Vector2(180f, 220f);
        layout.spacing         = new Vector2(16f, 16f);
        layout.childAlignment  = TextAnchor.MiddleCenter;
        layout.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 4;
        view.gridRoot = grid;

        return root.gameObject;
    }

    // ── Versus Screen ─────────────────────────────────────────────────────────

    private static GameObject BuildVersusScreen(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("VersusScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<VersusScreenView>();
        view.root = root.gameObject;

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        (view.playerPortrait, view.playerNameText)     = BuildVersusSide(root, "Player",   new Vector2(0.25f, 0.5f));
        (view.opponentPortrait, view.opponentNameText) = BuildVersusSide(root, "Opponent", new Vector2(0.75f, 0.5f));

        var vsText = UIFactory.CreateText("VS", root, Loc.Get("Fight.Versus"), 72, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(vsText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 150f));
        vsText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
        view.vsText = vsText;

        return root.gameObject;
    }

    // anchor.x picks left/right half of the screen; portrait sits above its name, both centered
    // on that anchor — same layout VersusScreenController's own procedural fallback uses.
    private static (Image portrait, TextMeshProUGUI nameText) BuildVersusSide(RectTransform root, string label, Vector2 anchor)
    {
        var portraitRt = UIFactory.CreateRect(label + "Portrait", root);
        var portrait = portraitRt.gameObject.AddComponent<Image>();
        UIFactory.SetBox(portraitRt, anchor, anchor, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(280f, 280f));

        var nameText = UIFactory.CreateText(label + "Name", root, "", 24, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(nameText.rectTransform, anchor, anchor, new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(320f, 40f));
        nameText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        return (portrait, nameText);
    }

    // ── Round Intro (ROUND n / 3-2-1 / FIGHT!) ────────────────────────────────

    private static GameObject BuildRoundIntro(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("RoundIntroScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<RoundIntroView>();
        view.root = root.gameObject;

        // The ONE dark curtain for the whole VS->RoundIntro->Countdown->Fighting reveal — see
        // RoundIntroController's own doc (alpha driven at runtime from FightFlowConfig.
        // countdownOverlayAlpha, starts transparent here). Not theme-driven: a readability dimmer
        // over an already-visible arena+HUD should look the same regardless of which theme is active.
        var overlay = UIFactory.CreatePanel("Overlay", root, new Color(0f, 0f, 0f, 0f));
        UIFactory.Stretch(overlay.rectTransform);
        view.overlay = overlay;

        var text = UIFactory.CreateText("Text", root, "", 96, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(700f, 200f));
        text.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
        view.text = text;

        return root.gameObject;
    }

    // ── Round End (KO / TIME UP / round result) ──────────────────────────────

    private static GameObject BuildRoundEnd(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("RoundEndScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<RoundEndView>();
        view.root = root.gameObject;

        var text = UIFactory.CreateText("Text", root, "", 80, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(800f, 220f));
        text.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
        view.text = text;

        return root.gameObject;
    }

    // ── Match Result (YOU WIN / YOU LOSE) ────────────────────────────────────

    private static GameObject BuildMatchResult(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("MatchResultScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<MatchResultView>();
        view.root = root.gameObject;

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 0.9f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var title = UIFactory.CreateText("Title", root, "", 64, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 220f), new Vector2(800f, 90f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
        view.titleText = title;

        var rival = UIFactory.CreateText("Rival", root, "", 20, Color.white);
        UIFactory.SetBox(rival.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 160f), new Vector2(700f, 30f));
        rival.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        view.rivalText = rival;

        var rounds = UIFactory.CreateText("Rounds", root, "", 40, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(rounds.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 110f), new Vector2(400f, 50f));
        rounds.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);
        view.roundsText = rounds;

        var summary = UIFactory.CreateText("Summary", root, "", 16, Color.white);
        UIFactory.SetBox(summary.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 55f), new Vector2(700f, 50f));
        summary.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        view.summaryText = summary;

        var level = UIFactory.CreateText("Level", root, "", 22, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(level.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0f), new Vector2(500f, 34f));
        level.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Positive, UIFontToken.Body);
        view.levelText = level;

        var continueBtn = UIFactory.CreateButton("ContinueButton", root, Loc.Get("MatchResult.Continue"), out var continueLabel);
        UIFactory.SetBox(continueBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -60f), new Vector2(260f, 52f));
        continueBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        continueLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        view.continueButton = continueBtn;
        view.continueButtonLabel = continueLabel;

        var fightAgainBtn = UIFactory.CreateButton("FightAgainButton", root, "", out var fightAgainLabel);
        UIFactory.SetBox(fightAgainBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -60f), new Vector2(260f, 52f));
        fightAgainBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        fightAgainLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        view.fightAgainButton = fightAgainBtn;
        view.fightAgainButtonLabel = fightAgainLabel;

        var replaySongBtn = UIFactory.CreateButton("ReplaySongButton", root, Loc.Get("MatchResult.ReplaySong"), out var replaySongLabel);
        UIFactory.SetBox(replaySongBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -120f), new Vector2(260f, 52f));
        replaySongBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        replaySongLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        view.replaySongButton = replaySongBtn;
        view.replaySongButtonLabel = replaySongLabel;

        var mainMenuBtn = UIFactory.CreateButton("MainMenuButton", root, Loc.Get("Fight.MainMenu"), out var mainMenuLabel);
        UIFactory.SetBox(mainMenuBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -180f), new Vector2(260f, 52f));
        mainMenuBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        mainMenuLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        view.mainMenuButton = mainMenuBtn;
        view.mainMenuButtonLabel = mainMenuLabel;

        BuildRewardAdModal(root, view);

        return root.gameObject;
    }

    // Small confirmation sub-panel shown only when Fight Again is pressed with zero lives left —
    // see MatchResultController's own doc on the Rewarded Ad flow.
    private static void BuildRewardAdModal(RectTransform root, MatchResultView view)
    {
        var modal = UIFactory.CreateRect("RewardAdModal", root);
        UIFactory.Stretch(modal);

        var dim = UIFactory.CreatePanel("Dim", modal, new Color(0f, 0f, 0f, 0.75f));
        UIFactory.Stretch(dim.rectTransform);

        var panel = UIFactory.CreatePanel("Panel", modal, new Color(0.05f, 0.05f, 0.05f, 0.98f));
        UIFactory.SetBox(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 220f));
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        var title = UIFactory.CreateText("Title", panel.rectTransform, Loc.Get("MatchResult.WatchAdTitle"), 22, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(380f, 60f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);
        view.rewardAdTitleText = title;

        var watchAdBtn = UIFactory.CreateButton("WatchAdButton", panel.rectTransform, Loc.Get("MatchResult.WatchAd"), out var watchAdLabel);
        UIFactory.SetBox(watchAdBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(320f, 46f));
        watchAdBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        watchAdLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        view.watchAdButton = watchAdBtn;
        view.watchAdButtonLabel = watchAdLabel;

        var cancelBtn = UIFactory.CreateButton("CancelButton", panel.rectTransform, Loc.Get("MatchResult.Cancel"), out var cancelLabel);
        UIFactory.SetBox(cancelBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(320f, 46f));
        cancelBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        cancelLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        view.cancelButton = cancelBtn;
        view.cancelButtonLabel = cancelLabel;

        view.rewardAdModalRoot = modal.gameObject;
        modal.gameObject.SetActive(false);
    }

    // ── Next Song Transition (post Match-Won Continue cartela) ──────────────

    private static GameObject BuildNextSongTransition(RectTransform canvas)
    {
        var root = UIFactory.CreateRect("NextSongTransitionScreen", canvas);
        UIFactory.Stretch(root);
        var view = root.gameObject.AddComponent<NextSongTransitionView>();
        view.root = root.gameObject;
        view.canvasGroup = root.gameObject.AddComponent<CanvasGroup>();

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var level = UIFactory.CreateText("Level", root, "", 26, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(level.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 70f), new Vector2(700f, 40f));
        level.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Secondary, UIFontToken.Body);
        view.levelText = level;

        var header = UIFactory.CreateText("Header", root, Loc.Get("NextSong.Header"), 20, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(header.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 20f), new Vector2(700f, 34f));
        header.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        view.headerText = header;

        var songName = UIFactory.CreateText("SongName", root, "", 40, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(songName.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -40f), new Vector2(900f, 60f));
        songName.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.songNameText = songName;

        return root.gameObject;
    }
}
#endif
