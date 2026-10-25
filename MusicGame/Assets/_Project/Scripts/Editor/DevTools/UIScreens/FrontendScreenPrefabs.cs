#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Frontend screen prefabs (called by UIScreenPrefabBuilder), ported 1:1 from the controllers' former
/// procedural Build() code — names, anchors, sizes, colours, fonts and theme receivers unchanged:
///   Prefabs/UI/IntroScreen.prefab     IntroScreenView     (IntroScreenController)
///   Prefabs/UI/MainMenu.prefab        MainMenuView        (MainMenuController) — nests Components/LanguageSelector
///   Prefabs/UI/SongSelection.prefab   SongSelectionView   (SongSelectionController)
///   Prefabs/UI/Items/SongRow.prefab   SongRowView         one catalog row of Song Selection
/// and an idempotent PATCH of SongRow.prefab: the old text badge (drawn over PREVIEW, catching its clicks) is
/// replaced by a non-interactive "played" check in its own column (name | check | PREVIEW).
/// Static captions carry a LocalizedText; data-dependent texts (PLAY/CONTINUE, card PLAY/PAUSE, streaming
/// "Coming Soon" lines, status/profile lines) are filled by the controllers.
/// </summary>
public static class FrontendScreenPrefabs
{
    private static readonly Color DimColor = new(0.02f, 0.02f, 0.02f, 1f);
    private static readonly Vector2 Center = new(0.5f, 0.5f);
    private static readonly Vector2 Top = new(0.5f, 1f);
    private static readonly Vector2 Bottom = new(0.5f, 0f);

    public static void Build(UIScreenPrefabContext ctx)
    {
        // Items first, so the screens can reference them.
        ctx.Library.songRow = ctx.Prefab<SongRowView>($"{UIScreenPrefabContext.ItemsFolder}/SongRow.prefab", BuildSongRow);
        ctx.Patch($"{UIScreenPrefabContext.ItemsFolder}/SongRow.prefab", PatchSongRow);

        ctx.Library.introScreen = ctx.Prefab<IntroScreenView>($"{UIScreenPrefabContext.ScreensFolder}/IntroScreen.prefab", BuildIntroScreen);
        ctx.Library.mainMenu = ctx.Prefab<MainMenuView>($"{UIScreenPrefabContext.ScreensFolder}/MainMenu.prefab", () => BuildMainMenu(ctx));
        ctx.Library.songSelection = ctx.Prefab<SongSelectionView>($"{UIScreenPrefabContext.ScreensFolder}/SongSelection.prefab", BuildSongSelection);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RectTransform StretchedRoot(string name, out GameObject go)
    {
        go = UIScreenPrefabContext.Root(name, Vector2.zero);
        var rt = (RectTransform)go.transform;
        UIFactory.Stretch(rt);
        return rt;
    }

    private static void Dim(RectTransform root, out Image dim)
    {
        dim = UIFactory.CreatePanel("Dim", root, DimColor);
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);
    }

    private static void Box(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size) =>
        UIFactory.SetBox(rt, anchor, anchor, anchor, pos, size);

    /// <summary>UIFactory.CreateButton + the background/label theme receivers the old Build() added.
    /// `key` null = data-dependent label (no LocalizedText).</summary>
    private static Button ThemedButton(string name, RectTransform parent, string key, out TextMeshProUGUI label,
                                       UIColorToken? background, UIColorToken labelColor)
    {
        var btn = UIFactory.CreateButton(name, parent, "", out label);
        if (key != null) UIScreenPrefabContext.Localized(label, key);
        if (background.HasValue) btn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(background.Value);
        label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(labelColor, UIFontToken.Body);
        return btn;
    }

    // ── Intro ────────────────────────────────────────────────────────────────

