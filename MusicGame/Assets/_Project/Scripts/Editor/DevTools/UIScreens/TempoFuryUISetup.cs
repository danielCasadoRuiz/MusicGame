#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.UI;

/// <summary>
/// Tempo Fury art in the UI (idempotent, adds only what is missing — manual prefab edits are kept):
///   1. Copies AssetsMaterial/UI/Buttons/TempoFury_boton/&lt;Style&gt;/{normal,selected}.png → Art/UI/StyleButtons and
///      AssetsMaterial/UI/Titles/&lt;Style&gt;/{01_background,03_foreground,Letters/X}.png → Art/UI/Titles (full-rect sprites),
///      and fills Configs/UI/StyleSkinLibrary.asset (+ the TempoFuryBrush SDF title font).
///   2. Big titles use TempoFuryBrush SDF: countdown (3-2-1 TEMPO!), round intro (3-2-1 FURY!), round end (KO!),
///      finish banner, match result (YOU WIN / YOU LOSE, PERFECT!), runner end screen, both pause titles.
///   3. Pause buttons (Runner PauseMenu, Fight pause) get a classic layer + a style layer (StyleBlendButton) and a
///      StyleBlendGroup; the pause menus are relaid out once for the 6:1 art.
///   4. IntroScreen gets a StyledTitleView: TEMPO FURY rebuilt from background + 9 letter images + foreground.
/// Also run at the end of Build All UI Prefabs (Patch), so a Rebuild keeps all of it.
/// Menu: Tools > MusicGame > UI > Tempo Fury Titles + Style Buttons.
/// Batch: -executeMethod TempoFuryUISetup.RunFromCommandLine [-preview]  (Logs/TempoFuryUISetup.txt, Logs/TempoFuryPreview/*.png)
/// </summary>
public static class TempoFuryUISetup
{
    private const string ArtButtons = "Assets/_Project/Art/UI/StyleButtons";
    private const string ArtTitles = "Assets/_Project/Art/UI/Titles";
    private const string LibraryPath = "Assets/_Project/Configs/UI/StyleSkinLibrary.asset";
    private const string FontPath = "Assets/_Project/UI/Fonts/TempoFuryBrush/TempoFuryBrush SDF.asset";
    private const string ClassicFolder = "Classic_fixed";
    private static readonly string Screens = UIScreenPrefabContext.ScreensFolder;

    private static string SourceRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../AssetsMaterial/UI"));

    [MenuItem("Tools/MusicGame/UI/Tempo Fury Titles + Style Buttons", priority = 30)]
    public static void Menu() => Debug.Log(Run());

    [MenuItem("Tools/MusicGame/UI/Tempo Fury Preview PNGs", priority = 31)]
    public static void PreviewMenu() => Debug.Log(RenderPreviews());

    public static void RunFromCommandLine()
    {
        string report = Run();
        if (Environment.GetCommandLineArgs().Contains("-preview")) report += RenderPreviews();
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/TempoFuryUISetup.txt", report, Encoding.UTF8);
        EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }

