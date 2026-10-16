#if UNITY_EDITOR
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fight group, ported 1:1 from the controllers' former procedural Build() code:
///   Prefabs/UI/FightHud.prefab            → FightHudView (UIPrefabLibrarySO.fightHud) — FightController: top bar
///                                           (names, health, round pips, timer, pause button, portraits),
///                                           camera view toggle, pause menu, mobile joystick + Punch/Kick.
///   Prefabs/UI/OpponentSelection.prefab   → OpponentSelectionView (opponentSelection) — title, grid, Skip.
///   Prefabs/UI/Items/OpponentCell.prefab  → OpponentCellView (opponentCell) — one roulette cell per rival.
///   Prefabs/UI/VersusScreen.prefab        → VersusScreenView (versusScreen) — the transparent Versus card.
/// Called by UIScreenPrefabBuilder.
/// </summary>
public static class FightScreenPrefabs
{
    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

    // Opponent Selection grid: 6 columns × 2 rows for the 12-composer roster (rows grow with the roster,
    // OpponentSelectionController sizes the container) — fits 1920×1080 between the title and Skip.
    private const float CellWidth = 190f, CellHeight = 240f, CellSpacing = 18f, CellNameHeight = 26f;
    private const int GridColumns = 6;

    // Fight HUD portraits: each takes its group's outer x; name / health / pips sit inwards by
    // PortraitSize + PortraitGap (FightController's former BuildPortrait shift, baked).
    private const float PortraitSize = 70f, PortraitGap = 12f, PortraitShift = PortraitSize + PortraitGap;

    public static void Build(UIScreenPrefabContext ctx)
    {
        ctx.Library.opponentCell = ctx.Prefab<OpponentCellView>($"{UIScreenPrefabContext.ItemsFolder}/OpponentCell.prefab", BuildOpponentCell);
        ctx.Library.fightHud = ctx.Prefab<FightHudView>($"{UIScreenPrefabContext.ScreensFolder}/FightHud.prefab", BuildFightHud);
        ctx.Library.opponentSelection = ctx.Prefab<OpponentSelectionView>($"{UIScreenPrefabContext.ScreensFolder}/OpponentSelection.prefab", BuildOpponentSelection);
        ctx.Library.versusScreen = ctx.Prefab<VersusScreenView>($"{UIScreenPrefabContext.ScreensFolder}/VersusScreen.prefab", BuildVersusScreen);
    }

    private static RectTransform StretchedRoot(string name)
    {
        var root = (RectTransform)UIScreenPrefabContext.Root(name, Vector2.zero).transform;
        UIFactory.Stretch(root);
        return root;
    }

    // ── Fight HUD ─────────────────────────────────────────────────────────────

    private static GameObject BuildFightHud()
    {
        var root = StretchedRoot("FightScreen");
        var view = root.gameObject.AddComponent<FightHudView>();
        view.root = root.gameObject;

        BuildTopBar(root, view);
        BuildPauseMenu(root, view);
        BuildMobileControls(root, view);
        BuildCameraToggle(root, view);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    private static void BuildTopBar(RectTransform root, FightHudView view)
    {
        var topBar = UIFactory.CreatePanel("TopBar", root, new Color(0.02f, 0.02f, 0.05f, 0.55f));
        UIFactory.SetBox(topBar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 90f));
        topBar.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);
        var bar = topBar.rectTransform;
        Vector2 topLeft = new Vector2(0f, 1f), topRight = new Vector2(1f, 1f);