    private static GameObject BuildIntroScreen()
    {
        var root = StretchedRoot("IntroScreen", out var go);
        var canvasGroup = go.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        Dim(root, out var dim);
        // Full-screen invisible button so a click/tap anywhere skips the wait.
        var skipButton = dim.gameObject.AddComponent<Button>();
        skipButton.transition = Selectable.Transition.None;

        var title = UIScreenPrefabContext.Localized(UIFactory.CreateText("Title", root, "", 42, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "Intro.Title");
        Box(title.rectTransform, Center, Vector2.zero, new Vector2(1000f, 80f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        var view = go.AddComponent<IntroScreenView>();
        view.root = go;
        view.canvasGroup = canvasGroup;
        view.skipButton = skipButton;
        view.titleText = title;

        go.SetActive(false);
        return go;
    }

    // ── Main Menu ────────────────────────────────────────────────────────────

    private static GameObject BuildMainMenu(UIScreenPrefabContext ctx)
    {
        var root = StretchedRoot("MainMenuScreen", out var go);
        var view = go.AddComponent<MainMenuView>();
        view.root = go;

        Dim(root, out _);

        view.titleText = UIScreenPrefabContext.Localized(UIFactory.CreateText("Title", root, "", 36, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "MainMenu.Title");
        UIFactory.SetBox(view.titleText.rectTransform, Top, Top, Top, new Vector2(0f, -100f), new Vector2(900f, 60f));
        view.titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        // Profile icon placeholder, top-right — no behavior yet.
        var profileIcon = UIFactory.CreatePanel("ProfileIcon", root, Color.gray);
        UIFactory.SetBox(profileIcon.rectTransform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-24f, -24f), new Vector2(48f, 48f));
        profileIcon.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Secondary);

        // Stacked top → bottom from PLAY's slot (MainMenuController.LayoutButtons re-stacks the visible ones
        // at runtime with the same step: button height + buttonStackGap).
        const float btnW = 260f, btnH = 54f;
        float gap = view.buttonStackGap, y = -20f;
        Button StackButton(string name, string key, out TextMeshProUGUI label, UIColorToken bg, UIColorToken fg)
        {
            var b = ThemedButton(name, root, key, out label, bg, fg);
            Box(b.GetComponent<RectTransform>(), Center, new Vector2(0f, y), new Vector2(btnW, btnH));
            y -= btnH + gap;
            return b;
        }
        view.playButton = StackButton("PlayButton", null, out view.playButtonLabel, UIColorToken.ButtonPrimary, UIColorToken.Accent);
        view.playButtonLabel.text = "MainMenu.Play"; // PLAY / CONTINUE, set at runtime
        view.rivalsButton = StackButton("RivalsButton", "MainMenu.Rivals", out _, UIColorToken.ButtonPrimary, UIColorToken.TextPrimary);
        view.progressionButton = StackButton("ProgressionButton", "MainMenu.Progression", out _, UIColorToken.ButtonPrimary, UIColorToken.TextPrimary);
        view.settingsButton = StackButton("SettingsButton", "MainMenu.Settings", out view.settingsButtonLabel, UIColorToken.ButtonPrimary, UIColorToken.TextPrimary);
        view.quitButton = StackButton("QuitButton", "MainMenu.Quit", out view.quitButtonLabel, UIColorToken.ButtonSecondary, UIColorToken.TextSecondary);

        // Campaign status under the title (MainMenuController: next match position + lap · mastered · extra lives).
        view.profileText = UIFactory.CreateText("ProfileText", root, "", 18, Color.white, TextAlignmentOptions.Center);
        UIFactory.SetBox(view.profileText.rectTransform, Top, Top, Top, new Vector2(0f, -160f), new Vector2(900f, 70f));
        view.profileText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        view.menuStatusText = UIFactory.CreateText("MenuStatus", root, "", 18, new Color(0.45f, 0.85f, 0.5f), TextAlignmentOptions.Center, FontStyles.Bold);
        Box(view.menuStatusText.rectTransform, Center, new Vector2(0f, 60f), new Vector2(700f, 32f));
        view.menuStatusText.gameObject.SetActive(false);

        BuildSettingsPanel(ctx, root, view);
        go.SetActive(false);
        return go;
    }

    public const float ChannelRowsHeight = 170f;

    /// <summary>Music / Effects / Voices sliders under the language row: grows the panel by ChannelRowsHeight and
    /// moves the existing centred rows up by half of it. Also used on the existing prefab (AudioSettingsUISetup).</summary>
    public static void AddChannelVolumeRows(RectTransform panel, MainMenuView view)
    {
        var size = panel.sizeDelta; size.y += ChannelRowsHeight; panel.sizeDelta = size;
        foreach (RectTransform child in panel)
            if (child.anchorMin == Center && child.anchorMax == Center) child.anchoredPosition += new Vector2(0f, ChannelRowsHeight * 0.5f);
        var rows = new (string key, string name)[] { ("Settings.MusicVolume", "Music"), ("Settings.SfxVolume", "Sfx"), ("Settings.VoiceVolume", "Voice") };
        var sliders = new UnityEngine.UI.Slider[3];
        for (int i = 0; i < rows.Length; i++)
        {
            float y = -30f - i * 46f;
            var label = UIScreenPrefabContext.Localized(UIFactory.CreateText(rows[i].name + "VolumeLabel", panel, "", 13, new Color(0.8f, 0.8f, 0.8f), TextAlignmentOptions.Left), rows[i].key);
            Box(label.rectTransform, Center, new Vector2(0f, y), new Vector2(360f, 20f));
            label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
            var s = UIFactory.CreateSlider(rows[i].name + "VolumeSlider", panel, 1f, new Color(0.18f, 0.75f, 0.95f));
            Box(s.GetComponent<RectTransform>(), Center, new Vector2(0f, y - 18f), new Vector2(360f, 12f));
            s.fillRect.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Accent);
            sliders[i] = s;
        }
        view.musicVolumeSlider = sliders[0]; view.sfxVolumeSlider = sliders[1]; view.voiceVolumeSlider = sliders[2];
    }

    private static void BuildSettingsPanel(UIScreenPrefabContext ctx, RectTransform root, MainMenuView view)
    {
        var selectorPrefab = ctx.Library.languageSelector;
        float extra = selectorPrefab != null ? selectorPrefab.PanelExtraHeight : 0f; // the panel grows to fit the language row

        var panel = UIFactory.CreateRect("SettingsPanel", root);
        Box(panel, Center, Vector2.zero, new Vector2(420f, 220f + extra));
        var panelBg = panel.gameObject.AddComponent<Image>();
        panelBg.color = new Color(0.05f, 0.05f, 0.05f, 0.97f);
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);
        view.settingsPanel = panel.gameObject;

        view.settingsTitleText = UIScreenPrefabContext.Localized(UIFactory.CreateText("Title", panel, "", 20, Color.white), "Settings.Title");
        UIFactory.SetBox(view.settingsTitleText.rectTransform, Top, Top, Top, new Vector2(0f, -16f), new Vector2(380f, 30f));
        view.settingsTitleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);