    public static string Run()
    {
        var log = new StringBuilder("[TempoFuryUISetup]\n");
        ImportArt(log);
        var uiLib = AssetDatabase.LoadAssetAtPath<UIPrefabLibrarySO>(UIScreenPrefabContext.LibraryPath);
        Patch(new UIScreenPrefabContext(uiLib, false, log));
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ── 1. Art + library ─────────────────────────────────────────────────────────

    [Serializable] private class LetterBox { public string letter; public int[] box; }
    [Serializable] private class TitleLayout { public int[] fullCanvas; public int padding; public LetterBox[] letters; }

    private static void ImportArt(StringBuilder log)
    {
        string buttonsSrc = Path.Combine(SourceRoot, "Buttons/TempoFury_boton"), titlesSrc = Path.Combine(SourceRoot, "Titles");
        if (!Directory.Exists(buttonsSrc) || !Directory.Exists(titlesSrc)) { log.AppendLine($"  FAIL  source art missing under {SourceRoot}"); return; }

        var lib = AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(LibraryPath);
        if (lib == null)
        {
            lib = ScriptableObject.CreateInstance<StyleSkinLibrarySO>();
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
            AssetDatabase.CreateAsset(lib, LibraryPath);
            log.AppendLine($"  new   {LibraryPath}");
        }
        lib.titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (lib.titleFont == null) log.AppendLine($"  FAIL  title font missing: {FontPath}");

        lib.buttons.Clear();
        foreach (var dir in Directory.GetDirectories(buttonsSrc).OrderBy(d => d))
        {
            string name = Path.GetFileName(dir);
            bool classic = name == ClassicFolder;
            GameMusicStyle style = GameMusicStyle.Unknown;
            if (!classic && !Enum.TryParse(name, out style)) { log.AppendLine($"  skip  button folder '{name}' (not a GameMusicStyle)"); continue; }
            var normal = CopySprite(Path.Combine(dir, "normal.png"), $"{ArtButtons}/{name}/normal.png", 2048, log);
            var selected = CopySprite(Path.Combine(dir, "selected.png"), $"{ArtButtons}/{name}/selected.png", 2048, log);
            if (classic) { lib.classicNormal = normal; lib.classicSelected = selected; }
            else lib.buttons.Add(new StyleSkinLibrarySO.ButtonSkin { style = style, normal = normal, selected = selected });
        }
        if (lib.classicNormal == null) log.AppendLine($"  FAIL  classic button ({ClassicFolder}) missing");

        lib.titles.Clear();
        foreach (var dir in Directory.GetDirectories(titlesSrc).OrderBy(d => d))
        {
            string name = Path.GetFileName(dir);
            if (!Enum.TryParse(name, out GameMusicStyle style)) { log.AppendLine($"  skip  title folder '{name}'"); continue; }
            var skin = new StyleSkinLibrarySO.TitleSkin
            {
                style = style,
                background = CopySprite(Path.Combine(dir, "01_background.png"), $"{ArtTitles}/{name}/01_background.png", 2048, log),
                foreground = CopySprite(Path.Combine(dir, "03_foreground.png"), $"{ArtTitles}/{name}/03_foreground.png", 2048, log),
                letters = StyleSkinLibrarySO.TitleLetters.Select(c =>
                    CopySprite(Path.Combine(dir, "Letters", c + ".png"), $"{ArtTitles}/{name}/Letters/{c}.png", 1024, log)).ToArray(),
            };
            if (skin.letters.Any(l => l == null)) log.AppendLine($"  FAIL  title '{name}' is missing letters");
            lib.titles.Add(skin);
        }
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        log.AppendLine($"  PASS  library: {lib.buttons.Count} styled buttons + classic, {lib.titles.Count} titles");
    }

    private static Sprite CopySprite(string source, string assetPath, int maxSize, StringBuilder log)
    {
        if (!File.Exists(source)) { log.AppendLine($"  FAIL  missing {source}"); return null; }
        string full = Path.GetFullPath(assetPath);
        bool copy = !File.Exists(full) || new FileInfo(full).Length != new FileInfo(source).Length
                    || File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(full);
        if (copy)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.Copy(source, full, true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        bool wrong = importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single
                     || importer.mipmapEnabled || settings.spriteMeshType != SpriteMeshType.FullRect || importer.maxTextureSize != maxSize;
        if (wrong)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = maxSize;
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;   // whole canvas: the glow margins / title placement stay exact
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    // ── 2-4. Prefab patches ──────────────────────────────────────────────────────

    /// <summary>Applies the font / button skins / styled title to the generated prefabs. Also called by UIScreenPrefabBuilder.</summary>
    public static void Patch(UIScreenPrefabContext ctx)
    {
        var lib = AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(LibraryPath);
        if (lib == null || lib.titleFont == null)
        {
            ctx.Log.AppendLine("  skip  Tempo Fury UI: no StyleSkinLibrary yet (Tools > MusicGame > UI > Tempo Fury Titles + Style Buttons)");
            return;
        }
        TitlePass(lib, ctx.Log);
        ctx.Patch($"{Screens}/PauseMenu.prefab", r => PatchRunnerPause(r, lib, ctx.Log));
        ctx.Patch($"{Screens}/FightHud.prefab", r => PatchFightPause(r, lib, ctx.Log));
        ctx.Patch($"{Screens}/IntroScreen.prefab", r => PatchIntroTitle(r, lib, ctx.Log));
    }

    // ── Titles: every title / header text of the UI prefabs gets a TitleText (font, ivory, tier size) ──

    private const TitleTier D = TitleTier.Display, T = TitleTier.ScreenTitle, S = TitleTier.Section;

    /// <summary>"ViewType.field" (or "PrefabName|ViewType.field" when the view type is shared by other rows) → tier.</summary>
    private static readonly Dictionary<string, TitleTier> FieldTiers = new()
    {
        ["CountdownScreenView.numberText"] = D, ["RoundIntroView.text"] = D, ["RoundEndView.text"] = D,
        ["FinishBannerView.text"] = D, ["MatchResultView.perfectBanner"] = D, ["VersusScreenView.vsText"] = D,

        ["MatchResultView.titleText"] = T, ["EndScreenView.titleText"] = T, ["PauseView.titleText"] = T,
        ["FightHudView.pauseTitleText"] = T, ["MainMenuView.titleText"] = T, ["MainMenuView.settingsTitleText"] = T,
        ["SongSelectionView.titleText"] = T, ["OpponentSelectionView.titleText"] = T, ["ProfileScreenView.titleText"] = T,
        ["HelpScreenView.titleText"] = T, ["AnalyzingScreenView.titleText"] = T, ["NextSongTransitionView.headerText"] = T,
        ["IntroScreenView.titleText"] = T,

        ["MatchResultView.rewardAdTitleText"] = S, ["ProfileScreenView.strengthHeader"] = S, ["ProfileScreenView.stylesHeader"] = S,
        ["RivalCollectionView.detailTitle"] = S, ["ProgressionLevelHeaderView.title"] = S, ["TipCardView.titleText"] = S,
        ["CampaignPopup._title"] = S, ["BonusConversionView.titleText"] = S, ["HelpScreenView.headerPrefab"] = S,
        ["EvaluationRow_Header|EvaluationRowView.label"] = S, ["BossHeaderRow|ProgressionTextRowView.label"] = S,
    };

    /// <summary>Titles that are plain localized labels in their prefab (no view field).</summary>
    private static readonly Dictionary<string, TitleTier> KeyTiers = new()
    {
        ["Rivals.Title"] = T, ["Progression.Title"] = T, ["Settings.Title"] = T, ["MainMenu.Title"] = T,
        ["SongSelection.Title"] = T, ["OpponentSelection.Title"] = T, ["NextSong.Header"] = T, ["Intro.Title"] = T,
        ["Fight.Paused"] = T, ["Fight.Versus"] = D, ["Countdown.Finish"] = D, ["MatchResult.WatchAdTitle"] = S,
        // Not "Eval.Perfect": besides the result banner (a field above) it is the small PERFECT tag of the Fight HUD.
    };

    /// <summary>Prefabs whose only text is a header.</summary>
    private static readonly Dictionary<string, TitleTier> WholePrefabTiers = new() { ["HelpHeader"] = S };

    private static void TitlePass(StyleSkinLibrarySO lib, StringBuilder log)
    {
        foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { Screens }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
        {
            string prefabName = Path.GetFileNameWithoutExtension(path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var found = new Dictionary<TMP_Text, TitleTier>();
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    foreach (var f in mb.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    {
                        if (!typeof(TMP_Text).IsAssignableFrom(f.FieldType)) continue;
                        string key = $"{mb.GetType().Name}.{f.Name}";
                        if (!FieldTiers.TryGetValue($"{prefabName}|{key}", out var tier) && !FieldTiers.TryGetValue(key, out tier)) continue;
                        if (f.GetValue(mb) is TMP_Text t && t != null) found[t] = tier;
                    }
                    if (mb is LocalizedText loc && loc.key != null && KeyTiers.TryGetValue(loc.key, out var keyTier)
                        && mb.TryGetComponent<TMP_Text>(out var lt) && !found.ContainsKey(lt)) found[lt] = keyTier;
                }
                if (WholePrefabTiers.TryGetValue(prefabName, out var whole))
                    foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) found[t] = whole;

                // A nested prefab's texts are patched in their own prefab.
                var mine = found.Where(kv => !PrefabUtility.IsPartOfPrefabInstance(kv.Key)).ToList();
                if (mine.Count == 0) continue;
                foreach (var (text, tier) in mine) MakeTitle(text, tier, lib, root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                log.AppendLine($"  title {path}: " + string.Join(", ", mine.Select(kv => $"{kv.Key.name}={kv.Value}")));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    private static void MakeTitle(TMP_Text text, TitleTier tier, StyleSkinLibrarySO lib, Transform prefabRoot)
    {
        // Ivory + one font for every title: the theme colour/font receivers would override them.
        foreach (var receiver in text.GetComponents<ThemeTextReceiver>()) UnityEngine.Object.DestroyImmediate(receiver);
        var title = C<TitleText>(text.gameObject) ?? text.gameObject.AddComponent<TitleText>();
        title.library = lib;
        title.tier = tier;
        title.Apply();

        // Room for the tier size: height for one line, width so auto-size does not shrink a big title much.
        float size = lib.TitleSize(tier), need = size * 1.3f;
        var rt = text.rectTransform;
        var group = rt.parent != null ? rt.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
        if (group != null && group.childControlHeight)
        {
            var le = C<LayoutElement>(rt.gameObject) ?? rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = Mathf.Max(le.minHeight, need);
            le.preferredHeight = Mathf.Max(le.preferredHeight, need);
        }
        else if (Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y) && rt.sizeDelta.y < need)
            GrowDown(rt, need - rt.sizeDelta.y);
        else if (rt.parent == prefabRoot)   // a header row stretched over its row prefab: grow the row
        {
            var row = (RectTransform)prefabRoot;
            float rowNeed = need - rt.offsetMin.y + rt.offsetMax.y;
            row.sizeDelta = new Vector2(row.sizeDelta.x, Mathf.Max(row.sizeDelta.y, rowNeed));
            var le = C<LayoutElement>(row.gameObject);
            if (le != null) { le.minHeight = Mathf.Max(le.minHeight, rowNeed); le.preferredHeight = Mathf.Max(le.preferredHeight, rowNeed); }
        }
        float minWidth = tier == TitleTier.Display ? 1800f : tier == TitleTier.ScreenTitle ? 900f : 0f;
        if (minWidth > 0f && Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x) && rt.sizeDelta.x < minWidth)
            rt.sizeDelta = new Vector2(minWidth, rt.sizeDelta.y);
    }

    /// <summary>Grows a fixed-height rect by `dh` keeping its top edge, and moves down every sibling below it that
    /// shares its vertical anchor (the rest of a top-anchored screen), so a bigger title never covers them.</summary>
    private static void GrowDown(RectTransform rt, float dh)
    {
        float oldBottom = rt.anchoredPosition.y - rt.pivot.y * rt.sizeDelta.y;
        rt.sizeDelta += new Vector2(0f, dh);
        rt.anchoredPosition -= new Vector2(0f, (1f - rt.pivot.y) * dh);
        if (rt.parent == null) return;
        foreach (RectTransform sib in rt.parent)
        {
            if (sib == rt || !Mathf.Approximately(sib.anchorMin.y, rt.anchorMin.y) || !Mathf.Approximately(sib.anchorMax.y, rt.anchorMax.y)) continue;
            float sibTop = sib.anchoredPosition.y + (1f - sib.pivot.y) * sib.sizeDelta.y;
            if (sibTop <= oldBottom + 0.5f) sib.anchoredPosition -= new Vector2(0f, dh);
        }
    }

    private static void PatchRunnerPause(GameObject root, StyleSkinLibrarySO lib, StringBuilder log)
    {
        var view = root.GetComponent<PauseView>();
        if (view == null || view.overlayRoot == null || view.resumeButton == null) { log.AppendLine("  FAIL  PauseMenu.prefab: PauseView incomplete"); return; }
        EnsureGroup(view.overlayRoot, lib);
        var buttons = new (Button b, TextMeshProUGUI l)[]
            { (view.resumeButton, view.resumeButtonLabel), (view.restartButton, view.restartButtonLabel),
              (view.mainMenuButton, view.mainMenuButtonLabel), (view.bonusHelpButton, view.bonusHelpButtonLabel) }
            .Where(x => x.b != null).ToArray();
        bool added = buttons.Select(x => SkinButton(x.b, x.l, lib)).ToArray().Any(a => a);
        // Relaid out when the buttons are first skinned or the title tier size changed (other hand edits are kept).
        const float top = 28f, btnW = 480f, btnH = 80f, gap = 18f, bottom = 36f;
        float titleH = lib.screenTitleSize * 1.3f;
        float firstY = -(top + titleH + 12f);
        var panel = (RectTransform)view.resumeButton.transform.parent;
        panel.sizeDelta = new Vector2(600f, top + titleH + 12f + buttons.Length * btnH + (buttons.Length - 1) * gap + bottom);
        if (!added && Mathf.Abs(((RectTransform)buttons[0].b.transform).anchoredPosition.y - firstY) < 0.5f) return;
        var up = new Vector2(0.5f, 1f);
        if (view.titleText != null)
        {
            UIFactory.SetBox(view.titleText.rectTransform, up, up, up, new Vector2(0f, -top), new Vector2(900f, titleH));
            view.titleText.alignment = TextAlignmentOptions.Center;
        }
        for (int i = 0; i < buttons.Length; i++)
            UIFactory.SetBox((RectTransform)buttons[i].b.transform, up, up, up,
                new Vector2(0f, firstY - i * (btnH + gap)), new Vector2(btnW, btnH));
        log.AppendLine("  PASS  Runner pause: style buttons + layout");
    }

    private static void PatchFightPause(GameObject root, StyleSkinLibrarySO lib, StringBuilder log)
    {
        var view = root.GetComponent<FightHudView>();
        if (view == null || view.pausePanel == null || view.resumeButton == null) { log.AppendLine("  FAIL  FightHud.prefab: pause menu incomplete"); return; }
        EnsureGroup(view.pausePanel, lib);
        var buttons = new (Button b, TextMeshProUGUI l)[]
            { (view.resumeButton, view.resumeButtonLabel), (view.mainMenuButton, view.mainMenuButtonLabel), (view.movesHelpButton, view.movesHelpButtonLabel) }
            .Where(x => x.b != null).ToArray();
        if (!buttons.Select(x => SkinButton(x.b, x.l, lib)).ToArray().Any(added => added)) return;

        var c = new Vector2(0.5f, 0.5f);
        if (view.pauseTitleText != null)
        {
            UIFactory.SetBox(view.pauseTitleText.rectTransform, c, c, c, new Vector2(0f, 190f), new Vector2(900f, 150f));
        }
        for (int i = 0; i < buttons.Length; i++)
            UIFactory.SetBox((RectTransform)buttons[i].b.transform, c, c, c, new Vector2(0f, 50f - i * 98f), new Vector2(480f, 80f));
        log.AppendLine("  PASS  Fight pause: style buttons + layout");
    }

    private static void EnsureGroup(GameObject host, StyleSkinLibrarySO lib)
    {
        var group = C<StyleBlendGroup>(host) ?? host.AddComponent<StyleBlendGroup>();
        group.library = lib;
    }

    /// <returns>true when the skin was added now (false = already skinned, only its library refreshed).</returns>
    private static bool SkinButton(Button button, TextMeshProUGUI label, StyleSkinLibrarySO lib)
    {
        var existing = button.GetComponent<StyleBlendButton>();
        if (existing != null) { existing.library = lib; return false; }

        // The skin owns the look now: the theme receivers would re-tint the button / label over it.
        foreach (var receiver in button.GetComponents<ThemeColorReceiver>()) UnityEngine.Object.DestroyImmediate(receiver);
        if (label != null) foreach (var receiver in label.GetComponents<ThemeTextReceiver>()) UnityEngine.Object.DestroyImmediate(receiver);

        var hit = button.GetComponent<Image>();
        if (hit != null) hit.color = new Color(1f, 1f, 1f, 0f);   // stays the click target, invisible
        button.transition = Selectable.Transition.None;

        var skin = button.gameObject.AddComponent<StyleBlendButton>();
        skin.library = lib;
        skin.classicLayer = Layer("ClassicLayer", button.transform, 0, lib.classicNormal);
        skin.styleLayer = Layer("StyleLayer", button.transform, 1, null);
        skin.styleLayer.enabled = false;
        skin.label = label;
        if (label != null)
        {
            label.fontSize = 30f;
            label.color = lib.labelNormal;
            label.raycastTarget = false;
            label.transform.SetAsLastSibling();
        }
        return true;
    }

    private static Image Layer(string name, Transform parent, int sibling, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.transform.SetSiblingIndex(sibling);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        float sx = 480f / StyleSkinLibrarySO.ButtonBody.width, sy = 80f / StyleSkinLibrarySO.ButtonBody.height;
        rt.offsetMin = new Vector2(-64f * sx, -64f * sy); rt.offsetMax = new Vector2(64f * sx, 64f * sy);   // StyleBlendButton refits at runtime
        return img;
    }

    private static void PatchIntroTitle(GameObject root, StyleSkinLibrarySO lib, StringBuilder log)
    {
        var view = root.GetComponent<IntroScreenView>();
        if (view == null) { log.AppendLine("  FAIL  IntroScreen.prefab: no IntroScreenView"); return; }
        if (view.styledTitle != null) { view.styledTitle.library = lib; return; }

        string layoutPath = Path.Combine(SourceRoot, "Titles", lib.titles.Count > 0 ? lib.titles[0].style.ToString() : "Rock", "Letters/layout.json");
        if (!File.Exists(layoutPath)) { log.AppendLine($"  FAIL  title layout missing: {layoutPath}"); return; }
        var layout = JsonUtility.FromJson<TitleLayout>(File.ReadAllText(layoutPath));
        var canvasSize = new Vector2(layout.fullCanvas[0], layout.fullCanvas[1]);

        var parent = view.titleText != null ? view.titleText.transform.parent : root.transform;
        var titleRoot = new GameObject("StyledTitle", typeof(RectTransform)).GetComponent<RectTransform>();
        titleRoot.SetParent(parent, false);
        if (view.titleText != null) titleRoot.SetSiblingIndex(view.titleText.transform.GetSiblingIndex() + 1);
        var c = new Vector2(0.5f, 0.5f);
        UIFactory.SetBox(titleRoot, c, c, c, Vector2.zero, canvasSize);   // the title's own 1920×1080 canvas…
        titleRoot.localScale = Vector3.one * 0.8f;                         // …shown at 80 % of the screen

        var title = titleRoot.gameObject.AddComponent<StyledTitleView>();
        title.library = lib;
        title.background = FullImage("Background", titleRoot);
        var letters = new GameObject("Letters", typeof(RectTransform)).GetComponent<RectTransform>();
        letters.SetParent(titleRoot, false);
        UIFactory.Stretch(letters);
        title.letters = new Image[StyleSkinLibrarySO.TitleLetters.Length];
        for (int i = 0; i < title.letters.Length; i++)
        {
            string ch = StyleSkinLibrarySO.TitleLetters[i].ToString();
            var box = layout.letters.First(l => l.letter == ch).box;   // x, y (from the top-left), width, height; PNG = box + padding
            var img = NewImage(ch, letters);
            var corner = new Vector2(0f, 1f);
            UIFactory.SetBox(img.rectTransform, corner, corner, corner,
                new Vector2(box[0] - layout.padding, -(box[1] - layout.padding)),
                new Vector2(box[2] + 2 * layout.padding, box[3] + 2 * layout.padding));
            title.letters[i] = img;
        }
        title.foreground = FullImage("Foreground", titleRoot);
        title.Show(lib.Title(GameMusicStyle.Rock) != null ? GameMusicStyle.Rock : lib.titleFallback);   // editor preview
        view.styledTitle = title;
        log.AppendLine("  PASS  IntroScreen: styled TEMPO FURY title (9 separate letters)");
    }

    private static Image FullImage(string name, RectTransform parent)
    {
        var img = NewImage(name, parent);
        UIFactory.Stretch(img.rectTransform);
        return img;
    }

    private static Image NewImage(string name, RectTransform parent)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.raycastTarget = false;   // the full-screen skip button underneath keeps every click
        return img;
    }

    // ── Preview PNGs (edit mode, offscreen) ──────────────────────────────────────

    public static string RenderPreviews()
    {
        var log = new StringBuilder();
        var lib = AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(LibraryPath);
        if (lib == null) return "  FAIL  preview: no library\n";
        string outDir = Path.GetFullPath("Logs/TempoFuryPreview");
        Directory.CreateDirectory(outDir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        try
        {
            var style = GameMusicStyle.Rock;
            foreach (float w in new[] { 0f, 0.5f, 1f })
            {
                Shot(outDir, $"runner_pause_{style}_{w:0.0}.png", $"{Screens}/PauseMenu.prefab", r =>
                {
                    var v = r.GetComponent<PauseView>();
                    v.overlayRoot.SetActive(true);
                    if (v.pauseButtonRoot != null) v.pauseButtonRoot.SetActive(false);
                    if (v.cameraToggleButtonRoot != null) v.cameraToggleButtonRoot.SetActive(false);
                    Txt(v.titleText, "Pause.Title");
                    SetLabels((v.resumeButtonLabel, "Pause.Resume"), (v.restartButtonLabel, "Pause.RestartSong"),
                              (v.mainMenuButtonLabel, "Pause.MainMenu"), (v.bonusHelpButtonLabel, "Help.Pause.Bonuses"));
                    C<StyleBlendGroup>(v.overlayRoot)?.Apply(style, w);
                    C<StyleBlendButton>(v.resumeButton.gameObject)?.OnPointerEnter(null);
                }, log);
            }
            foreach (var s in new[] { GameMusicStyle.Electronic, GameMusicStyle.Jazzy, GameMusicStyle.Funky })
                Shot(outDir, $"fight_pause_{s}_0.8.png", $"{Screens}/FightHud.prefab", r =>
                {
                    var v = r.GetComponent<FightHudView>();
                    foreach (Transform child in r.transform) child.gameObject.SetActive(child.gameObject == v.pausePanel);
                    Txt(v.pauseTitleText, "Fight.Paused");
                    SetLabels((v.resumeButtonLabel, "Fight.Resume"), (v.mainMenuButtonLabel, "Fight.MainMenu"), (v.movesHelpButtonLabel, "Help.Pause.Moves"));
                    C<StyleBlendGroup>(v.pausePanel)?.Apply(s, 0.8f);
                    C<StyleBlendButton>(v.mainMenuButton.gameObject)?.OnPointerEnter(null);
                }, log);
            foreach (var s in new[] { GameMusicStyle.Rock, GameMusicStyle.Dreamy, GameMusicStyle.Jazzy })
                Shot(outDir, $"intro_title_{s}.png", $"{Screens}/IntroScreen.prefab", r =>
                {
                    var v = r.GetComponent<IntroScreenView>();
                    if (v.canvasGroup != null) v.canvasGroup.alpha = 1f;
                    v.titleText.gameObject.SetActive(false);
                    v.styledTitle.Show(s);
                }, log);
            foreach (var (file, key) in new[] { ("countdown_tempo.png", "Countdown.Go"), ("countdown_3.png", null) })
                Shot(outDir, file, $"{Screens}/CountdownScreen.prefab", r =>
                {
                    var v = r.GetComponent<CountdownScreenView>();
                    if (key != null) Txt(v.numberText, key); else v.numberText.text = "3";
                }, log);
            foreach (var screen in new[] { "MainMenu", "Collection/RivalCollectionScreen", "ProfileScreen", "Progression/ProgressionScreen",
                                           "SongSelection", "OpponentSelection", "EndScreen", "MatchResult", "HelpScreen" })
                Shot(outDir, $"screen_{Path.GetFileName(screen)}.png", $"{Screens}/{screen}.prefab", r =>
                {
                    foreach (var mb in r.GetComponentsInChildren<MonoBehaviour>(true))
                        if (mb != null && mb.GetType().GetField("root")?.GetValue(mb) is GameObject viewRoot && viewRoot != null) viewRoot.SetActive(true);
                    foreach (var loc in r.GetComponentsInChildren<LocalizedText>(true))
                        if (loc.TryGetComponent<TMP_Text>(out var t)) Txt(t, loc.key);
                    // Data-filled titles: sample text, so their size / place can be checked.
                    foreach (var title in r.GetComponentsInChildren<TitleText>(true))
                        if (title.TryGetComponent<TMP_Text>(out var tt) && string.IsNullOrWhiteSpace(tt.text)) tt.text = title.tier == TitleTier.Section ? "SECCIÓ" : "PERFIL";
                }, log);
            Shot(outDir, "round_intro_fury.png", $"{Screens}/RoundIntro.prefab", r => Txt(r.GetComponent<RoundIntroView>().text, "Fight.Banner"), log);
            Shot(outDir, "match_result_you_lose.png", $"{Screens}/MatchResult.prefab", r => Txt(r.GetComponent<MatchResultView>().titleText, "Fight.YouLose"), log);
        }
        finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        log.AppendLine($"  preview PNGs → {outDir}");
        return log.ToString();
    }

    // A missing component reads as a real null here (GetComponent's editor "fake null" breaks ?. / ??).
    private static T C<T>(GameObject go) where T : Component { var c = go != null ? go.GetComponent<T>() : null; return c != null ? c : null; }

    // Preview texts: plain Loc.Get (Loc.Set would also track the throwaway preview instances).
    private static void Txt(TMP_Text text, string key) { if (text != null) text.text = Loc.Get(key); }

    private static void SetLabels(params (TextMeshProUGUI label, string key)[] labels)
    {
        foreach (var (label, key) in labels) Txt(label, key);
    }

    private static void Shot(string outDir, string file, string prefabPath, Action<GameObject> setup, StringBuilder log)
    {
        const int W = 1920, H = 1080;
        var camGo = new GameObject("PreviewCamera", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.07f, 0.07f, 1f);
        cam.orthographic = true;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        var canvasGo = new GameObject("PreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10f;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(W, H);
        scaler.matchWidthOrHeight = 0.5f;
        GameObject instance = null;
        try
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), canvasGo.transform);
            instance.SetActive(true);
            UIFactory.Stretch((RectTransform)instance.transform);
            setup(instance);
            Canvas.ForceUpdateCanvases();
            foreach (var t in instance.GetComponentsInChildren<TMP_Text>()) t.ForceMeshUpdate();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            log.AppendLine($"  shot  {file}");
        }
        catch (Exception e) { log.AppendLine($"  FAIL  preview {file}: {e.Message}"); }
        finally
        {
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            UnityEngine.Object.DestroyImmediate(canvasGo);
            UnityEngine.Object.DestroyImmediate(camGo);
        }
    }
}
#endif
