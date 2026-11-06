#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Incremental runner-only layout migration. Also used by the regular UI prefab builder.</summary>
public static class RunnerHudLayoutSetup
{
    public const string Folder = "Assets/_Project/Prefabs/UI/";
    public const string Library = "Assets/_Project/Configs/UI/StyleSkinLibrary.asset";
    private static readonly BonusKind[] Kinds = { BonusKind.Kick, BonusKind.Snare, BonusKind.HiHat,
        BonusKind.Beat, BonusKind.Onset, BonusKind.Impact, BonusKind.Life, BonusKind.Special };

    public static void RunFromCommandLine()
    {
        try { Apply(); AssetDatabase.SaveAssets(); Debug.Log("[RunnerHudLayout] PASS: art, prefab, pause and skin updated."); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
    }

    [MenuItem("Tools/MusicGame/UI/Apply compact Runner HUD")]
    public static void Apply()
    {
        var lib = AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(Library);
        if (lib == null) throw new InvalidOperationException("Missing StyleSkinLibrary.");
        foreach (GameMusicStyle style in Enum.GetValues(typeof(GameMusicStyle)))
        {
            if (style == GameMusicStyle.Unknown || lib.hud.Any(h => h.style == style)) continue;
            string folder = $"Assets/_Project/Art/UI/RunnerHud/{style}";
            if (!File.Exists(folder + "/bar.png")) continue;
            lib.hud.Add(new StyleSkinLibrarySO.HudSkin { style = style,
                pause = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "/pause.png"),
                pauseSelected = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "/pause_selected.png") });
        }
        foreach (var skin in lib.hud)
        {
            string path = $"Assets/_Project/Art/UI/RunnerHud/{skin.style}/bar.png";
            if (!File.Exists(path)) continue;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 4096;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            skin.bar = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        EditorUtility.SetDirty(lib);
        Edit("LiveHud", root => PatchLiveHud(root, lib));
        Edit("PauseMenu", root => PatchPause(root.GetComponent<PauseView>(), lib));
    }

