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
        ctx.Library.jukeboxRow = ctx.Prefab<JukeboxRowView>(UIScreenPrefabContext.ItemsFolder + "/JukeboxRow.prefab", Row);
        ctx.Library.jukebox = ctx.Prefab<JukeboxView>(UIScreenPrefabContext.ScreensFolder + "/Jukebox.prefab", Screen);
        ctx.Patch(UIScreenPrefabContext.ScreensFolder + "/Jukebox.prefab", PatchLyrics);
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
        view.lyricsLine.margin = new Vector4(20, 10, 20, 10);
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
        view.cover.preserveAspect = true; view.cover.raycastTarget = false;
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
        view.cover.preserveAspect = true; view.cover.raycastTarget = false;
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
