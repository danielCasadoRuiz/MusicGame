#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generates the EDITABLE UI prefabs used by the runtime (UIPrefabLibrarySO, AppConfig.uiPrefabs):
///   Prefabs/UI/Components   Star, StarRating, EvaluationRow_*, EvaluationPanel, ComposerSpeechBubble,
///                           PresentationSkipButton, CampaignPopup, LanguageOption, LanguageSelector
///   Prefabs/UI/Progression  ProgressionScreen, LevelHeaderRow, ColumnHeaderRow, MatchRow, BossHeaderRow, BossStageRow
///   Prefabs/UI/Collection   RivalCollectionScreen, RivalCard
///   Art/UI/Star.png         the star sprite (replace it to restyle every star)
///
/// "Build Component Prefabs" creates ONLY what is missing — prefabs already in the project (and any manual
/// edits to them) are left untouched. "Rebuild Component Prefabs (overwrite)" regenerates them all from this
/// code (explicit, destructive: asks for confirmation). Nested prefabs (stars inside rows, the evaluation panel
/// inside the Progression detail) stay linked, so restyling Star.prefab restyles every star.
/// Batch: -executeMethod UIComponentPrefabBuilder.BuildFromCommandLine
/// </summary>
public static class UIComponentPrefabBuilder
{
    private const string Components = "Assets/_Project/Prefabs/UI/Components";
    private const string ProgressionFolder = "Assets/_Project/Prefabs/UI/Progression";
    private const string CollectionFolder = "Assets/_Project/Prefabs/UI/Collection";
    private const string StarSpritePath = "Assets/_Project/Art/UI/Star.png";
    private const string LibraryPath = "Assets/_Project/Configs/UI/UIPrefabLibrary.asset";

    private static readonly Color Gold = new(1f, 0.82f, 0.35f);
    private static readonly Color Dim = new(0.8f, 0.8f, 0.8f);

    private static readonly string[] ComponentSlots =
    {
        "starRating", "evaluationPanel", "speechBubble", "skipButton", "campaignPopup", "languageSelector",
        "progressionScreen", "progressionLevelHeader", "progressionColumnHeader", "progressionMatchRow",
        "progressionBossHeader", "progressionBossStage", "collectionScreen", "rivalCard",
    };

    private static bool s_rebuild;
    private static StringBuilder s_log;

    [MenuItem("Tools/MusicGame/UI/Build Component Prefabs")]
    public static void BuildMenu() => Debug.Log(Build(rebuild: false));

    [MenuItem("Tools/MusicGame/UI/Rebuild Component Prefabs (overwrite)")]
    public static void RebuildMenu()
    {
        if (!EditorUtility.DisplayDialog("Rebuild UI component prefabs",
                "Every generated UI component prefab will be OVERWRITTEN from code, losing manual edits. Continue?", "Rebuild", "Cancel")) return;
        Debug.Log(Build(rebuild: true));
    }

    public static void BuildFromCommandLine()
    {
        string report = Build(rebuild: System.Environment.GetCommandLineArgs().Contains("-rebuild"));
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/UIComponentPrefabBuilder.txt", report, Encoding.UTF8);
        EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }

    public static string Build(bool rebuild)
    {
        s_rebuild = rebuild;
        s_log = new StringBuilder($"[UIComponentPrefabBuilder] {(rebuild ? "REBUILD" : "build missing")}\n");
        foreach (var f in new[] { Components, ProgressionFolder, CollectionFolder, "Assets/_Project/Art/UI", "Assets/_Project/Configs/UI" }) EnsureFolder(f);
        var star = EnsureStarSprite();

        // Components
        var starPrefab = Prefab<Image>($"{Components}/Star.prefab", () => BuildStar(star));
        var rating = Prefab<StarRatingView>($"{Components}/StarRating.prefab", () => BuildStarRating(starPrefab.gameObject));
        var rHeader = Prefab<EvaluationRowView>($"{Components}/EvaluationRow_Header.prefab", () => BuildRow("EvaluationRow_Header", 20, Gold, FontStyles.Bold, null));
        var rLine = Prefab<EvaluationRowView>($"{Components}/EvaluationRow_Line.prefab", () => BuildRow("EvaluationRow_Line", 16, Color.white, FontStyles.Normal, null));
        var rNote = Prefab<EvaluationRowView>($"{Components}/EvaluationRow_Note.prefab", () => BuildRow("EvaluationRow_Note", 13, Dim, FontStyles.Normal, null));
        var rHigh = Prefab<EvaluationRowView>($"{Components}/EvaluationRow_Highlight.prefab", () => BuildRow("EvaluationRow_Highlight", 17, Gold, FontStyles.Bold, null));
        var rWarn = Prefab<EvaluationRowView>($"{Components}/EvaluationRow_Warning.prefab", () => BuildRow("EvaluationRow_Warning", 14, new Color(1f, 0.6f, 0.5f), FontStyles.Normal, null));
        var rStars = Prefab<EvaluationRowView>($"{Components}/EvaluationRow_Stars.prefab", () => BuildRow("EvaluationRow_Stars", 16, Color.white, FontStyles.Normal, rating));
        var rStarsS = Prefab<EvaluationRowView>($"{Components}/EvaluationRow_StarsSmall.prefab", () => BuildRow("EvaluationRow_StarsSmall", 14, Dim, FontStyles.Normal, rating));
        var panel = Prefab<EvaluationPanel>($"{Components}/EvaluationPanel.prefab", () => BuildEvaluationPanel(rHeader, rLine, rNote, rHigh, rWarn, rStars, rStarsS));
        var bubble = Prefab<ComposerSpeechBubble>($"{Components}/ComposerSpeechBubble.prefab", BuildBubble);
        var skip = Prefab<FightPresentationSkip>($"{Components}/PresentationSkipButton.prefab", BuildSkip);
        var popup = Prefab<CampaignPopup>($"{Components}/CampaignPopup.prefab", BuildPopup);
        var option = Prefab<LanguageOptionView>($"{Components}/LanguageOption.prefab", BuildLanguageOption);
        var selector = Prefab<LanguageSelectorView>($"{Components}/LanguageSelector.prefab", () => BuildLanguageSelector(option));

        // Progression
        var pScreen = Prefab<ProgressionScreenView>($"{ProgressionFolder}/ProgressionScreen.prefab", () => BuildProgressionScreen(panel));
        var pHeader = Prefab<ProgressionLevelHeaderView>($"{ProgressionFolder}/LevelHeaderRow.prefab", () => BuildLevelHeader(rating));
        var pCols = Prefab<RectTransform>($"{ProgressionFolder}/ColumnHeaderRow.prefab", BuildColumnHeader);
        var pRow = Prefab<ProgressionMatchRowView>($"{ProgressionFolder}/MatchRow.prefab", () => BuildMatchRow(rating));
        var pBossH = Prefab<ProgressionTextRowView>($"{ProgressionFolder}/BossHeaderRow.prefab", () => BuildTextRow("BossHeaderRow", 56f, new Color(0.35f, 0.25f, 0.05f, 0.35f), 18, null));
        var pBossS = Prefab<ProgressionTextRowView>($"{ProgressionFolder}/BossStageRow.prefab", () => BuildTextRow("BossStageRow", 60f, new Color(1f, 1f, 1f, 0.05f), 16, rating));

        // Collection
        var card = Prefab<RivalCardView>($"{CollectionFolder}/RivalCard.prefab", BuildCard);
        var cScreen = Prefab<RivalCollectionView>($"{CollectionFolder}/RivalCollectionScreen.prefab", BuildCollectionScreen);

        // Library + AppConfig
        var lib = AssetDatabase.LoadAssetAtPath<UIPrefabLibrarySO>(LibraryPath);
        if (lib == null) { lib = ScriptableObject.CreateInstance<UIPrefabLibrarySO>(); AssetDatabase.CreateAsset(lib, LibraryPath); s_log.AppendLine($"  new   {LibraryPath}"); }
        lib.starRating = rating; lib.evaluationPanel = panel; lib.speechBubble = bubble; lib.skipButton = skip; lib.campaignPopup = popup;
        lib.languageSelector = selector; lib.progressionScreen = pScreen; lib.progressionLevelHeader = pHeader;
        lib.progressionColumnHeader = pCols != null ? pCols.gameObject : null; lib.progressionMatchRow = pRow;
        lib.progressionBossHeader = pBossH; lib.progressionBossStage = pBossS; lib.collectionScreen = cScreen; lib.rivalCard = card;
        EditorUtility.SetDirty(lib);
        var app = Resources.Load<AppConfigSO>("AppConfig");
        if (app != null && app.uiPrefabs != lib) { app.uiPrefabs = lib; EditorUtility.SetDirty(app); s_log.AppendLine("  ok    AppConfig.uiPrefabs assigned"); }
        AssetDatabase.SaveAssets();

        // Validation: every component slot assigned (screen slots are checked by UIScreenPrefabBuilder).
        int missing = 0;
        foreach (var f in ComponentSlots)
            if (typeof(UIPrefabLibrarySO).GetField(f).GetValue(lib) as Object == null) { missing++; s_log.AppendLine($"  FAIL  library slot '{f}' empty"); }
        s_log.AppendLine(missing == 0 ? "  PASS  every component slot assigned" : $"  FAIL  {missing} empty slot(s)");
        return s_log.ToString();
    }