    private static void Edit(string name, Action<GameObject> patch)
    {
        string path = Folder + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try { patch(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void PatchLiveHud(GameObject root, StyleSkinLibrarySO lib)
    {
        var view = root.GetComponent<LiveHudView>();
        if (view == null) throw new InvalidOperationException("Missing LiveHudView.");
        var skin = view.skin != null ? view.skin : root.GetComponent<RunnerHudSkin>();
        if (skin == null) skin = root.AddComponent<RunnerHudSkin>();
        var logo = skin.logo;
        if (logo != null) logo.transform.SetParent(root.transform, false);
        foreach (string n in new[] { "HudBar", "TopBar", "SongProgressBg" })
        { var t = root.transform.Find(n); if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject); }
        skin.library = lib; skin.icons.Clear(); skin.separators.Clear();
        view.scoreLabel = view.scoreValue = view.totalLabel = view.totalValue = null;
        view.elapsedTimeText = view.totalTimeText = null;
        view.kickLabel = view.snareLabel = view.hiHatLabel = view.beatLabel = view.onsetLabel = view.impactLabel = null;
        view.lifeLabel = view.specialLabel = null;
        var bar = Rect("HudBar", root.transform, 0, 0, 3072, 320);
        bar.SetAsFirstSibling();
        var fit = bar.gameObject.AddComponent<RunnerHudArtAnchor>(); fit.entireBar = true; fit.Fit();
        var well = ImageAt("PauseWell", bar, 2806.25f, 109.12f, 167.44f, 167.44f);
        well.sprite = PauseWell(); well.color = new Color(.012f, .025f, .04f, .98f);
        skin.styleBar = Full("StyleBar", bar); skin.styleBar.sprite = lib.Hud(GameMusicStyle.Funky)?.bar;
        skin.classicBar = Full("ClassicBar", bar); skin.classicBar.sprite = lib.ClassicHud.bar;
        skin.classicBar.material = lib.curtainMaterial;
        for (int i = 0; i < Kinds.Length; i++)
        {
            float x = i < 6 ? 510f + i * 148f : 1454f + (i - 6) * 148f;
            var cell = Rect("Cell_" + Kinds[i], bar, x, 122f, 148f, 112f);
            var normal = ImageAt("Icon", cell, 0f, 8f, 88f, 88f);
            var classic = ImageAt("IconClassic", cell, 0f, 8f, 88f, 88f);
            var art = lib.Bonus(Kinds[i]); normal.sprite = art?.normal; classic.sprite = art?.classic;
            normal.preserveAspect = classic.preserveAspect = true;
            classic.color = new Color(1, 1, 1, 0); classic.enabled = false;
            skin.icons.Add(new RunnerHudSkin.BonusIcon { kind = Kinds[i], normal = normal, classic = classic });
            var value = TextAt("Value", cell, 88f, 17f, 58f, 72f, 44f);
            value.text = "0"; value.alignment = TextAlignmentOptions.MidlineLeft;
            switch (Kinds[i])
            {
                case BonusKind.Kick: view.kickValue = value; view.kickIcon = normal; break;
                case BonusKind.Snare: view.snareValue = value; view.snareIcon = normal; break;
                case BonusKind.HiHat: view.hiHatValue = value; view.hiHatIcon = normal; break;
                case BonusKind.Beat: view.beatValue = value; view.beatIcon = normal; break;
                case BonusKind.Onset: view.onsetValue = value; view.onsetIcon = normal; break;
                case BonusKind.Impact: view.impactValue = value; view.impactIcon = normal; break;
                case BonusKind.Life: view.lifeValue = value; view.lifeIcon = normal; break;
                case BonusKind.Special: view.specialValue = value; view.specialIcon = normal; break;
            }
        }
        foreach (float x in new[] { 1420f, 1778f })
        {
            var line = ImageAt("Separator", bar, x, 120f, 2.5f, 114f);
            line.sprite = White(); skin.separators.Add(line);
        }
        view.artistSongText = TextAt("ArtistSong", bar, 1812f, 119f, 850f, 64f, 40f);
        view.artistSongText.text = "ARTISTA · CANÇÓ";
        view.artistSongText.fontStyle = FontStyles.Normal;
        view.artistSongText.enableAutoSizing = true;
        view.artistSongText.fontSizeMin = 32f; view.artistSongText.fontSizeMax = 40f;
        view.artistSongText.overflowMode = TextOverflowModes.Ellipsis;
        var border = ImageAt("ProgressBorder", bar, 1812f, 204f, 850f, 20f);
        border.sprite = White(); skin.progressBorder = border;
        var track = ImageAt("ProgressTrack", bar, 1815f, 207f, 844f, 14f);
        track.sprite = White(); track.color = new Color(.005f, .015f, .03f, .95f);
        var fill = ImageAt("ProgressFill", bar, 1815f, 207f, 844f, 14f);
        fill.sprite = White(); fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left; fill.fillAmount = 0f;
        skin.progressFill = view.progressFill = fill;
        if (logo != null)
        {
            logo.transform.SetParent(bar, false); logo.library = lib;
            var rt = (RectTransform)logo.transform;
            UIFactory.SetBox(rt, Vector2.up, Vector2.up, Vector2.up, new Vector2(170f, -82f), new Vector2(1920f, 1080f));
            rt.localScale = Vector3.one * .18f; logo.Show(GameMusicStyle.Funky); skin.logo = logo;
        }
        if (view.tagsStrip != null)
        { var rt = view.tagsStrip.GetComponent<RectTransform>(); rt.anchoredPosition = new Vector2(24f, -206f); view.tagsStrip.SetActive(false); }
        view.skin = skin;
        skin.Apply(GameMusicStyle.Funky, 1f);
    }

    public static void PatchPause(PauseView view, StyleSkinLibrarySO lib)
    {
        if (view == null || view.pauseButton == null) return;
        var button = view.pauseButton;
        var anchor = button.GetComponent<RunnerHudArtAnchor>();
        if (anchor == null) anchor = button.gameObject.AddComponent<RunnerHudArtAnchor>();
        anchor.entireBar = false; anchor.artCenter = StyleSkinLibrarySO.HudPauseCenter;
        anchor.artSize = Vector2.one * StyleSkinLibrarySO.HudPauseDiameter; anchor.Fit();
        var blend = button.GetComponent<StyleBlendButton>();
        if (blend != null)
        {
            blend.library = lib;
            foreach (var layer in new[] { blend.classicLayer, blend.styleLayer })
                if (layer != null)
                {
                    // The PNG has transparent padding: keep its visible glyph at 55% of the socket.
                    var rt = layer.rectTransform;
                    float margin = (.55f * 256f / 100f - 1f) * .5f;
                    rt.anchorMin = Vector2.one * -margin;
                    rt.anchorMax = Vector2.one * (1f + margin);
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                    rt.localScale = Vector3.one;
                }
        }
        if (view.cameraToggleButtonRoot != null)
            UIFactory.SetBox(view.cameraToggleButtonRoot.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one, new Vector2(-12f, -206f), new Vector2(150f,32f));
    }

    private static RectTransform Rect(string n, Transform p, float x,float y,float w,float h)
    { var rt = UIFactory.CreateRect(n,(RectTransform)p); UIFactory.SetBox(rt,Vector2.up,Vector2.up,Vector2.up,new Vector2(x,-y),new Vector2(w,h));return rt; }
    private static Image ImageAt(string n,Transform p,float x,float y,float w,float h)
    { var image = Rect(n,p,x,y,w,h).gameObject.AddComponent<Image>();image.raycastTarget=false;return image; }
    private static Image Full(string n,RectTransform p)
    { var image=ImageAt(n,p,0,0,3072,320);return image; }
    private static TextMeshProUGUI TextAt(string n,Transform p,float x,float y,float w,float h,float size)
    { var t=UIFactory.CreateText(n,(RectTransform)p,"",(int)size,Color.white,TextAlignmentOptions.MidlineLeft,FontStyles.Bold);UIFactory.SetBox(t.rectTransform,Vector2.up,Vector2.up,Vector2.up,new Vector2(x,-y),new Vector2(w,h));t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.NoWrap;return t; }
    private static Sprite White()=>AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/UI/White.png");
    private static Sprite PauseWell()
    {
        const string path="Assets/_Project/Art/UI/RunnerHud/Common/PauseWell.png";
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));var t=new Texture2D(128,128,TextureFormat.RGBA32,false);
            for(int y=0;y<128;y++)for(int x=0;x<128;x++){float a=Mathf.Clamp01(63.5f-Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(64,64)));t.SetPixel(x,y,new Color(1,1,1,a));}
            t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);AssetDatabase.ImportAsset(path);
            var i=(TextureImporter)AssetImporter.GetAtPath(path);i.textureType=TextureImporterType.Sprite;i.spriteImportMode=SpriteImportMode.Single;i.alphaIsTransparency=true;i.mipmapEnabled=false;i.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
#endif
