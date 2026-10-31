#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generates every SCREEN prefab (and the list-item prefabs they spawn) plus the root canvas, after the
/// component prefabs (UIComponentPrefabBuilder). The game never builds UI in code: these generators are the
/// one place the initial structure is written, and from then on the prefabs are edited by hand.
///   Prefabs/UI/UIRootCanvas.prefab     the overlay canvas everything is instantiated under
///   Prefabs/UI/&lt;Screen&gt;.prefab        one prefab per screen (paths kept from the old UIPrefabBuilder)
///   Prefabs/UI/Items/&lt;Item&gt;.prefab     rows, cells and buttons spawned per data item
/// "Build All UI Prefabs" creates only what is missing (manual edits are kept); "Rebuild" overwrites every
/// generated prefab from code (asks for confirmation; GUIDs are kept so references survive).
/// Per-screen generators live next to this file (FrontendScreenPrefabs, OverlayScreenPrefabs,
/// FightScreenPrefabs, ResultsScreenPrefabs, RunnerScreenPrefabs).
/// Batch: -executeMethod UIScreenPrefabBuilder.BuildFromCommandLine [-rebuild | -rebuildScreens]   (Logs/UIScreenPrefabBuilder.txt)
/// </summary>
public static class UIScreenPrefabBuilder
{
    [MenuItem("Tools/MusicGame/UI/Build All UI Prefabs", priority = 0)]
    public static void BuildMenu() => Debug.Log(Build(rebuild: false));

    [MenuItem("Tools/MusicGame/UI/Rebuild All UI Prefabs (overwrite)", priority = 1)]
    public static void RebuildMenu()
    {
        if (!EditorUtility.DisplayDialog("Rebuild ALL UI prefabs",
                "Every generated UI prefab (screens, items, components, canvas) will be OVERWRITTEN from code, losing manual edits. Continue?", "Rebuild", "Cancel")) return;
        Debug.Log(Build(rebuild: true));
    }

    public static void BuildFromCommandLine()
    {
        var args = System.Environment.GetCommandLineArgs();
        string report = Build(rebuild: args.Contains("-rebuild"), rebuildScreensOnly: args.Contains("-rebuildScreens"));
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/UIScreenPrefabBuilder.txt", report, Encoding.UTF8);
        EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }

    /// <param name="rebuildScreensOnly">Overwrite the screen/item/canvas prefabs but keep the component prefabs.</param>
    public static string Build(bool rebuild, bool rebuildScreensOnly = false)
    {
        var log = new StringBuilder();
        log.Append(UIComponentPrefabBuilder.Build(rebuild));
        var lib = AssetDatabase.LoadAssetAtPath<UIPrefabLibrarySO>(UIScreenPrefabContext.LibraryPath);
        var ctx = new UIScreenPrefabContext(lib, rebuild || rebuildScreensOnly, log);
        log.AppendLine($"[UIScreenPrefabBuilder] {(ctx.Rebuild ? "REBUILD" : "build missing")}");

        lib.rootCanvas = ctx.Prefab<Canvas>($"{UIScreenPrefabContext.ScreensFolder}/UIRootCanvas.prefab", BuildRootCanvas);
        Run(ctx, "Frontend", FrontendScreenPrefabs.Build);
        Run(ctx, "Overlay", OverlayScreenPrefabs.Build);
        Run(ctx, "Fight", FightScreenPrefabs.Build);
        Run(ctx, "Results", ResultsScreenPrefabs.Build);
        Run(ctx, "Runner", RunnerScreenPrefabs.Build);
        Run(ctx, "Help", HelpScreenPrefabs.Build);
        Run(ctx, "Profile", ProfileScreenPrefabs.Build);   // after Frontend: it patches MainMenu   // after Fight / Runner: it patches FightHud, PauseMenu, LiveHud
        Run(ctx, "TempoFury", TempoFuryUISetup.Patch);     // last: title font, style pause buttons, styled intro title

        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();

        int missing = 0;
        foreach (var f in typeof(UIPrefabLibrarySO).GetFields().Where(f => typeof(Object).IsAssignableFrom(f.FieldType)))
            if (f.GetValue(lib) as Object == null) { missing++; log.AppendLine($"  FAIL  library slot '{f.Name}' empty"); }
        log.AppendLine(missing == 0 ? "  PASS  every UIPrefabLibrary slot assigned" : $"  FAIL  {missing} empty slot(s)");
        return log.ToString();
    }

