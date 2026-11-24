#if UNITY_EDITOR
using System;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class JukeboxSetup
{
    private static readonly Vector2 TL = new(0, 1), C = new(.5f, .5f);
    // Migrate an existing prefab once, including when the project is already open in the editor.
    [InitializeOnLoadMethod]
    private static void UpgradeCabinet()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var view = AssetDatabase.LoadAssetAtPath<JukeboxView>(UIScreenPrefabContext.ScreensFolder + "/Jukebox.prefab");
            if (view == null) return;
            if (view.cabinetContent != null && view.carousel != null && view.tempoFury != null && view.transform.Find("Content/JukeboxNameplate") == null && !view.libraryPanel.GetComponent<ScrollRect>().enabled &&
                view.libraryPanel.GetComponent<ScrollRect>().viewport.GetComponent<Mask>() != null && view.archedTitle != null && view.playPause.image.type == Image.Type.Simple) return;
            if (AssetImporter.GetAtPath(JukeboxArt + "TransportForward.png") == null) return;
            var library = AssetDatabase.LoadAssetAtPath<UIPrefabLibrarySO>(UIScreenPrefabContext.LibraryPath);
            if (library == null) return;
            var context = new UIScreenPrefabContext(library, false, new StringBuilder());
            context.Patch(UIScreenPrefabContext.ItemsFolder + "/JukeboxRow.prefab", PatchClassicRow);
            context.Patch(UIScreenPrefabContext.ScreensFolder + "/Jukebox.prefab", root =>
            {
                PatchLyrics(root);
                PatchCabinet(root);
            });
        };
    }

    [MenuItem("Tools/MusicGame/UI/Setup Jukebox")]
    public static void Setup()
    {
        var lib = AssetDatabase.LoadAssetAtPath<UIPrefabLibrarySO>(UIScreenPrefabContext.LibraryPath);
        var log = new StringBuilder();
        Build(new UIScreenPrefabContext(lib, false, log));
        EditorUtility.SetDirty(lib); AssetDatabase.SaveAssets();
        Debug.Log(log);
    }
    public static void Build(UIScreenPrefabContext ctx)
    {
        ctx.Prefab<JukeboxRowView>(UIScreenPrefabContext.ItemsFolder + "/JukeboxRow.prefab", Row);
        ctx.Prefab<JukeboxView>(UIScreenPrefabContext.ScreensFolder + "/Jukebox.prefab", Screen);
        ctx.Patch(UIScreenPrefabContext.ItemsFolder + "/JukeboxRow.prefab", PatchClassicRow);
        ctx.Patch(UIScreenPrefabContext.ScreensFolder + "/Jukebox.prefab", root =>
        {
            PatchLyrics(root);
            PatchCabinet(root);
        });
        ctx.Library.jukeboxRow = AssetDatabase.LoadAssetAtPath<JukeboxRowView>(UIScreenPrefabContext.ItemsFolder + "/JukeboxRow.prefab");
        ctx.Library.jukebox = AssetDatabase.LoadAssetAtPath<JukeboxView>(UIScreenPrefabContext.ScreensFolder + "/Jukebox.prefab");
        ctx.Patch(UIScreenPrefabContext.ScreensFolder + "/MainMenu.prefab", PatchMenu);
        LocalizationSetup.AddMissingEntries(LocalizationSetup.Entries.Where(e => e.key.StartsWith("Jukebox.") || e.key == "MainMenu.Jukebox").Select(e => e.key));
        const string path = "Assets/_Project/Resources/JukeboxMetadata.asset";
        if (AssetDatabase.LoadAssetAtPath<JukeboxMetadataSO>(path) == null)
        {
            var metadata = ScriptableObject.CreateInstance<JukeboxMetadataSO>();
            var app = Resources.Load<AppConfigSO>("AppConfig");
            if (app?.songCatalog != null)
                foreach (var e in app.songCatalog.songs)
                {
                    JukeboxTrack.SplitName(e.address ?? e.songId, out var artist, out var title);
                    metadata.songs.Add(new JukeboxMetadataSO.Entry { songId = e.songId, artist = artist, title = title });
                }
            AssetDatabase.CreateAsset(metadata, path);
        }
    }
    private const string JukeboxArt = "Assets/_Project/Art/UI/Jukebox/";
    private static readonly Color Brass = new(.98f, .82f, .5f);

    private static Sprite Art(string name)
    {
        string path = JukeboxArt + name + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Sprite Classic(bool selected)
    {
        string name = selected ? "StraightCrimson" : "StraightIvory";
        var sprite = Art(name);
        var importer = (TextureImporter)AssetImporter.GetAtPath(JukeboxArt + name + ".png");
        var border = new Vector4(170, 26, 200, 26);
        if (importer.spriteBorder != border)
        {
            importer.spriteBorder = border;
            importer.SaveAndReimport();
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(JukeboxArt + name + ".png");
        }
        return sprite;
    }

    private static void PatchClassicRow(GameObject root)
    {
        var row = root.GetComponent<JukeboxRowView>();
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(710, 107);
        row.background.sprite = null;
        row.background.color = Color.clear;
        row.normalArt = Classic(false);
        row.selectedArt = Classic(true);
        row.plate = ImagePart("ClassicPlate", rect, 0, 0, 710, 107, row.normalArt, Color.white);
        UIFactory.Stretch(row.plate.rectTransform);
        row.plate.transform.SetAsFirstSibling();
        row.button.targetGraphic = row.plate;
        row.SetSelected(false);
        JukeboxRowView.ApplyButtonFeedback(row.button);
        row.title.gameObject.SetActive(true);
        row.cover.gameObject.SetActive(false);
        row.detail.gameObject.SetActive(false);
        row.state.gameObject.SetActive(false);
        Box(row.title.rectTransform, 50, 14, 583, 79);
        row.title.fontSize = 35;
        row.title.enableAutoSizing = true;
        row.title.fontSizeMin = 21;
        row.title.fontSizeMax = 35;
        row.title.textWrappingMode = TextWrappingModes.NoWrap;
        row.title.fontStyle = FontStyles.Bold | FontStyles.Italic;
        var receiver = row.title.GetComponent<ThemeTextReceiver>();
        if (receiver != null) UnityEngine.Object.DestroyImmediate(receiver);
        row.rowLight = Neon(rect, "RowLight", 32, 111, 626, 4, 0, 2, true);
    }

    private static void PatchCabinet(GameObject root)
    {
        var view = root.GetComponent<JukeboxView>();
        var panel = (RectTransform)root.transform.Find("Content");
        view.cabinetContent = panel;
        var background = root.transform.Find("Background").GetComponent<Image>();
        background.sprite = null;
        background.color = Color.clear;
        FrontendBackdropSetup.Configure(root);
        var cabinet = ImagePart("Cabinet", panel, 0, 0, 1740, 980, Art("SecretCabinetFloorTransparent"), Color.white);
        cabinet.transform.SetAsFirstSibling();
        foreach (string obsolete in new[] { "CabinetNeon", "SleeveNeon", "ControlsNeon", "NowPlayingRule", "ForegroundScoreLeft", "ForegroundScoreRight" })
        {
            var element = panel.Find(obsolete);
            if (element != null) UnityEngine.Object.DestroyImmediate(element.gameObject);
        }
        Neon(panel, "UpperRailLight", 185, 193, 1370, 4, 0, 5, true);
        Neon(panel, "LibraryNeon", 122, 244, 740, 568, 70, 1);
        Neon(panel, "PlayerNeon", 882, 244, 729, 568, 70, 3);
        Neon(panel, "BaseRailLight", 167, 837, 1400, 4, 0, 7, true);
        AddTempoFury(view, panel);
        view.styleSleeves = Enumerable.Range(0, 9).Select(index => Art("StyleSleeve" + index)).ToArray();
        view.medallions = new Image[4];
        float[] centers = { 1169, 1292, 1411, 1530 };
        for (int i = 0; i < centers.Length; i++)
        {
            var circle = ImagePart("Medallion" + i, panel, centers[i] - 51, 64, 102, 102, Art("MedallionAperture"), Color.white);
            var mount = circle.GetComponent<Mask>() ?? circle.gameObject.AddComponent<Mask>();
            mount.showMaskGraphic = false;
            view.medallions[i] = ImagePart("Artwork", circle.rectTransform, 0, 0, 102, 102, view.styleSleeves[i], Color.white);
        }

        var oldPlaque = panel.Find("JukeboxNameplate");
        if (oldPlaque != null) UnityEngine.Object.DestroyImmediate(oldPlaque.gameObject);
        var title = panel.Find("Title").GetComponent<TextMeshProUGUI>();
        if (view.archedTitle == null)
        {
            Box(title.rectTransform, 551, 85, 638, 110);
            title.text = "JUKEBOX";
            title.overflowMode = TextOverflowModes.Overflow;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.alignment = TextAlignmentOptions.Center;
            title.fontSize = 82;
            title.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/TempoFuryBrush/TempoFuryBrush SDF.asset");
            var titleTheme = title.GetComponent<ThemeTextReceiver>();
            if (titleTheme != null) UnityEngine.Object.DestroyImmediate(titleTheme);
            title.color = new Color(1f, .964f, .89f);
        }
        view.archedTitle = title;
        panel.Find("Subtitle").gameObject.SetActive(false);
        Box(view.summary.rectTransform, 1405, 851, 175, 62);
        view.summary.alignment = TextAlignmentOptions.Right;
        view.summary.fontSize = 33;
        Box((RectTransform)view.libraryPanel.transform, 123, 245, 737, 566);
        var lyricsRect = (RectTransform)view.lyricsPanel.transform;
        Box(lyricsRect, 914, 578, 654, 50);
        view.lyricsScroll.enabled = false;
        view.lyricsLine.rectTransform.SetParent(lyricsRect, false);
        Box(view.lyricsLine.rectTransform, 0, 0, 654, 50);
        view.lyricsLine.gameObject.SetActive(true);
        view.lyricsLine.alignment = TextAlignmentOptions.Center;
        view.lyricsLine.color = new Color(1f, .92f, .76f);
        view.lyricsLine.fontStyle = FontStyles.Italic;
        view.lyricsLine.fontSize = 27;
        view.lyricsLine.enableAutoSizing = true;
        view.lyricsLine.fontSizeMin = 18;
        view.lyricsLine.fontSizeMax = 27;
        view.lyricsLine.text = "";
        var viewport = view.libraryPanel.GetComponent<ScrollRect>().viewport;
        var aperture = viewport.GetComponent<Image>();
        aperture.sprite = Art("ReelAperture");
        aperture.color = Color.white;
        var mask = viewport.GetComponent<Mask>() ?? viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        var list = view.list.GetComponent<VerticalLayoutGroup>();
        list.enabled = false;
        view.list.GetComponent<ContentSizeFitter>().enabled = false;
        UIFactory.Stretch(view.list);
        var scroll = view.libraryPanel.GetComponent<ScrollRect>();
        scroll.enabled = false;
        view.carousel = view.libraryPanel.GetComponent<JukeboxCarousel>() ?? view.libraryPanel.AddComponent<JukeboxCarousel>();
        list.childControlHeight = false;
        list.childForceExpandHeight = false;

        Box(view.nowPlaying.rectTransform, 915, 286, 361, 39);
        view.nowPlaying.fontSize = 25;
        var nowTheme = view.nowPlaying.GetComponent<ThemeTextReceiver>();
        if (nowTheme != null) UnityEngine.Object.DestroyImmediate(nowTheme);
        view.nowPlaying.color = Brass;
        Box(view.selected.rectTransform, 915, 329, 360, 109);
        view.selected.fontSize = 46;
        view.selected.fontStyle = FontStyles.Bold;
        view.selected.enableAutoSizing = true;
        view.selected.fontSizeMin = 26;
        view.selected.fontSizeMax = 46;
        view.detail = ExtraText(panel, "ArtistStyle", 915, 446, 350, 53, 26);
        var sleeve = ImagePart("CoverAperture", panel, 1288, 281, 280, 280, Art("CoverAperture"), Color.white);
        var coverMask = sleeve.GetComponent<Mask>() ?? sleeve.gameObject.AddComponent<Mask>();
        coverMask.showMaskGraphic = false;
        view.cover.rectTransform.SetParent(sleeve.rectTransform, false);
        UIFactory.Stretch(view.cover.rectTransform);
        view.defaultCover = Art("CoverFallback");
        view.cover.preserveAspect = false;
        view.cover.sprite = view.defaultCover;
        ImagePart("SleeveBezel", panel, 1284, 277, 288, 288, Art("SleeveBezel"), Color.white);

        Skin(view.previous, 917, 638, 198, 73, false, Art("Previous"), Art("TransportRewind"));
        view.transportIcon = Skin(view.playPause, 1130, 638, 224, 73, true, Art("Play"), Art("TransportCenter"));
        view.playIcon = Art("Play");
        view.pauseIcon = Art("Pause");
        Skin(view.next, 1369, 638, 198, 73, false, Art("Next"), Art("TransportForward"));
        Skin(view.back, 710, 848, 320, 62, true, null, Art("TransportCenter"));
        view.back.image.type = Image.Type.Sliced;
        JukeboxRowView.ApplyButtonFeedback(view.back);
        view.back.image.color = Color.white;
        view.lyrics.gameObject.SetActive(false);
        view.background.gameObject.SetActive(false);
        Skin(view.delete, 1365, 546, 184, 58, true);
        Skin(view.full, 913, 325, 351, 108, true);
        var offerLabel = view.full.GetComponentInChildren<TextMeshProUGUI>(true);
        offerLabel.fontSizeMax = 25;
        offerLabel.margin = new Vector4(44, 19, 42, 19);
        view.preview.gameObject.SetActive(false);
        view.full.gameObject.SetActive(false);
        Box(view.status.rectTransform, 916, 771, 650, 30);
        view.status.fontSize = 19;
        view.elapsed = ExtraText(panel, "Elapsed", 917, 726, 88, 40, 27);
        view.duration = ExtraText(panel, "Duration", 1493, 726, 73, 40, 27);
        view.duration.alignment = TextAlignmentOptions.Right;
        ImagePart("ProgressTrack", panel, 1007, 744, 465, 7, null, new Color(.5f,.38f,.27f));
        view.progress = ImagePart("ProgressGlow", panel, 1007, 743, 0, 9, null, Color.cyan);
        view.progressKnob = ImagePart("ProgressKnob", panel, 993, 732, 28, 28, Art("Dial"), Color.white);
    }

    private static void AddTempoFury(JukeboxView view, RectTransform panel)
    {
        if (view.tempoFury == null)
        {
            var menu = AssetDatabase.LoadAssetAtPath<MainMenuView>(UIScreenPrefabContext.ScreensFolder + "/MainMenu.prefab");
            if (menu == null || menu.logo == null) return;
            view.tempoFury = UnityEngine.Object.Instantiate(menu.logo, panel);
            view.tempoFury.name = "TempoFuryBrand";
            Box((RectTransform)view.tempoFury.transform, 617.5f, 1, 505, 75);
        }
        var logo = view.tempoFury;
        if (logo.background != null) logo.background.gameObject.SetActive(false);
        if (logo.foreground != null) logo.foreground.gameObject.SetActive(false);
        foreach (var image in logo.GetComponentsInChildren<Image>(true)) image.raycastTarget = false;
        view.rockFuryLetter = Art("RockFuryF");
        view.ApplyBrand(StyledTitleView.MenuStyle(logo.library));
    }

    private static TextMeshProUGUI ExtraText(RectTransform panel, string name, float x, float y, float w, float h, int size) =>
        ResizeText(panel.Find(name)?.GetComponent<TextMeshProUGUI>() ?? Text(name, panel, x, y, w, h, size), x, y, w, h, size);

    private static TextMeshProUGUI ResizeText(TextMeshProUGUI label, float x, float y, float w, float h, int size)
    {
        Box(label.rectTransform, x, y, w, h);
        label.fontSize = size;
        return label;
    }

    private static Image ImagePart(string name, RectTransform panel, float x, float y, float w, float h, Sprite sprite, Color color)
    {
        var image = panel.Find(name)?.GetComponent<Image>() ?? UIFactory.CreatePanel(name, panel, color);
        Box(image.rectTransform, x, y, w, h);
        image.raycastTarget = false;
        image.sprite = sprite;
        image.color = color;
        return image;
    }

    private static void SkinLabel(Button button, Color color)
    {
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        var receiver = label.GetComponent<ThemeTextReceiver>();
        if (receiver != null) UnityEngine.Object.DestroyImmediate(receiver);
        label.color = color;
        label.fontStyle = FontStyles.Bold;
    }

    private static Image Skin(Button button, float x, float y, float w, float h, bool selected, Sprite icon = null, Sprite plate = null)
    {
        var rect = (RectTransform)button.transform;
        Box(rect, x, y, w, h);
        var image = button.GetComponent<Image>();
        image.sprite = plate ?? Classic(selected);
        image.type = plate == null ? Image.Type.Sliced : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = 2.7f;
        image.color = Color.white;
        SkinLabel(button, selected ? new Color(1,.94f,.81f) : new Color(.11f,.06f,.03f));
        if (icon == null) return null;
        button.GetComponentInChildren<TextMeshProUGUI>(true).gameObject.SetActive(false);
        return ImagePart("TransportIcon", rect, w / 2 - 34, h / 2 - 22, 68, 44, icon,
            selected ? new Color(1,.94f,.81f) : new Color(.11f,.06f,.03f));
    }

    private static JukeboxNeon Neon(RectTransform parent, string name, float x, float y, float w, float h,
        float radius, float phase, bool underline = false, float leftShear = 0, float rightShear = 0)
    {
        var rect = parent.Find(name) as RectTransform ?? UIFactory.CreateRect(name, parent);
        Box(rect, x, y, w, h);
        if (rect.GetComponent<CanvasRenderer>() == null) rect.gameObject.AddComponent<CanvasRenderer>();
        var light = rect.GetComponent<JukeboxNeon>() ?? rect.gameObject.AddComponent<JukeboxNeon>();
        light.Configure(radius, underline, phase, leftShear, rightShear);
        return light;
    }
    public static void PatchMenu(GameObject root)
    {
        var view = root.GetComponent<MainMenuView>();
        if (view == null) return;
        if (view.jukeboxButton == null)
        {
            var button = UnityEngine.Object.Instantiate(view.settingsButton, view.settingsButton.transform.parent);
            button.name = "JukeboxButton";
            button.onClick = new Button.ButtonClickedEvent();
            view.jukeboxButton = button;
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            var localized = label.GetComponent<LocalizedText>();
            if (localized != null) localized.key = "MainMenu.Jukebox";
            else UIScreenPrefabContext.Localized(label, "MainMenu.Jukebox");
            label.text = "JUKEBOX";
        }
        var jukebox = view.jukeboxButton.transform;
        var settings = view.settingsButton.transform;
        if (jukebox.parent == settings.parent && jukebox.GetSiblingIndex() > settings.GetSiblingIndex())
            jukebox.SetSiblingIndex(settings.GetSiblingIndex());

        // Keep the existing fanned art, but fit six actions instead of five. The mobile menu omits Quit.
        var buttons = new[] { view.playButton, view.rivalsButton, view.progressionButton, view.jukeboxButton, view.settingsButton, view.quitButton };
        var old = view.buttonSlots;
        if (old == null || old.Length < 2) return;
        Vector3 first = old[0], last = old[old.Length - 1];
        var slots = new Vector3[6];
        for (int i = 0; i < slots.Length; i++)
        {
            float t = i / 5f;
            slots[i] = Vector3.Lerp(first, last, t);
            var rt = (RectTransform)buttons[i].transform;
            rt.anchoredPosition = new Vector2(slots[i].x, slots[i].y);
            rt.localRotation = Quaternion.Euler(0, 0, slots[i].z);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, Mathf.Min(rt.sizeDelta.y, 84));
        }
        view.buttonSlots = slots;
    }
    public static void PatchLyrics(GameObject root)
    {
        var view = root.GetComponent<JukeboxView>();
        if (view == null) return;
        var panel = root.transform.Find("Content") as RectTransform;
        if (panel == null) throw new InvalidOperationException("Missing Jukebox Content panel");
        if (view.lyricsPanel == null)
        {
            view.libraryPanel = panel.Find("Library").gameObject;
            AddLyricsPanel(view, panel);
        }
        if (view.lyricsLock == null)
        {
            var icon = UIFactory.CreateRect("PremiumLock", (RectTransform)view.lyrics.transform);
            Box(icon, 12, 12, 24, 30); view.lyricsLock = icon.gameObject;
            void Part(string name, float x, float y, float w, float h)
            {
                var image = UIFactory.CreatePanel(name, icon, new Color(.75f, .77f, .8f));
                Box(image.rectTransform, x, y, w, h); image.raycastTarget = false;
            }
            Part("Body", 2, 13, 20, 15); Part("Top", 6, 3, 12, 4);
            Part("Left", 5, 5, 4, 10); Part("Right", 15, 5, 4, 10);
            view.lyrics.GetComponentInChildren<TextMeshProUGUI>(true).margin = new Vector4(40, 3, 12, 3);
        }
    }
    private static void AddLyricsPanel(JukeboxView view, RectTransform panel)
    {
        view.lyricsScroll = UIFactory.CreateScrollRect("SyncedLyrics", panel, out view.lyricsContent, 18);
        Box((RectTransform)view.lyricsScroll.transform, 0, 158, 1000, 665);
        view.lyricsPanel = view.lyricsScroll.gameObject;
        var lyricsHit = view.lyricsScroll.viewport.gameObject.AddComponent<Image>(); lyricsHit.color = new Color(1, 1, 1, .005f);
        var layout = view.lyricsContent.GetComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true; layout.childForceExpandHeight = false;
        view.lyricsLine = UIFactory.CreateText("LineTemplate", view.lyricsContent, "", 32, Color.white, TextAlignmentOptions.TopLeft);
        view.lyricsLine.textWrappingMode = TextWrappingModes.Normal;
        view.lyricsLine.richText = false; view.lyricsLine.raycastTarget = false;
        view.lyricsLine.margin = new Vector4(12, 2, 12, 2);
        view.lyricsLine.gameObject.SetActive(false); view.lyricsPanel.SetActive(false);
    }
    private static void Box(RectTransform rt, float x, float y, float w, float h) => UIFactory.SetBox(rt, TL, TL, TL, new Vector2(x, -y), new Vector2(w, h));
    private static TextMeshProUGUI Text(string name, RectTransform p, float x, float y, float w, float h, int size, string key = null)
    {
        var t = UIFactory.CreateText(name, p, "", size, Color.white, TextAlignmentOptions.MidlineLeft);
        Box(t.rectTransform, x, y, w, h);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        if (key != null) UIScreenPrefabContext.Localized(t, key);
        t.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        return t;
    }
    private static Button Button(string name, RectTransform p, float x, float y, float w, float h, string key, out TextMeshProUGUI label)
    {
        var b = UIFactory.CreateButton(name, p, "", out label);
        Box((RectTransform)b.transform, x, y, w, h);
        b.GetComponent<Image>().color = new Color(.1f, .28f, .34f, 1);
        var colors = b.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(.6f, .95f, 1);
        colors.pressedColor = new Color(.4f, .7f, .8f); colors.disabledColor = new Color(.4f, .4f, .4f); b.colors = colors;
        label.fontSize = 22; label.enableAutoSizing = true; label.fontSizeMin = 16; label.fontSizeMax = 22;
        label.margin = new Vector4(12, 3, 12, 3); label.raycastTarget = false;
        label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        if (key != null) UIScreenPrefabContext.Localized(label, key);
        return b;
    }
    private static GameObject Row()
    {
        var go = UIScreenPrefabContext.Root("JukeboxRow", new Vector2(960, 104));
        var rt = (RectTransform)go.transform;
        var view = go.AddComponent<JukeboxRowView>();
        view.background = go.AddComponent<Image>(); view.background.color = new Color(.12f, .15f, .2f, .9f);
        view.button = go.AddComponent<Button>(); view.button.targetGraphic = view.background;
        view.cover = UIFactory.CreatePanel("Cover", rt, Color.white); Box(view.cover.rectTransform, 12, 12, 80, 80);
        view.cover.preserveAspect = false; view.cover.raycastTarget = false;
        view.title = Text("Title", rt, 110, 8, 810, 32, 26); view.title.richText = false;
        view.detail = Text("ArtistStyleOrigin", rt, 110, 43, 810, 25, 19); view.detail.richText = false;
        view.state = Text("UnlockState", rt, 110, 73, 810, 22, 16); view.state.color = new Color(.6f, .82f, .87f);
        return go;
    }
    private static GameObject Screen()
    {
        var go = UIScreenPrefabContext.Root("Jukebox", Vector2.zero); var rt = (RectTransform)go.transform; UIFactory.Stretch(rt);
        var view = go.AddComponent<JukeboxView>();
        var bg = UIFactory.CreatePanel("Background", rt, new Color(.025f, .035f, .06f, 1)); UIFactory.Stretch(bg.rectTransform);
        var panel = UIFactory.CreateRect("Content", rt); UIFactory.SetBox(panel, C, C, C, Vector2.zero, new Vector2(1740, 980));
        var title = Text("Title", panel, 0, 0, 900, 60, 48, "Jukebox.Title"); title.fontStyle = FontStyles.Bold;
        Text("Subtitle", panel, 0, 64, 1200, 34, 23, "Jukebox.Subtitle");
        view.summary = Text("Summary", panel, 0, 105, 1000, 30, 21);
        view.back = Button("Back", panel, 1510, 10, 230, 56, "Jukebox.Back", out _);
        var scroll = UIFactory.CreateScrollRect("Library", panel, out view.list, 10);
        Box((RectTransform)scroll.transform, 0, 158, 1000, 665);
        var hit = scroll.viewport.gameObject.AddComponent<Image>(); hit.color = new Color(1, 1, 1, .005f);
        view.libraryPanel = scroll.gameObject;
        AddLyricsPanel(view, panel);
        view.selected = Text("SelectedSong", panel, 1050, 158, 690, 120, 32);
        view.selected.richText = false;
        view.cover = UIFactory.CreatePanel("SelectedCover", panel, Color.white); Box(view.cover.rectTransform, 1050, 294, 140, 140);
        view.cover.preserveAspect = false; view.cover.raycastTarget = false;
        view.nowPlaying = Text("NowPlaying", panel, 1220, 294, 520, 140, 24);
        view.nowPlaying.richText = false;
        view.preview = Button("Preview", panel, 1050, 455, 330, 62, "Jukebox.Preview", out _);
        view.full = Button("FullSong", panel, 1400, 455, 340, 62, "Jukebox.Full", out _);
        view.previous = Button("Previous", panel, 1050, 535, 210, 58, "Jukebox.Previous", out _);
        view.playPause = Button("PlayPause", panel, 1280, 535, 230, 58, null, out view.playLabel);
        view.next = Button("Next", panel, 1530, 535, 210, 58, "Jukebox.Next", out _);
        view.lyrics = Button("Lyrics", panel, 1050, 615, 330, 58, "Jukebox.Lyrics", out _);
        view.delete = Button("DeleteImported", panel, 1400, 695, 340, 58, "Jukebox.Delete", out _);
        view.background = Button("Background", panel, 1050, 845, 690, 60, null, out view.backgroundLabel);
        view.status = Text("Status", panel, 0, 925, 1740, 46, 20);
        // Scrollable modal for long lyrics as well as purchase information and delete confirmation.
        var dim = UIFactory.CreatePanel("Dialog", rt, new Color(0, 0, 0, .92f)); UIFactory.Stretch(dim.rectTransform); view.dialog = dim.gameObject;
        var modal = UIFactory.CreatePanel("Panel", dim.rectTransform, new Color(.055f, .08f, .12f, 1));
        UIFactory.SetBox(modal.rectTransform, C, C, C, Vector2.zero, new Vector2(1320, 860));
        view.dialogTitle = Text("Title", modal.rectTransform, 44, 26, 1232, 65, 36);
        view.dialogTitle.richText = false;
        var bodyScroll = UIFactory.CreateScrollRect("Body", modal.rectTransform, out var content);
        Box((RectTransform)bodyScroll.transform, 44, 118, 1232, 600);
        var hitBody = bodyScroll.viewport.gameObject.AddComponent<Image>(); hitBody.color = new Color(1, 1, 1, .005f);
        var body = UIFactory.CreateText("Text", content, "", 27, Color.white, TextAlignmentOptions.TopLeft);
        body.textWrappingMode = TextWrappingModes.Normal; body.richText = false; body.raycastTarget = false;
        var layout = content.GetComponent<VerticalLayoutGroup>(); layout.childControlHeight = true;
        body.gameObject.AddComponent<LayoutElement>();
        view.dialogBody = body;
        view.confirm = Button("Confirm", modal.rectTransform, 300, 760, 350, 60, null, out view.confirmLabel);
        view.cancel = Button("Close", modal.rectTransform, 680, 760, 350, 60, "Jukebox.Close", out _);
        dim.gameObject.SetActive(false);
        return go;
    }
}
#endif