        // Player — left (shifted right past its portrait).
        view.playerNameText = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("PlayerName", bar, "", 18, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold), "Fight.PlayerName");
        UIFactory.SetBox(view.playerNameText.rectTransform, topLeft, topLeft, topLeft, new Vector2(20f + PortraitShift, -12f), new Vector2(320f, 24f));
        view.playerNameText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);

        var playerHealthBg = UIFactory.CreateFillBar("PlayerHealth", bar, new Color(0.12f, 0.12f, 0.12f), new Color(0.3f, 0.85f, 0.3f), out var playerHealthFill);
        UIFactory.SetBox(playerHealthBg.rectTransform, topLeft, topLeft, topLeft, new Vector2(20f + PortraitShift, -42f), new Vector2(320f, 18f));
        playerHealthFill.fillAmount = 1f;
        // Health = Positive/Negative tokens, not just "some color" — the player's own bar drains
        // green, the rival's drains red, in every Theme.
        playerHealthFill.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Positive);
        view.playerHealthFill = playerHealthFill;

        view.playerRoundPipsText = UIFactory.CreateText("PlayerRoundPips", bar, "", 16, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        UIFactory.SetBox(view.playerRoundPipsText.rectTransform, topLeft, topLeft, topLeft, new Vector2(20f + PortraitShift, -64f), new Vector2(320f, 20f));
        view.playerRoundPipsText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        // Opponent — right (leaves room for the pause button at the far right, then its portrait).
        // Its text is data (PopulateInfo); the key is only an editor placeholder.
        view.opponentNameText = UIFactory.CreateText("OpponentName", bar, "Fight.RivalUnknown", 18, Color.white, TextAlignmentOptions.TopRight, FontStyles.Bold);
        UIFactory.SetBox(view.opponentNameText.rectTransform, topRight, topRight, topRight, new Vector2(-90f - PortraitShift, -12f), new Vector2(320f, 24f));
        view.opponentNameText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);

        var opponentHealthBg = UIFactory.CreateFillBar("OpponentHealth", bar, new Color(0.12f, 0.12f, 0.12f), new Color(0.9f, 0.3f, 0.25f), out var opponentHealthFill);
        UIFactory.SetBox(opponentHealthBg.rectTransform, topRight, topRight, topRight, new Vector2(-90f - PortraitShift, -42f), new Vector2(320f, 18f));
        opponentHealthFill.fillAmount = 1f;
        opponentHealthFill.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Negative);
        // Health bars fill from the LEFT by default (UIFactory.CreateFillBar) — mirror the
        // opponent's so it visibly drains toward its own name, like the player's does.
        opponentHealthFill.fillOrigin = (int)Image.OriginHorizontal.Right;
        view.opponentHealthFill = opponentHealthFill;

        view.opponentRoundPipsText = UIFactory.CreateText("OpponentRoundPips", bar, "", 16, Color.white, TextAlignmentOptions.TopRight, FontStyles.Bold);
        UIFactory.SetBox(view.opponentRoundPipsText.rectTransform, topRight, topRight, topRight, new Vector2(-90f - PortraitShift, -64f), new Vector2(320f, 20f));
        view.opponentRoundPipsText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        // Timer — center.
        view.timerText = UIFactory.CreateText("Timer", bar, "", 28, Color.white, TextAlignmentOptions.Top, FontStyles.Bold);
        UIFactory.SetBox(view.timerText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -12f), new Vector2(140f, 36f));
        view.timerText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        // Pause — far right.
        view.pauseButton = UIFactory.CreateButton("PauseButton", bar, "", out view.pauseButtonLabel);
        UIScreenPrefabContext.Localized(view.pauseButtonLabel, "Fight.Pause");
        view.pauseButtonLabel.fontSize = 12;
        UIFactory.SetBox(view.pauseButton.GetComponent<RectTransform>(), topRight, topRight, topRight, new Vector2(-15f, -15f), new Vector2(60f, 60f));
        view.pauseButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        view.pauseButtonLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        // Portraits — each at its group's outer x (the SAME sprites the VS screen shows; set at runtime).
        view.playerPortrait   = BuildPortrait("PlayerPortrait",   bar, topLeft,  new Vector2(0f, 1f), 20f,  view.portraitPlaceholderColor);
        view.opponentPortrait = BuildPortrait("OpponentPortrait", bar, topRight, new Vector2(1f, 1f), -90f, view.portraitPlaceholderColor);
    }

    private static Image BuildPortrait(string name, RectTransform parent, Vector2 anchor, Vector2 pivot, float x, Color placeholder)
    {
        var rt = UIFactory.CreateRect(name, parent);
        UIFactory.SetBox(rt, anchor, anchor, pivot, new Vector2(x, -8f), new Vector2(PortraitSize, PortraitSize));
        var img = rt.gameObject.AddComponent<Image>();
        img.preserveAspect = true;
        img.color = placeholder;
        return img;
    }

    private static void BuildPauseMenu(RectTransform root, FightHudView view)
    {
        var pausePanel = UIFactory.CreateRect("PauseMenu", root);
        UIFactory.Stretch(pausePanel);
        view.pausePanel = pausePanel.gameObject;

        var dim = UIFactory.CreatePanel("Dim", pausePanel, new Color(0f, 0f, 0f, 0.75f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        view.pauseTitleText = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("Title", pausePanel, "", 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "Fight.Paused");
        UIFactory.SetBox(view.pauseTitleText.rectTransform, Center, Center, Center, new Vector2(0f, 60f), new Vector2(400f, 50f));
        view.pauseTitleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        view.resumeButton = UIFactory.CreateButton("ResumeButton", pausePanel, "", out view.resumeButtonLabel);
        UIScreenPrefabContext.Localized(view.resumeButtonLabel, "Fight.Resume");
        UIFactory.SetBox(view.resumeButton.GetComponent<RectTransform>(), Center, Center, Center, new Vector2(0f, -10f), new Vector2(220f, 48f));
        view.resumeButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        view.resumeButtonLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);

        // The equivalent of the old top-level "Back" button — lives in the pause menu, since it's an
        // exit action, not something that belongs permanently on-screen during a match.
        view.mainMenuButton = UIFactory.CreateButton("MainMenuButton", pausePanel, "", out view.mainMenuButtonLabel);
        UIScreenPrefabContext.Localized(view.mainMenuButtonLabel, "Fight.MainMenu");
        UIFactory.SetBox(view.mainMenuButton.GetComponent<RectTransform>(), Center, Center, Center, new Vector2(0f, -70f), new Vector2(220f, 48f));
        view.mainMenuButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        view.mainMenuButtonLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        pausePanel.gameObject.SetActive(false);
    }

    // Joystick (movement) + Punch/Kick — visible ONLY during FightFlowState.Fighting, on real/simulated
    // mobile. The VirtualJoystick / TouchActionButton components live here; FightController wires their
    // callbacks to FightTouchInputState (Initialize) at runtime.
    private static void BuildMobileControls(RectTransform root, FightHudView view)
    {
        var mobileControls = UIFactory.CreateRect("MobileControls", root);
        UIFactory.Stretch(mobileControls);
        view.mobileControlsRoot = mobileControls.gameObject;

        const float joySize = 180f;
        var background = UIFactory.CreatePanel("JoystickBackground", mobileControls, new Color(1f, 1f, 1f, 0.15f));
        UIFactory.SetBox(background.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(40f, 40f), new Vector2(joySize, joySize));
        var handle = UIFactory.CreatePanel("JoystickHandle", background.rectTransform, new Color(1f, 1f, 1f, 0.4f));
        handle.rectTransform.sizeDelta        = new Vector2(joySize * 0.45f, joySize * 0.45f);
        handle.rectTransform.anchoredPosition = Vector2.zero;
        view.joystickBackground = background.rectTransform;
        view.joystickHandle     = handle.rectTransform;
        view.joystick           = background.gameObject.AddComponent<VirtualJoystick>();

        view.punchAction = BuildActionButton(mobileControls, "PunchButton", -170f, "Mobile.Punch", out view.punchButtonLabel);
        view.punchButton = view.punchAction.gameObject;
        view.kickAction  = BuildActionButton(mobileControls, "KickButton", -40f, "Mobile.Kick", out view.kickButtonLabel);
        view.kickButton  = view.kickAction.gameObject;

        mobileControls.gameObject.SetActive(false);
    }

    private static TouchActionButton BuildActionButton(RectTransform parent, string name, float xOffset, string locKey, out TextMeshProUGUI label)
    {
        const float size = 110f;
        var button = UIFactory.CreatePanel(name, parent, new Color(1f, 1f, 1f, 0.25f));
        UIFactory.SetBox(button.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(xOffset, 40f), new Vector2(size, size));

        label = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("Label", button.rectTransform, "", 16, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), locKey);
        UIFactory.Stretch(label.rectTransform);

        return button.gameObject.AddComponent<TouchActionButton>();
    }

    // Camera view toggle — just below the pause button; label (First/Third Person) is set at runtime.
    private static void BuildCameraToggle(RectTransform root, FightHudView view)
    {
        view.cameraToggleButton = UIFactory.CreateButton("CameraToggleButton", root, "", out view.cameraToggleLabel);
        view.cameraToggleLabel.fontSize = 12;
        UIFactory.SetBox(view.cameraToggleButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-15f, -84f), new Vector2(130f, 30f));
        view.cameraToggleButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        view.cameraToggleLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
    }

    // ── Opponent Selection ────────────────────────────────────────────────────

    private static GameObject BuildOpponentSelection()
    {
        var root = StretchedRoot("OpponentSelectionScreen");
        var view = root.gameObject.AddComponent<OpponentSelectionView>();
        view.root = root.gameObject;

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        view.titleText = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("Title", root, "", 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "OpponentSelection.Title");
        UIFactory.SetBox(view.titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -50f), new Vector2(900f, 50f));
        view.titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        // Container size here is the 12-rival preview; the controller re-sizes it to columns × rows.
        var grid = UIFactory.CreateRect("Grid", root);
        UIFactory.SetBox(grid, Center, Center, Center, Vector2.zero,
            new Vector2(GridColumns * CellWidth + (GridColumns - 1) * CellSpacing, 2f * CellHeight + CellSpacing));
        var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize        = new Vector2(CellWidth, CellHeight);
        layout.spacing         = new Vector2(CellSpacing, CellSpacing);
        layout.childAlignment  = TextAnchor.MiddleCenter;
        layout.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = GridColumns;
        layout.padding         = new RectOffset();
        view.gridRoot = grid;

        view.skipButton = UIFactory.CreateButton("SkipButton", root, "", out var skipLabel);
        UIScreenPrefabContext.Localized(skipLabel, "OpponentSelection.Skip");
        UIFactory.SetBox(view.skipButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-40f, 40f), new Vector2(170f, 50f));
        view.skipButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        skipLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    private static GameObject BuildOpponentCell()
    {
        // Real Button component (for its Image + color-tint machinery), no onClick — display-only.
        var button = UIFactory.CreateButton("OpponentCell", null, "?", out var nameLabel);
        var root = button.GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(CellWidth, CellHeight); // the grid's cellSize drives it at runtime
        var view = root.gameObject.AddComponent<OpponentCellView>();
        view.button = button;
        view.background = button.GetComponent<Image>();
        view.background.color = new Color(1f, 1f, 1f, 0.10f);

        UIFactory.SetBox(nameLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 4f), new Vector2(-10f, CellNameHeight));
        nameLabel.fontSize = 13;
        view.nameText = nameLabel;

        var portraitRt = UIFactory.CreateRect("Portrait", root);
        view.portrait = portraitRt.gameObject.AddComponent<Image>();
        UIFactory.SetBox(portraitRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -8f), new Vector2(-10f, CellHeight - CellNameHeight - 20f));
        view.portrait.preserveAspect = true; // portraits are tall cut-outs, the cell is square
        view.portrait.color = view.emptyPortraitColor;

        return root.gameObject;
    }

    // ── Versus card ───────────────────────────────────────────────────────────

    private static GameObject BuildVersusScreen()
    {
        var root = StretchedRoot("VersusScreen");
        var view = root.gameObject.AddComponent<VersusScreenView>();
        view.root = root.gameObject;
        view.cardGroup = root.gameObject.AddComponent<CanvasGroup>();
        view.cardGroup.blocksRaycasts = false; // the arena presentation never eats clicks; Skip is its own button

        // A light veil over the LIVE arena; VersusScreenController sets its alpha from
        // FightFlowConfig.versusBackdropAlpha — no theme tint, which would make it opaque.
        view.backdrop = UIFactory.CreatePanel("Dim", root, new Color(0f, 0f, 0f, 0.15f));
        UIFactory.Stretch(view.backdrop.rectTransform);
        view.backdrop.raycastTarget = false;

        (view.playerPortrait, view.playerNameText)     = BuildVersusSide(root, "Player",   new Vector2(0.25f, 0.5f), view.placeholderPortraitColor);
        (view.opponentPortrait, view.opponentNameText) = BuildVersusSide(root, "Opponent", new Vector2(0.75f, 0.5f), view.placeholderPortraitColor);

        view.vsText = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("VS", root, "", 72, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "Fight.Versus");
        UIFactory.SetBox(view.vsText.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(300f, 150f));
        view.vsText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    // anchor.x picks left/right half of the screen; portrait sits above its name, both centered on that anchor.
    private static (Image portrait, TextMeshProUGUI nameText) BuildVersusSide(RectTransform root, string label, Vector2 anchor, Color placeholder)
    {
        var portraitRt = UIFactory.CreateRect(label + "Portrait", root);
        var portrait = portraitRt.gameObject.AddComponent<Image>();
        UIFactory.SetBox(portraitRt, anchor, anchor, Center, new Vector2(0f, 40f), new Vector2(280f, 280f));
        portrait.color = placeholder; // PopulateInfo sets sprite + tint at runtime

        var nameText = UIFactory.CreateText(label + "Name", root, "", 24, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(nameText.rectTransform, anchor, anchor, Center, new Vector2(0f, -130f), new Vector2(320f, 40f));
        nameText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        return (portrait, nameText);
    }
}
#endif