    private static void Run(UIScreenPrefabContext ctx, string group, System.Action<UIScreenPrefabContext> build)
    {
        try { build(ctx); }
        catch (System.Exception e) { ctx.Log.AppendLine($"  FAIL  {group} generator threw: {e}"); }
    }

    private static GameObject BuildRootCanvas()
    {
        var go = new GameObject("UIRootCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return go;
    }
}

/// <summary>Shared state + helpers for the screen prefab generators.</summary>
public sealed class UIScreenPrefabContext
{
    public const string ScreensFolder = "Assets/_Project/Prefabs/UI";
    public const string ItemsFolder = "Assets/_Project/Prefabs/UI/Items";
    public const string LibraryPath = "Assets/_Project/Configs/UI/UIPrefabLibrary.asset";
    private const string WhiteSpritePath = "Assets/_Project/Art/UI/White.png";

    public readonly UIPrefabLibrarySO Library;
    public readonly bool Rebuild;
    public readonly StringBuilder Log;
    /// <summary>Asset 4×4 white sprite: use it (never UIFactory.WhiteSprite, a runtime-only object) for
    /// Filled images. Prefab() also swaps any non-asset sprite for it automatically.</summary>
    public readonly Sprite WhiteSprite;

    public UIScreenPrefabContext(UIPrefabLibrarySO library, bool rebuild, StringBuilder log)
    {
        Library = library; Rebuild = rebuild; Log = log;
        EnsureFolder(ScreensFolder); EnsureFolder(ItemsFolder); EnsureFolder("Assets/_Project/Art/UI");
        WhiteSprite = EnsureWhiteSprite();
    }

    /// <summary>Saves `build()` as a prefab at `path` (only if missing, or always when Rebuild) and returns its
    /// `T` component. Before saving: TMP texts get the default font, non-asset sprites become WhiteSprite.
    /// Overwriting keeps the asset GUID, so library/scene references survive a rebuild.</summary>
    public T Prefab<T>(string path, System.Func<GameObject> build) where T : Component
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !Rebuild) { Log.AppendLine($"  keep  {path}"); return existing.GetComponent<T>(); }
        var root = build();
        Sanitize(root);
        var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
        Object.DestroyImmediate(root);
        Log.AppendLine(ok ? $"  {(existing != null ? "redo" : "new ")}  {path}" : $"  FAIL  could not save {path}");
        var component = saved != null ? saved.GetComponent<T>() : null;
        if (saved != null && component == null) Log.AppendLine($"  FAIL  {path} has no {typeof(T).Name} on its root");
        return component;
    }

    /// <summary>Edits an EXISTING prefab in place (keeps its internal file ids, so scene instances and their
    /// overrides survive). `patch` must be idempotent: add only what is missing. Returns false if absent.</summary>
    public bool Patch(string path, System.Action<GameObject> patch)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Log.AppendLine($"  FAIL  cannot patch missing {path}"); return false; }
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            patch(root);
            Sanitize(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Log.AppendLine($"  patch {path}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return true;
    }

    /// <summary>Instantiates a prefab asset as a linked nested prefab under `parent`.</summary>
    public static T Nested<T>(T prefab, Transform parent) where T : Component =>
        ((GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, parent)).GetComponent<T>();

    /// <summary>Adds a LocalizedText (static label following the language) and shows the key in the editor.</summary>
    public static TextMeshProUGUI Localized(TextMeshProUGUI t, string key)
    {
        t.gameObject.AddComponent<LocalizedText>().key = key;
        t.text = key;
        return t;
    }

    public static GameObject Root(string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        ((RectTransform)go.transform).sizeDelta = size;
        return go;
    }

    private void Sanitize(GameObject root)
    {
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) if (t.font == null) t.font = TMP_Settings.defaultFontAsset;
        foreach (var img in root.GetComponentsInChildren<Image>(true))
            if (img.sprite != null && !EditorUtility.IsPersistent(img.sprite)) img.sprite = WhiteSprite;
    }

    private Sprite EnsureWhiteSprite()
    {
        if (!File.Exists(WhiteSpritePath))
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            tex.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
            File.WriteAllBytes(WhiteSpritePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(WhiteSpritePath);
            Log.AppendLine($"  new   {WhiteSpritePath}");
        }
        return UIComponentPrefabBuilder.EnsureSingleSprite(WhiteSpritePath, Log);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
#endif