        view.volumeLabelText = UIScreenPrefabContext.Localized(UIFactory.CreateText("VolumeLabel", panel, "", 14, new Color(0.8f, 0.8f, 0.8f), TextAlignmentOptions.Left), "Settings.Volume");
        Box(view.volumeLabelText.rectTransform, Center, new Vector2(0f, 20f), new Vector2(360f, 24f));
        view.volumeLabelText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        view.volumeSlider = UIFactory.CreateSlider("VolumeSlider", panel, 1f, new Color(0.18f, 0.75f, 0.95f));
        Box(view.volumeSlider.GetComponent<RectTransform>(), Center, new Vector2(0f, -15f), new Vector2(360f, 16f));
        view.volumeSlider.fillRect.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Accent);

        view.closeButton = ThemedButton("CloseButton", panel, "Settings.Close", out view.closeButtonLabel, UIColorToken.ButtonSecondary, UIColorToken.TextPrimary);
        UIFactory.SetBox(view.closeButton.GetComponent<RectTransform>(), Bottom, Bottom, Bottom, new Vector2(0f, 20f), new Vector2(140f, 40f));

        // Language row: the LanguageSelector component prefab, nested (linked) so editing it updates every user.
        if (selectorPrefab != null)
        {
            view.languageSelector = UIScreenPrefabContext.Nested(selectorPrefab, panel);
            var src = (RectTransform)selectorPrefab.transform;
            var dst = (RectTransform)view.languageSelector.transform;
            UIFactory.SetBox(dst, src.anchorMin, src.anchorMax, src.pivot, src.anchoredPosition, src.sizeDelta);
        }
        else ctx.Log.AppendLine("  FAIL  MainMenu: UIPrefabLibrary.languageSelector missing — settings panel built without the language row");

        AddChannelVolumeRows(panel, view);
        panel.gameObject.SetActive(false);
    }

    // ── Song Selection ───────────────────────────────────────────────────────

    private static GameObject BuildSongSelection()
    {
        var root = StretchedRoot("SongSelectionScreen", out var go);
        var view = go.AddComponent<SongSelectionView>();
        view.root = go;

        Dim(root, out _);

        view.titleText = UIScreenPrefabContext.Localized(UIFactory.CreateText("Title", root, "", 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "SongSelection.Title");
        UIFactory.SetBox(view.titleText.rectTransform, Top, Top, Top, new Vector2(0f, -50f), new Vector2(900f, 50f));
        view.titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        // Scrollable catalog: SongRow prefabs are spawned into Content (VerticalLayoutGroup, spacing = row gap).
        var scrollRect = UIFactory.CreateScrollRect("SongList", root, out view.songListRoot, 10f);
        UIFactory.SetBox(scrollRect.GetComponent<RectTransform>(), Top, Top, Top, new Vector2(0f, -120f), new Vector2(560f, 400f));

        // A standalone button right below the scrollable list — NOT one of its rows. Its background is
        // deliberately NOT a ThemeColorReceiver (selection state tints it, see SongSelectionController).
        view.playYourSongButton = ThemedButton("PlayYourSongButton", root, "SongSelection.PlayYourSong", out view.playYourSongLabel, null, UIColorToken.TextPrimary);
        UIFactory.SetBox(view.playYourSongButton.GetComponent<RectTransform>(), Top, Top, Top, new Vector2(0f, -530f), new Vector2(560f, 50f));

        // Streaming placeholders (disabled; "<service> (Coming Soon)" is bound at runtime).
        var row = UIFactory.CreateRect("StreamingRow", root);
        UIFactory.SetBox(row, Bottom, Bottom, Bottom, new Vector2(0f, 150f), new Vector2(560f, 44f));
        string[] keys = { "SongSelection.Spotify", "SongSelection.YouTubeMusic", "SongSelection.AmazonMusic" };
        var buttons = new Button[keys.Length];
        var labels = new TextMeshProUGUI[keys.Length];
        float w = (560f - 2f * 10f) / 3f;
        for (int i = 0; i < keys.Length; i++)
        {
            buttons[i] = ThemedButton("Streaming_" + keys[i], row, null, out labels[i], UIColorToken.ButtonSecondary, UIColorToken.TextSecondary);
            labels[i].text = keys[i];
            labels[i].fontSize = 11;
            UIFactory.SetBox(buttons[i].GetComponent<RectTransform>(), Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(i * (w + 10f), 0f), new Vector2(w, 0f));
            buttons[i].interactable = false;
        }
        view.spotifyButton = buttons[0]; view.spotifyLabel = labels[0];
        view.youtubeMusicButton = buttons[1]; view.youtubeMusicLabel = labels[1];
        view.amazonMusicButton = buttons[2]; view.amazonMusicLabel = labels[2];

        // Bottom bar.
        view.backButton = ThemedButton("BackButton", root, "SongSelection.Back", out view.backButtonLabel, UIColorToken.ButtonSecondary, UIColorToken.TextPrimary);
        UIFactory.SetBox(view.backButton.GetComponent<RectTransform>(), Bottom, Bottom, Bottom, new Vector2(-150f, 40f), new Vector2(180f, 44f));
        view.playButton = ThemedButton("PlayButton", root, "SongSelection.Play", out view.playButtonLabel, UIColorToken.ButtonPrimary, UIColorToken.Accent);
        UIFactory.SetBox(view.playButton.GetComponent<RectTransform>(), Bottom, Bottom, Bottom, new Vector2(150f, 40f), new Vector2(180f, 44f));

        view.statusText = UIFactory.CreateText("Status", root, "", 12, view.neutralStatusColor);
        UIFactory.SetBox(view.statusText.rectTransform, Bottom, Bottom, Bottom, new Vector2(0f, 90f), new Vector2(500f, 24f));

        go.SetActive(false);
        return go;
    }

    /// <summary>One catalog row: UIFactory.CreateButton (its Image = selection background, no theme receiver),
    /// left-aligned name, the played check and the Play/Pause card button — three separate columns.</summary>
    private static GameObject BuildSongRow()
    {
        const float rowHeight = 56f;
        var holder = UIScreenPrefabContext.Root("Holder", Vector2.zero);
        var rowButton = UIFactory.CreateButton("SongRow", (RectTransform)holder.transform, "", out var label);
        var go = rowButton.gameObject;
        go.transform.SetParent(null, false);
        Object.DestroyImmediate(holder);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(560f, rowHeight); // width is driven by the list's VerticalLayoutGroup

        label.text = "Song name";
        label.alignment = TextAlignmentOptions.Left;
        label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        // Play/Pause preview on the card itself (label PLAY / PAUSE set at runtime).
        var play = ThemedButton("PlayPause", rt, null, out var playLabel, UIColorToken.ButtonSecondary, UIColorToken.TextPrimary);
        playLabel.text = "SongSelection.CardPlay";
        playLabel.fontSize = 13;
        UIFactory.SetBox(play.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-8f, 0f), new Vector2(92f, rowHeight - 14f));

        UIFactory.SetBox(label.rectTransform, Vector2.zero, Vector2.one, Center, new Vector2(-50f, 0f), new Vector2(-124f, 0f));

        var view = go.AddComponent<SongRowView>();
        view.button = rowButton;
        view.background = go.GetComponent<Image>();
        view.label = label;
        view.playPauseButton = play;
        view.playPauseLabel = playLabel;
        PatchSongRow(go);
        return go;
    }

    // Columns, right to left: PREVIEW (8 px margin, 92 wide) · 8 px · check (28) · 14 px · name (12 px left inset).
    private const float CheckSize = 28f, CheckRight = -108f, NameRightInset = 150f;
    public const string CheckSpritePath = HelpUISetup.PlaceholderFolder + "/Icon_PlayedCheck.png";

    public static void PatchSongRow(GameObject root)
    {
        var view = root.GetComponent<SongRowView>();
        if (view == null) return;
        var rt = (RectTransform)root.transform;

        // The old status text: a raycast-target TMP drawn after PREVIEW, over it — removed, not hidden.
        var badge = rt.Find("CompletedBadge");
        if (badge != null) Object.DestroyImmediate(badge.gameObject, true);

        if (view.playedCheck == null)
        {
            var check = UIFactory.CreatePanel("PlayedCheck", rt, view.playedOffColor);
            UIFactory.SetBox(check.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(CheckRight, 0f), new Vector2(CheckSize, CheckSize));
            check.preserveAspect = true;
            view.playedCheck = check;
        }
        if (view.playedCheck.sprite == null) view.playedCheck.sprite = EnsureCheckSprite();
        view.playedCheck.raycastTarget = false; // informative only: never catches a click meant for the row / PREVIEW

        // The name ends before the check's column; a long name is cut with an ellipsis instead of running under it.
        if (view.label != null)
        {
            var l = view.label.rectTransform;
            l.offsetMax = new Vector2(-NameRightInset, l.offsetMax.y);
            view.label.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    // Placeholder tick (white, tinted by SongRowView's on/off colours) — swap the PNG or the Image's sprite for final art.
    private static Sprite EnsureCheckSprite()
    {
        if (!File.Exists(CheckSpritePath))
        {
            const int size = 64;
            var a = new Vector2(14f, 33f); var b = new Vector2(27f, 19f); var c = new Vector2(51f, 46f);
            const float half = 4.5f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Mathf.Min(SegmentDistance(p, a, b), SegmentDistance(p, b, c));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(half + 0.5f - d)));
                }
            Directory.CreateDirectory(Path.GetDirectoryName(CheckSpritePath));
            File.WriteAllBytes(CheckSpritePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(CheckSpritePath);
        }
        return UIComponentPrefabBuilder.EnsureSingleSprite(CheckSpritePath, null);
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }
}
#endif