    // ── Generic ──────────────────────────────────────────────────────────────

    private static T Prefab<T>(string path, System.Func<GameObject> build) where T : Component
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !s_rebuild) { s_log.AppendLine($"  keep  {path}"); return existing.GetComponent<T>(); }
        var root = build();
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) if (t.font == null) t.font = TMP_Settings.defaultFontAsset;
        var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        s_log.AppendLine($"  {(existing != null ? "redo" : "new ")}  {path}");
        return saved != null ? saved.GetComponent<T>() : null;
    }

    private static GameObject Root(string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        ((RectTransform)go.transform).sizeDelta = size;
        return go;
    }

    private static RectTransform RT(GameObject go) => (RectTransform)go.transform;

    private static T Nested<T>(T prefab, Transform parent) where T : Component =>
        ((GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, parent)).GetComponent<T>();

    private static TextMeshProUGUI Text(Transform parent, string name, string text, int size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, FontStyles style = FontStyles.Normal)
    {
        var t = UIFactory.CreateText(name, (RectTransform)parent, text, size, color, align, style);
        t.raycastTarget = false;
        t.enableWordWrapping = true;
        if (t.font == null) t.font = TMP_Settings.defaultFontAsset;
        return t;
    }

    private static TextMeshProUGUI Localized(TextMeshProUGUI t, string key)
    {
        t.gameObject.AddComponent<LocalizedText>().key = key;
        t.text = key; // shown in the editor; replaced at runtime
        return t;
    }

    private static Button Button(Transform parent, string name, out TextMeshProUGUI label, UIColorToken bg = UIColorToken.ButtonSecondary, UIColorToken fg = UIColorToken.TextPrimary)
    {
        var b = UIFactory.CreateButton(name, (RectTransform)parent, "", out label);
        b.gameObject.AddComponent<ThemeColorReceiver>().Initialize(bg);
        label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(fg, UIFontToken.Body);
        if (label.font == null) label.font = TMP_Settings.defaultFontAsset;
        return b;
    }

    // ── Stars ────────────────────────────────────────────────────────────────

    private static Sprite EnsureStarSprite()
    {
        if (!File.Exists(StarSpritePath))
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float r = i % 2 == 0 ? 0.5f : 0.2f, a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                pts[i] = new Vector2(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r - 0.02f);
            }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                            if (Inside(new Vector2((x + (sx + 0.5f) / 4f) / size, (y + (sy + 0.5f) / 4f) / size), pts)) hits++;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, hits / 16f));
                }
            File.WriteAllBytes(StarSpritePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(StarSpritePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(StarSpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            s_log.AppendLine($"  new   {StarSpritePath}");
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(StarSpritePath);
    }

    private static bool Inside(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        return inside;
    }

    private static GameObject BuildStar(Sprite sprite)
    {
        var root = Root("Star", new Vector2(22f, 22f));
        var back = root.AddComponent<Image>();
        back.sprite = sprite; back.color = new Color(1f, 1f, 1f, 0.18f); back.raycastTarget = false;
        var fillRt = UIFactory.CreateRect("Fill", RT(root));
        UIFactory.Stretch(fillRt);
        var fill = fillRt.gameObject.AddComponent<Image>();
        fill.sprite = sprite; fill.color = new Color(1f, 0.82f, 0.25f); fill.raycastTarget = false;
        fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
        return root;
    }

    private static GameObject BuildStarRating(GameObject starPrefab)
    {
        var root = Root("StarRating", new Vector2(122f, 22f));
        var h = root.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 3f; h.childControlWidth = h.childControlHeight = false; h.childForceExpandWidth = h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;
        var le = root.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 122f; le.minHeight = le.preferredHeight = 22f;
        var fills = new Image[5]; var backs = new Image[5];
        for (int i = 0; i < 5; i++)
        {
            var s = (GameObject)PrefabUtility.InstantiatePrefab(starPrefab, root.transform);
            s.name = "Star" + (i + 1);
            backs[i] = s.GetComponent<Image>();
            fills[i] = s.transform.Find("Fill").GetComponent<Image>();
        }
        root.AddComponent<StarRatingView>().Bind(fills, backs);
        return root;
    }

    // ── Evaluation panel ─────────────────────────────────────────────────────

    private static GameObject BuildRow(string name, int fontSize, Color color, FontStyles style, StarRatingView ratingPrefab)
    {
        var root = Root(name, new Vector2(540f, 30f));
        var h = root.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f; h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
        root.AddComponent<LayoutElement>().minHeight = Mathf.Max(30f, fontSize + 10f);
        var label = Text(root.transform, "Label", name, fontSize, color, TextAlignmentOptions.MidlineLeft, style);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        StarRatingView stars = ratingPrefab != null ? Nested(ratingPrefab, root.transform) : null;
        root.AddComponent<EvaluationRowView>().Bind(label, stars);
        return root;
    }

    private static GameObject BuildEvaluationPanel(params EvaluationRowView[] rows)
    {
        var root = Root("EvaluationPanel", new Vector2(560f, 0f));
        var rt = RT(root);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f); rt.pivot = new Vector2(1f, 0.5f); rt.anchoredPosition = new Vector2(-40f, 40f);
        var bg = root.AddComponent<Image>(); bg.color = new Color(0f, 0f, 0f, 0.55f); bg.raycastTarget = false;
        var v = root.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(10, 10, 10, 10); v.spacing = 4f;
        v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        root.AddComponent<EvaluationPanel>().BindRows(rows[0], rows[1], rows[2], rows[3], rows[4], rows[5], rows[6]);
        return root;
    }

    // ── Speech bubble ────────────────────────────────────────────────────────

    private static GameObject BuildBubble()
    {
        var root = Root("ComposerSpeechBubble", Vector2.zero);
        UIFactory.Stretch(RT(root));
        var canvas = root.AddComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = 150; // above the countdown curtain + HUD; no raycaster
        var group = root.AddComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
        var tail = UIFactory.CreatePanel("Tail", RT(root), new Color(0.07f, 0.07f, 0.09f, 0.94f));
        tail.raycastTarget = false;
        tail.rectTransform.anchorMin = tail.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        tail.rectTransform.sizeDelta = new Vector2(22f, 22f);
        tail.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var body = UIFactory.CreatePanel("Bubble", RT(root), new Color(0.07f, 0.07f, 0.09f, 0.94f));
        body.raycastTarget = false;
        body.rectTransform.anchorMin = body.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        body.rectTransform.pivot = new Vector2(0.5f, 0f);
        body.rectTransform.sizeDelta = new Vector2(400f, 100f);
        var outline = body.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.82f, 0.35f, 0.85f); outline.effectDistance = new Vector2(2f, -2f);
        var speaker = Text(body.transform, "Speaker", "Composer", 24, Gold, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        var line = Text(body.transform, "Line", "Line", 30, Color.white, TextAlignmentOptions.TopLeft);
        foreach (var t in new[] { speaker.rectTransform, line.rectTransform }) { t.anchorMin = t.anchorMax = new Vector2(0f, 1f); t.pivot = new Vector2(0f, 1f); }
        var typewriter = line.gameObject.AddComponent<TypewriterText>();
        root.AddComponent<ComposerSpeechBubble>().Bind(body.rectTransform, tail.rectTransform, group, speaker, line, typewriter);
        return root;
    }

    // ── Skip button, popup, language selector ───────────────────────────────

    private static GameObject BuildSkip()
    {
        var root = Root("PresentationSkipButton", new Vector2(190f, 56f));
        var rt = RT(root);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f); rt.anchoredPosition = new Vector2(-36f, 36f);
        var img = root.AddComponent<Image>();
        var btn = root.AddComponent<Button>(); btn.targetGraphic = img;
        root.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        var label = Localized(Text(root.transform, "Label", "", 20, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "Fight.Skip");
        UIFactory.Stretch(label.rectTransform);
        label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        root.AddComponent<FightPresentationSkip>().Bind(btn, label);
        return root;
    }

    private static GameObject BuildPopup()
    {
        var root = Root("CampaignPopup", Vector2.zero);
        UIFactory.Stretch(RT(root));
        root.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);
        var panel = UIFactory.CreatePanel("Panel", RT(root), new Color(0.07f, 0.07f, 0.1f, 0.98f));
        UIFactory.SetBox(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 620f));
        var outline = panel.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(1f, 0.82f, 0.35f, 0.9f); outline.effectDistance = new Vector2(3f, -3f);
        var imgRt = UIFactory.CreateRect("Image", panel.rectTransform);
        UIFactory.SetBox(imgRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(200f, 200f));
        var image = imgRt.gameObject.AddComponent<Image>(); image.preserveAspect = true;
        var title = Text(panel.transform, "Title", "Title", 34, Gold, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -236f), new Vector2(760f, 50f));
        var body = Text(panel.transform, "Body", "Body", 20, Color.white, TextAlignmentOptions.Top);
        UIFactory.SetBox(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -296f), new Vector2(740f, 220f));
        var primary = Button(panel.transform, "Primary", out var pLabel, UIColorToken.ButtonPrimary, UIColorToken.Accent);
        UIFactory.SetBox(primary.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-150f, 28f), new Vector2(280f, 56f));
        var secondary = Button(panel.transform, "Secondary", out var sLabel);
        UIFactory.SetBox(secondary.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(150f, 28f), new Vector2(280f, 56f));
        root.AddComponent<CampaignPopup>().Bind(image, title, body, primary, pLabel, secondary, sLabel);
        return root;
    }

    private static GameObject BuildLanguageOption()
    {
        var root = Root("LanguageOption", new Vector2(110f, 40f));
        var img = root.AddComponent<Image>();
        var btn = root.AddComponent<Button>(); btn.targetGraphic = img;
        root.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        var outline = root.AddComponent<Outline>(); outline.effectColor = Gold; outline.effectDistance = new Vector2(3f, -3f); outline.enabled = false;
        var label = Text(root.transform, "Label", "Language", 16, Color.white, TextAlignmentOptions.Center);
        UIFactory.Stretch(label.rectTransform);
        label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        root.AddComponent<LanguageOptionView>().Bind(btn, label, outline);
        return root;
    }

    private static GameObject BuildLanguageSelector(LanguageOptionView option)
    {
        var root = Root("LanguageSelector", new Vector2(380f, 74f));
        UIFactory.SetBox(RT(root), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -62f), new Vector2(380f, 74f));
        var label = Localized(Text(root.transform, "Label", "", 14, Dim, TextAlignmentOptions.Left), "Settings.Language");
        UIFactory.SetBox(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(360f, 24f));
        label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        var options = UIFactory.CreateRect("Options", RT(root));
        UIFactory.SetBox(options, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(360f, 40f));
        var h = options.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = true;
        root.AddComponent<LanguageSelectorView>().Bind(options, option);
        return root;
    }

    // ── Progression ──────────────────────────────────────────────────────────

    private static GameObject BuildProgressionScreen(EvaluationPanel panelPrefab)
    {
        var root = Root("ProgressionScreen", Vector2.zero);
        UIFactory.Stretch(RT(root));
        var view = root.AddComponent<ProgressionScreenView>();
        var dim = UIFactory.CreatePanel("Dim", RT(root), new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);
        var title = Localized(Text(root.transform, "Title", "", 34, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "Progression.Title");
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(1000f, 46f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.summary = Text(root.transform, "Summary", "Summary", 18, Color.white, TextAlignmentOptions.Center);
        UIFactory.SetBox(view.summary.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(1500f, 28f));
        view.requirements = Text(root.transform, "Requirements", "Requirements", 17, new Color(1f, 0.85f, 0.45f), TextAlignmentOptions.Center);
        UIFactory.SetBox(view.requirements.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(1500f, 28f));
        view.seal = Text(root.transform, "Seal", "", 20, new Color(1f, 0.9f, 0.4f), TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(view.seal.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -146f), new Vector2(1500f, 30f));
        var scroll = UIFactory.CreateScrollRect("Levels", RT(root), out var content, 6f);
        UIFactory.SetBox(scroll.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1500f, -330f));
        view.listContent = content;
        view.challengeButton = Button(root.transform, "ChallengeButton", out view.challengeLabel, UIColorToken.ButtonPrimary, UIColorToken.Accent);
        UIFactory.SetBox(view.challengeButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-170f, 36f), new Vector2(420f, 54f));
        view.backButton = Button(root.transform, "BackButton", out var backLabel);
        Localized(backLabel, "Rivals.Back");
        UIFactory.SetBox(view.backButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(170f, 36f), new Vector2(240f, 54f));
        view.detailRoot = UIFactory.CreateRect("Detail", RT(root));
        UIFactory.Stretch(view.detailRoot);
        var ddim = UIFactory.CreatePanel("DetailBackdrop", view.detailRoot, new Color(0f, 0f, 0f, 0.82f));
        UIFactory.Stretch(ddim.rectTransform);
        view.detailBackdrop = ddim.gameObject.AddComponent<Button>();
        view.detailPanel = Nested(panelPrefab, view.detailRoot);
        var dr = view.detailPanel.Rect;
        dr.anchorMin = dr.anchorMax = dr.pivot = new Vector2(0.5f, 0.5f); dr.anchoredPosition = Vector2.zero; dr.sizeDelta = new Vector2(760f, 0f);
        view.detailRoot.gameObject.SetActive(false);
        return root;
    }

    private static GameObject BuildLevelHeader(StarRatingView rating)
    {
        var root = Root("LevelHeaderRow", new Vector2(1500f, 74f));
        var img = root.AddComponent<Image>(); img.color = new Color(1f, 1f, 1f, 0.12f);
        var view = root.AddComponent<ProgressionLevelHeaderView>();
        view.button = root.AddComponent<Button>(); view.button.targetGraphic = img;
        view.title = Text(root.transform, "Title", "► LEVEL 1", 18, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        UIFactory.SetBox(view.title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 12f), new Vector2(560f, 30f));
        view.line = Text(root.transform, "Line", "This lap 0/5", 14, Dim, TextAlignmentOptions.MidlineLeft);
        UIFactory.SetBox(view.line.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, -14f), new Vector2(560f, 26f));
        (view.runnerLabel, view.runnerStars) = Average(root.transform, rating, 600f, "Runner");
        (view.combatLabel, view.combatStars) = Average(root.transform, rating, 900f, "Combat");
        (view.totalLabel, view.totalStars) = Average(root.transform, rating, 1200f, "Total");
        return root;
    }

    private static (TextMeshProUGUI, StarRatingView) Average(Transform parent, StarRatingView rating, float x, string name)
    {
        var label = Text(parent, name + "Label", name, 14, Color.white);
        UIFactory.SetBox(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 14f), new Vector2(290f, 26f));
        var stars = Nested(rating, parent);
        stars.name = name + "Stars";
        Place(stars, x, -16f);
        return (label, stars);
    }

    private static void Place(StarRatingView stars, float x, float y)
    {
        var r = (RectTransform)stars.transform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f); r.pivot = new Vector2(0f, 0.5f); r.anchoredPosition = new Vector2(x, y);
    }

    private static GameObject BuildColumnHeader()
    {
        var root = Root("ColumnHeaderRow", new Vector2(1500f, 30f));
        root.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
        var dim = new Color(1f, 1f, 1f, 0.6f);
        (string key, float x, float w)[] cols = { ("Progression.ColMatch", 40f, 170f), ("Progression.ColRunner", 220f, 330f), ("Progression.ColCombat", 580f, 400f), ("Progression.Total", 1060f, 160f) };
        foreach (var (key, x, w) in cols)
        {
            var t = Localized(Text(root.transform, key, "", 13, dim), key);
            UIFactory.SetBox(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(w, 26f));
        }
        return root;
    }

    private static GameObject BuildMatchRow(StarRatingView rating)
    {
        var root = Root("MatchRow", new Vector2(1500f, 84f));
        var view = root.AddComponent<ProgressionMatchRowView>();
        view.background = root.AddComponent<Image>(); view.background.color = view.oddColor;
        view.button = root.AddComponent<Button>(); view.button.targetGraphic = view.background;
        view.numberAndStatus = Text(root.transform, "NumberStatus", "MATCH 1", 17, Color.white);
        UIFactory.SetBox(view.numberAndStatus.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(170f, 60f));
        view.runner = Text(root.transform, "Runner", "Race: —", 15, Color.white);
        UIFactory.SetBox(view.runner.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(220f, 14f), new Vector2(330f, 40f));
        view.runnerStars = Nested(rating, root.transform); view.runnerStars.name = "RunnerStars"; Place(view.runnerStars, 220f, -22f);
        var portraitRt = UIFactory.CreateRect("Portrait", RT(root));
        UIFactory.SetBox(portraitRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(580f, 0f), new Vector2(64f, 76f));
        view.portrait = portraitRt.gameObject.AddComponent<Image>(); view.portrait.preserveAspect = true; view.portrait.raycastTarget = false;
        view.combat = Text(root.transform, "Combat", "Fight: —", 15, Color.white);
        UIFactory.SetBox(view.combat.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(656f, 14f), new Vector2(330f, 40f));
        view.combatStars = Nested(rating, root.transform); view.combatStars.name = "CombatStars"; Place(view.combatStars, 656f, -22f);
        var total = Localized(Text(root.transform, "TotalLabel", "", 15, Color.white), "Progression.Total");
        UIFactory.SetBox(total.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(1060f, 14f), new Vector2(120f, 40f));
        view.totalStars = Nested(rating, root.transform); view.totalStars.name = "TotalStars"; Place(view.totalStars, 1060f, -22f);
        return root;
    }

    private static GameObject BuildTextRow(string name, float height, Color bg, int fontSize, StarRatingView rating)
    {
        var root = Root(name, new Vector2(1500f, height));
        root.AddComponent<Image>().color = bg;
        var view = root.AddComponent<ProgressionTextRowView>();
        view.label = Text(root.transform, "Label", name, fontSize, Color.white);
        UIFactory.SetBox(view.label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(rating != null ? 40f : 16f, 0f), new Vector2(rating != null ? 600f : 1400f, 40f));
        if (rating != null) { view.stars = Nested(rating, root.transform); Place(view.stars, 656f, 0f); }
        return root;
    }

    // ── Collection ───────────────────────────────────────────────────────────

    private static GameObject BuildCard()
    {
        var root = Root("RivalCard", new Vector2(172f, 252f));
        var view = root.AddComponent<RivalCardView>();
        var img = root.AddComponent<Image>(); img.color = new Color(1f, 1f, 1f, 0.10f);
        view.button = root.AddComponent<Button>(); view.button.targetGraphic = img;
        var portraitRt = UIFactory.CreateRect("Portrait", RT(root));
        UIFactory.SetBox(portraitRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(-10f, 192f));
        view.portrait = portraitRt.gameObject.AddComponent<Image>(); view.portrait.preserveAspect = true; view.portrait.raycastTarget = false;
        view.nameText = Text(root.transform, "Name", "???", 14, Color.white, TextAlignmentOptions.Center);
        UIFactory.SetBox(view.nameText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(-8f, 24f));
        view.footer = Text(root.transform, "Footer", "0 / 4", 13, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Center);
        UIFactory.SetBox(view.footer.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(-8f, 22f));
        return root;
    }

    private static GameObject BuildCollectionScreen()
    {
        var root = Root("RivalCollectionScreen", Vector2.zero);
        UIFactory.Stretch(RT(root));
        var view = root.AddComponent<RivalCollectionView>();
        var dim = UIFactory.CreatePanel("Dim", RT(root), new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);
        var title = Localized(Text(root.transform, "Title", "", 34, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "Rivals.Title");
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(900f, 50f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.summary = Text(root.transform, "Summary", "0 / 48", 18, Color.white, TextAlignmentOptions.Center);
        UIFactory.SetBox(view.summary.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(900f, 28f));
        view.summary.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        view.grid = UIFactory.CreateRect("Grid", RT(root));
        UIFactory.SetBox(view.grid, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(6 * 172f + 5 * 18f, 2 * 252f + 22f));
        var grid = view.grid.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(172f, 252f); grid.spacing = new Vector2(18f, 22f);
        grid.startAxis = GridLayoutGroup.Axis.Horizontal; grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 6;
        view.grid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; // a 13th card (final rival) adds a row
        view.backButton = Button(root.transform, "BackButton", out var backLabel);
        Localized(backLabel, "Rivals.Back");
        UIFactory.SetBox(view.backButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(240f, 52f));

        view.detailRoot = UIFactory.CreateRect("Detail", RT(root));
        UIFactory.Stretch(view.detailRoot);
        UIFactory.Stretch(UIFactory.CreatePanel("DetailDim", view.detailRoot, new Color(0f, 0f, 0f, 0.8f)).rectTransform);
        var panel = UIFactory.CreatePanel("Panel", view.detailRoot, new Color(0.1f, 0.1f, 0.12f, 1f));
        UIFactory.SetBox(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 660f));
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);
        view.detailTitle = Text(panel.transform, "DetailTitle", "???", 28, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(view.detailTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1000f, 40f));
        view.detailTitle.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.detailRow = UIFactory.CreateRect("Versions", panel.rectTransform);
        UIFactory.SetBox(view.detailRow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 75f), new Vector2(1040f, 300f));
        var row = view.detailRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 18f; row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = row.childControlHeight = false; row.childForceExpandWidth = row.childForceExpandHeight = false;
        view.detailBio = Text(panel.transform, "Biography", "", 17, Color.white, TextAlignmentOptions.TopLeft);
        UIFactory.SetBox(view.detailBio.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -95f), new Vector2(980f, 140f));
        view.detailBio.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        view.detailClose = Button(panel.transform, "CloseDetail", out var closeLabel);
        Localized(closeLabel, "Rivals.Back");
        UIFactory.SetBox(view.detailClose.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(220f, 48f));
        view.detailRoot.gameObject.SetActive(false);
        return root;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
