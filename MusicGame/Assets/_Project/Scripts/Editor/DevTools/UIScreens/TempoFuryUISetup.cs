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
///   4. MainMenu (PrimeraPantalla reference): on the left the composers (AssetsMaterial/UI/Composers.png) + a StyledTitleView (TEMPO FURY rebuilt from
///      background + 9 letter images + foreground) in this launch's random style, on the right the Tempo Fury buttons
///      in that same style. (The IntroScreen no longer has the styled title.)
///   5. Runner HUD (HudExample reference), from AssetsMaterial/UI/Runner HUD → Art/UI/RunnerHud: LiveHud.prefab gets the
///      style bar over the classic bar (RunnerHudSkin), the logo on its left, score + the 6 musical bonuses, then past the
///      separator Special and Lives (icons fade normal → classic), and the song progress with its times; PauseMenu's
///      pause button becomes the style pause glyph (normal / pressed) in the bar's round slot.
/// Also run at the end of Build All UI Prefabs (Patch), so a Rebuild keeps all of it.
/// Menu: Tools > MusicGame > UI > Tempo Fury Titles + Style Buttons.
/// Batch: -executeMethod TempoFuryUISetup.RunFromCommandLine [-preview]  (Logs/TempoFuryUISetup.txt, Logs/TempoFuryPreview/*.png)
/// </summary>
public static class TempoFuryUISetup
{
    private const string ArtButtons = "Assets/_Project/Art/UI/StyleButtons";
    private const string ArtTitles = "Assets/_Project/Art/UI/Titles";
    private const string ArtHud = "Assets/_Project/Art/UI/RunnerHud";

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
        ImportHud(lib, log);
        lib.composers = CopySprite(Path.Combine(SourceRoot, "Composers.png"), $"{ArtTitles}/Composers.png", 2048, log);

        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        log.AppendLine($"  PASS  library: {lib.buttons.Count} styled buttons + classic, {lib.titles.Count} titles, " +
                       $"{lib.hud.Count} HUD bars, {lib.bonuses.Count} bonus icons");
    }

    private static string HudSource => Path.Combine(SourceRoot, "Runner HUD");

    // Runner HUD/<Style>/{bar,pause,pause_selected}.png (Classical = the classic set) and Runner HUD/Bonus/<Kind>/{normal,classic}_front.png.
    private static void ImportHud(StyleSkinLibrarySO lib, StringBuilder log)
    {
        lib.hud.Clear();
        lib.bonuses.Clear();
        if (!Directory.Exists(HudSource)) { log.AppendLine($"  FAIL  source art missing: {HudSource}"); return; }
        foreach (var dir in Directory.GetDirectories(HudSource).OrderBy(d => d))
        {
            string name = Path.GetFileName(dir);
            if (name == "Bonus") continue;
            if (!Enum.TryParse(name, out GameMusicStyle style)) { log.AppendLine($"  skip  HUD folder '{name}'"); continue; }
            lib.hud.Add(new StyleSkinLibrarySO.HudSkin
            {
                style = style,
                bar = CopySprite(Path.Combine(dir, "bar.png"), $"{ArtHud}/{name}/bar.png", 4096, log),
                pause = CopySprite(Path.Combine(dir, "pause.png"), $"{ArtHud}/{name}/pause.png", 256, log),
                pauseSelected = CopySprite(Path.Combine(dir, "pause_selected.png"), $"{ArtHud}/{name}/pause_selected.png", 256, log),
            });
        }
        foreach (var dir in Directory.GetDirectories(Path.Combine(HudSource, "Bonus")).OrderBy(d => d))
        {
            string name = Path.GetFileName(dir);
            if (!Enum.TryParse(name, out BonusKind kind)) { log.AppendLine($"  skip  bonus folder '{name}'"); continue; }
            lib.bonuses.Add(new StyleSkinLibrarySO.BonusSkin
            {
                kind = kind,
                normal = CopySprite(Path.Combine(dir, "normal_front.png"), $"{ArtHud}/Bonus/{name}/normal_front.png", 512, log),
                classic = CopySprite(Path.Combine(dir, "classic_front.png"), $"{ArtHud}/Bonus/{name}/classic_front.png", 512, log),
            });
        }
        if (lib.ClassicHud == null) log.AppendLine("  FAIL  Runner HUD: no Classical bar (the classic every style fades to)");

        // The same final bonus art everywhere else (runner results bonus block, help, tips, fight HUD): HelpUIConfig.
        var help = AssetDatabase.LoadAssetAtPath<HelpUIConfigSO>("Assets/_Project/Resources/UI/HelpUIConfig.asset");
        if (help != null)
        {
            var icons = help.bonusIcons.ToList();
            foreach (var b in lib.bonuses)
            {
                if (b.normal == null) continue;
                int i = icons.FindIndex(x => x.kind == b.kind);
                var icon = new HelpUIConfigSO.BonusIcon { kind = b.kind, sprite = b.normal, finalArt = true };
                if (i < 0) icons.Add(icon); else icons[i] = icon;
            }
            help.bonusIcons = icons.ToArray();
            EditorUtility.SetDirty(help);
        }
        else log.AppendLine("  FAIL  Resources/UI/HelpUIConfig.asset missing (bonus icons not updated)");

        // The curtain the classic bar is drawn with (an asset, so the shader ships in builds).
        var shader = Shader.Find("MusicGame/UI/CurtainFade");
        if (shader == null) { log.AppendLine("  FAIL  shader MusicGame/UI/CurtainFade missing"); return; }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(CurtainMaterialPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, CurtainMaterialPath);
        }
        mat.shader = shader;
        mat.SetFloat("_Edge", -1f);   // the prefab shows the classic bar whole
        mat.SetFloat("_Softness", lib.runnerCurtainSoftness);
        EditorUtility.SetDirty(mat);
        lib.curtainMaterial = mat;
    }

    private const string CurtainMaterialPath = ArtHud + "/CurtainFade.mat";

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
        return ConfigureSprite(assetPath, maxSize);
    }

    private static Sprite ConfigureSprite(string assetPath, int maxSize)
    {
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
        ctx.Patch($"{Screens}/IntroScreen.prefab", r => RemoveIntroTitle(r, ctx.Log));
        ctx.Patch($"{Screens}/MainMenu.prefab", r => PatchMainMenu(r, lib, ctx.Log));
        if (lib.ClassicHud != null) ctx.Patch($"{Screens}/LiveHud.prefab", r => PatchLiveHud(r, lib, ctx.Log));
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
        if (lib.ClassicHud != null) PatchHudPauseButton(view, lib, log);
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

    // The styled title lives in the main menu now (the PrimeraPantalla reference); the intro keeps its plain title.
    private static void RemoveIntroTitle(GameObject root, StringBuilder log)
    {
        var old = root.GetComponentInChildren<StyledTitleView>(true);
        if (old == null) return;
        UnityEngine.Object.DestroyImmediate(old.gameObject);
        var view = root.GetComponent<IntroScreenView>();
        if (view != null && view.titleText != null) view.titleText.gameObject.SetActive(true);
        log.AppendLine("  PASS  IntroScreen: styled title removed (now in the main menu)");
    }

    /// <summary>TEMPO FURY rebuilt on its own 1920×1080 canvas (centre-anchored rect, the caller places / scales it):
    /// background, the nine letters at their layout.json boxes, foreground.</summary>
    private static StyledTitleView BuildStyledTitle(string name, Transform parent, StyleSkinLibrarySO lib, StringBuilder log)
    {
        string layoutPath = Path.Combine(SourceRoot, "Titles", lib.titles.Count > 0 ? lib.titles[0].style.ToString() : "Rock", "Letters/layout.json");
        if (!File.Exists(layoutPath)) { log.AppendLine($"  FAIL  title layout missing: {layoutPath}"); return null; }
        var layout = JsonUtility.FromJson<TitleLayout>(File.ReadAllText(layoutPath));
        var canvasSize = new Vector2(layout.fullCanvas[0], layout.fullCanvas[1]);

        var titleRoot = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        titleRoot.SetParent(parent, false);
        var c = new Vector2(0.5f, 0.5f);
        UIFactory.SetBox(titleRoot, c, c, c, Vector2.zero, canvasSize);

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
        return title;
    }

    // ── Main menu (PrimeraPantalla): composers above the TEMPO FURY title, the buttons coming out from behind its right
    // side; the whole group centred on the screen. Laid out on every run (screen points, 1920×1080, top-left origin). ──

    /// <summary>A screen point (1920×1080, top-left origin) as a centre-anchored position.</summary>
    private static Vector2 ScreenPos(float x, float y) => new(x - 960f, 540f - y);
    // Layout measured on AssetsMaterial/References/PrimeraPantalla_unity.png (1920×1080 screen px, top-left origin),
    // regularised: logo group (composers over the TEMPO FURY title) + the plates fanning out from behind it.
    private const float MenuTitleScale = 0.48f;                              // title content 852×426
    private static readonly Vector2 MenuTitleTopLeft = new(309.24f, 427f);    // title content's top-left on screen
    private static readonly Rect MenuComposers = new(390f, 151f, 750f, 423f);  // composers.png 1670×942 at 0.449
    // The measured layout spans x 300…1540 (title's left stroke … top plate's tip) and y 150…860: this offset
    // centres that whole block on the screen, leaving room underneath for the campaign status.
    private static readonly Vector2 MenuCentreOffset = new(40f, -25f);
    // Whole logo group (composers + title) nudged by hand in Play mode (2026-11-01).
    private static readonly Vector2 MenuLogoOffset = new(-6f, -53f);
    private static Vector2 MenuPos(float x, float y) => ScreenPos(x + MenuCentreOffset.x, y + MenuCentreOffset.y);
    private const float MenuButtonW = 470f, MenuButtonH = 100f, MenuButtonGap = 33f;
    // Fan: one slot per VISIBLE plate (top → bottom; the 5th only when QUIT shows). Right edge follows an arc
    // around the logo, and the tilt eases gradually from + (rising to the right) to flat. Hand-tuned in Play mode
    // (2026-11-01); QUIT's slot is extrapolated from the other four.
    private static readonly (float right, float centreY, float tilt)[] MenuButtonFan =
    {
        (1489f,    415f,    3.59f),
        (1471.7f,  548.23f, 2.233f),
        (1447.41f, 681.55f, 0.94f),
        (1415.11f, 814.98f, 0f),
        (1375f,    948.5f, -1f),
    };
    private const float MenuButtonHidden = 140f;   // left part of each button behind the title: the label centres on the rest

    private static void PatchMainMenu(GameObject root, StyleSkinLibrarySO lib, StringBuilder log)
    {
        var view = root.GetComponent<MainMenuView>();
        if (view == null || view.playButton == null) { log.AppendLine("  FAIL  MainMenu.prefab: MainMenuView incomplete"); return; }
        var screen = (RectTransform)view.playButton.transform.parent;
        var c = new Vector2(0.5f, 0.5f);

        if (view.logo == null)
        {
            var logo = UIFactory.CreateRect("Logo", screen);
            UIFactory.Stretch(logo);
            NewImage("Composers", logo);
            var title = BuildStyledTitle("StyledTitle", logo, lib, log);
            if (title == null) return;
            view.logo = title;
            log.AppendLine("  PASS  MainMenu: composers + styled TEMPO FURY logo");
        }
        view.logo.library = lib;
        var logoRoot = view.logo.transform.parent;
        ((RectTransform)logoRoot).anchoredPosition = MenuLogoOffset;
        logoRoot.SetAsLastSibling();   // over the buttons, which come out from behind it…
        var settings = view.settingsPanel != null ? view.settingsPanel.transform : null;
        if (settings != null && settings.parent == logoRoot.parent) settings.SetAsLastSibling();   // …but under the settings panel
        var composers = C<Image>(logoRoot.Find("Composers")?.gameObject);
        if (composers != null)
        {
            composers.sprite = lib.composers;
            UIFactory.SetBox(composers.rectTransform, c, c, c, MenuPos(MenuComposers.center.x, MenuComposers.center.y), MenuComposers.size);
        }
        // Title content (73,96)-(1847,983) of its 1920×1080 canvas → its top-left at MenuTitleTopLeft.
        float s = MenuTitleScale;
        var titleRt = (RectTransform)view.logo.transform;
        UIFactory.SetBox(titleRt, c, c, c, MenuPos(MenuTitleTopLeft.x + (960f - 73f) * s, MenuTitleTopLeft.y + (540f - 96f) * s), new Vector2(1920f, 1080f));
        titleRt.localScale = Vector3.one * s;
        if (view.titleText != null) view.titleText.gameObject.SetActive(false);   // the logo is the title

        EnsureGroup(view.root, lib);
        view.buttonStyle = C<StyleBlendGroup>(view.root);
        var buttons = new[] { view.playButton, view.rivalsButton, view.progressionButton, view.settingsButton, view.quitButton }.Where(b => b != null).ToArray();
        foreach (var b in buttons) SkinButton(b, b.GetComponentInChildren<TextMeshProUGUI>(true), lib);
        var slots = MenuButtonFan.Select(f => { var p = MenuPos(f.right - MenuButtonW / 2f, f.centreY); return new Vector3(p.x, p.y, f.tilt); }).ToArray();
        for (int i = 0; i < buttons.Length; i++)
        {
            var slot = slots[Mathf.Min(i, slots.Length - 1)];
            UIFactory.SetBox((RectTransform)buttons[i].transform, c, c, c, new Vector2(slot.x, slot.y), new Vector2(MenuButtonW, MenuButtonH));
            buttons[i].transform.localRotation = Quaternion.Euler(0f, 0f, slot.z);
            var label = buttons[i].GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null) continue;
            // Spaced capitals as in the reference; a long word shrinks to stay on the visible part of the plate.
            label.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            label.characterSpacing = 12f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMax = 32f;
            label.fontSizeMin = 18f;
            label.margin = new Vector4(MenuButtonHidden, 0f, 78f, 0f);
        }
        view.buttonStackGap = MenuButtonGap;
        view.buttonSlots = slots;
        // Status line ("catalog complete" / load failure) under the logo group, above the campaign status.
        if (view.menuStatusText != null)
            UIFactory.SetBox(view.menuStatusText.rectTransform, c, c, c, ScreenPos(960f, 925f), new Vector2(900f, 32f));
        if (view.profileText != null)   // campaign status: centred under the whole group
            UIFactory.SetBox(view.profileText.rectTransform, c, c, c, ScreenPos(960f, 978f), new Vector2(1200f, 110f));
        log.AppendLine("  PASS  MainMenu: PrimeraPantalla layout");
    }

    // ── Runner HUD (HudExample) ──────────────────────────────────────────────────

    // The bar art (2048×384) is placed at HudScale, right-aligned with HudMargin to its painted edge (x 2008) and its
    // painted top (y 48) HudMargin below the screen top. Children are placed in ART pixels (top-left origin).
    private const float HudScale = 0.625f, HudMargin = 0f;
    private static Vector2 HudBarPos => Vector2.zero;   // top-right anchored
    private const float HudLogoScale = 0.29f;

    /// <summary>A point of the bar art (art px, top-left origin) in screen units from the screen's top-right corner.</summary>
    private static Vector2 HudPoint(Vector2 art) =>
        HudBarPos + new Vector2(-(StyleSkinLibrarySO.HudBarCanvas.x - art.x) * HudScale, -art.y * HudScale);

    private static readonly BonusKind[] HudCounters =
        { BonusKind.Kick, BonusKind.Snare, BonusKind.HiHat, BonusKind.Beat, BonusKind.Onset, BonusKind.Impact };

    private static void PatchLiveHud(GameObject root, StyleSkinLibrarySO lib, StringBuilder log)
    {
        var skin = root.GetComponent<RunnerHudSkin>();
        if (skin == null) skin = root.AddComponent<RunnerHudSkin>();
        if (skin.logo == null) skin.logo = BuildStyledTitle("Logo", root.transform, lib, log);
        RunnerHudLayoutSetup.PatchLiveHud(root, lib);
        log.AppendLine("  PASS  Compact runner HUD: artist · song, bonus counters, progress and shared curtain");
    }

    private static void SetCounter(LiveHudView view, BonusKind kind, TextMeshProUGUI value, Image icon)
    {
        switch (kind)
        {
            case BonusKind.Kick: view.kickValue = value; view.kickIcon = icon; break;
            case BonusKind.Snare: view.snareValue = value; view.snareIcon = icon; break;
            case BonusKind.HiHat: view.hiHatValue = value; view.hiHatIcon = icon; break;
            case BonusKind.Beat: view.beatValue = value; view.beatIcon = icon; break;
            case BonusKind.Onset: view.onsetValue = value; view.onsetIcon = icon; break;
            case BonusKind.Impact: view.impactValue = value; view.impactIcon = icon; break;
            case BonusKind.Special: view.specialValue = value; view.specialIcon = icon; break;
            case BonusKind.Life: view.lifeValue = value; view.lifeIcon = icon; break;
        }
    }

    /// <summary>The counter's colour: the average colour of the bonus' normal art, brightened (green Beat → green number…).</summary>
    private static Color IconColor(BonusKind kind)
    {
        string path = Path.Combine(HudSource, "Bonus", kind.ToString(), "normal_front.png");
        if (!File.Exists(path)) return Color.white;
        var tex = new Texture2D(2, 2);
        try
        {
            tex.LoadImage(File.ReadAllBytes(path));
            Vector3 sum = Vector3.zero; float weight = 0f;
            foreach (var p in tex.GetPixels32())
            {
                float a = p.a / 255f;
                sum += new Vector3(p.r, p.g, p.b) / 255f * a; weight += a;
            }
            if (weight <= 0f) return Color.white;
            var mean = sum / weight;
            Color.RGBToHSV(new Color(mean.x, mean.y, mean.z), out float h, out float sat, out _);
            return Color.HSVToRGB(h, Mathf.Clamp01(sat * 1.1f), 1f);
        }
        finally { UnityEngine.Object.DestroyImmediate(tex); }
    }

    // The pause button sits in the bar's round slot: the style pause glyph over the classic one (normal / pressed).
    private static void PatchHudPauseButton(PauseView view, StyleSkinLibrarySO lib, StringBuilder log)
    {
        if (view.pauseButton == null || view.pauseButtonRoot == null) return;
        var button = view.pauseButton;
        float d = StyleSkinLibrarySO.HudPauseDiameter * HudScale;
        var tr = new Vector2(1f, 1f);
        UIFactory.SetBox((RectTransform)button.transform, tr, tr, new Vector2(0.5f, 0.5f), HudPoint(StyleSkinLibrarySO.HudPauseCenter), new Vector2(d, d));
        if (view.cameraToggleButtonRoot != null)   // right under the bar
            UIFactory.SetBox((RectTransform)view.cameraToggleButtonRoot.transform, tr, tr, tr,
                new Vector2(-HudMargin - 8f, HudPoint(new Vector2(0f, 340f)).y - 6f), new Vector2(150f, 32f));

        var group = C<StyleBlendGroup>(view.pauseButtonRoot) ?? view.pauseButtonRoot.AddComponent<StyleBlendGroup>();
        group.library = lib;
        RunnerHudLayoutSetup.PatchPause(view, lib);
        var existing = C<StyleBlendButton>(button.gameObject);
        if (existing != null) { existing.library = lib; return; }

        foreach (var receiver in button.GetComponents<ThemeColorReceiver>()) UnityEngine.Object.DestroyImmediate(receiver);
        var hit = button.GetComponent<Image>();
        if (hit != null) hit.color = new Color(1f, 1f, 1f, 0f);   // round click target, invisible
        button.transition = Selectable.Transition.None;
        if (view.pauseButtonLabel != null) view.pauseButtonLabel.gameObject.SetActive(false);   // the glyph says it

        var skin = button.gameObject.AddComponent<StyleBlendButton>();
        skin.library = lib;
        skin.art = StyleBlendButton.Art.HudPause;
        // pause.png: 256×256 canvas with the glyph ~100 px tall in the middle → the glyph at ~55 % of the slot.
        float glyph = d * 0.55f * 256f / 100f;
        skin.classicLayer = Layer("ClassicLayer", button.transform, 0, lib.ClassicHud.pause);
        skin.styleLayer = Layer("StyleLayer", button.transform, 1, null);
        skin.styleLayer.enabled = false;
        var c = new Vector2(0.5f, 0.5f);
        foreach (var layer in new[] { skin.classicLayer, skin.styleLayer })
            UIFactory.SetBox(layer.rectTransform, c, c, c, Vector2.zero, new Vector2(glyph, glyph));
        log.AppendLine("  PASS  Runner pause button: style glyph in the HUD bar's round slot");
    }

    private static RectTransform ArtRect(string name, RectTransform parent, float x, float y, float w, float h)
    {
        var rt = UIFactory.CreateRect(name, parent);
        var tl = new Vector2(0f, 1f);
        UIFactory.SetBox(rt, tl, tl, tl, new Vector2(x, -y), new Vector2(w, h));
        return rt;
    }

    private static Image ArtImage(string name, RectTransform parent, float x, float y, float w, float h)
    {
        var img = ArtRect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        img.preserveAspect = true;
        return img;
    }

    private static TextMeshProUGUI ArtText(string name, RectTransform parent, float x, float y, float w, float h, int size,
        Color color, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
    {
        var t = UIFactory.CreateText(name, parent, "", size, color, align, style);
        var tl = new Vector2(0f, 1f);
        UIFactory.SetBox(t.rectTransform, tl, tl, tl, new Vector2(x, -y), new Vector2(w, h));
        t.raycastTarget = false;
        return t;
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
            foreach (var s in new[] { GameMusicStyle.Electronic, GameMusicStyle.Rock, GameMusicStyle.Jazzy })
                Shot(outDir, $"main_menu_{s}.png", $"{Screens}/MainMenu.prefab", r =>
                {
                    var v = r.GetComponent<MainMenuView>();
                    v.root.SetActive(true);
                    if (v.settingsPanel != null) v.settingsPanel.SetActive(false);
                    foreach (var loc in r.GetComponentsInChildren<LocalizedText>(true))
                        if (loc.TryGetComponent<TMP_Text>(out var t)) Txt(t, loc.key);
                    Txt(v.playButtonLabel, "MainMenu.Play");
                    // As in the game (QUIT hidden, campaign status shown), with readable sample labels.
                    if (v.quitButton != null) v.quitButton.gameObject.SetActive(false);
                    foreach (var (b, text) in new[] { (v.playButton, "CONTINUE"), (v.rivalsButton, "COLLECTION"), (v.progressionButton, "PROGRESSION"), (v.settingsButton, "SETTINGS") })
                    {
                        var l = b != null ? b.GetComponentInChildren<TMP_Text>(true) : null;
                        if (l != null) l.text = text;
                    }
                    if (v.profileText != null) { v.profileText.gameObject.SetActive(true); v.profileText.text = "<size=130%>LEVEL 2 · match 1 of 5</size>\nLap 1  ·  Mastered matches 0/22  ·  Extra lives 3"; }
                    if (v.logo != null) v.logo.Show(s);
                    if (v.buttonStyle != null) v.buttonStyle.Apply(s, 1f);
                    C<StyleBlendButton>(v.playButton.gameObject)?.OnPointerEnter(null);
                }, log);
            foreach (var (s, p) in new[] { (GameMusicStyle.Electronic, 0.1f), (GameMusicStyle.Electronic, 0.35f), (GameMusicStyle.Electronic, 0.6f), (GameMusicStyle.Electronic, 0.95f),
                                           (GameMusicStyle.Rock, 0.2f), (GameMusicStyle.Jazzy, 0.2f) })
                Shot(outDir, $"hud_{s}_{p:0.00}.png", $"{Screens}/LiveHud.prefab", r =>
                {
                    var v = r.GetComponent<LiveHudView>();
                    float w = lib.RunnerWeight(p);
                    if (v.skin != null) v.skin.Apply(s, w);
                    Txt(v.scoreLabel, "HUD.Score");
                    if (v.scoreValue != null) v.scoreValue.text = "012450";
                    if (v.artistSongText != null) v.artistSongText.text = "ARTISTA · CANÇÓ";
                    var n = new[] { v.kickValue, v.snareValue, v.hiHatValue, v.beatValue, v.onsetValue, v.impactValue, v.specialValue, v.lifeValue };
                    var sample = new[] { 14, 11, 35, 18, 6, 3, 1, 3 };
                    for (int i = 0; i < n.Length; i++) if (n[i] != null) n[i].text = sample[i].ToString();
                    v.progressFill.fillAmount = p;
                    if (v.elapsedTimeText != null) v.elapsedTimeText.text = $"{(int)(232 * p) / 60}:{(int)(232 * p) % 60:00}";
                    if (v.totalTimeText != null) v.totalTimeText.text = "3:52";
                    // The pause button is PauseMenu.prefab's: same canvas, as in Runner.unity.
                    var pause = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{Screens}/PauseMenu.prefab"), r.transform.parent);
                    UIFactory.Stretch((RectTransform)pause.transform);
                    var pv = pause.GetComponent<PauseView>();
                    pv.overlayRoot.SetActive(false);
                    if (pv.cameraToggleLabel != null) Txt(pv.cameraToggleLabel, "Pause.FirstPerson");
                    C<StyleBlendGroup>(pv.pauseButtonRoot)?.Apply(s, 1f - lib.CurtainClassic(1f - w, StyleSkinLibrarySO.HudPauseCenter.x / StyleSkinLibrarySO.HudBarCanvas.x));
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
