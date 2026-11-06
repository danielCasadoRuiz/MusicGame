using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Builds every composer and player-style recipe in play mode (AvatarsClothesChecker scene) at the five
/// qualities: all items equipped and assembled per region, hair present; front / back / side captures of the
/// Ultra build in Logs/SegmentedOutfits. Chained from SegmentedOutfitSetup (-validateOutfit).</summary>
[InitializeOnLoad]
public static class SegmentedOutfitCheck
{
    private const string Pending = "SegmentedOutfitCheck.Pending";
    private const string Folder = "Logs/SegmentedOutfits";
    private static readonly StringBuilder Log = new();
    private static int failures;

    static SegmentedOutfitCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            _ = Run();
        };
    }

    [MenuItem("Tools/MusicGame/Avatars/Garments/Validate All Segmented Outfits")]
    public static void RunFromCommandLine()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Debug/AvatarsClothesChecker.unity", OpenSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    private static void Check(string label, bool passed, string detail = "")
    {
        Log.AppendLine($"{(passed ? "PASS" : "FAIL")} {label}{(detail.Length > 0 ? ": " + detail : "")}");
        if (!passed) failures++;
    }

    private static async Task Run()
    {
        Directory.CreateDirectory(Folder);
        try
        {
            await Task.Yield();
            var checker = UnityEngine.Object.FindAnyObjectByType<AvatarsClothesChecker>();
            if (checker != null) checker.enabled = false;
            var recipes = AssetDatabase.FindAssets("t:AvatarRecipeSO", new[] { "Assets/_Project/Avatar/MakeHuman/Content/Composers/Recipes", "Assets/_Project/Avatar/MakeHuman/Content/Player/Recipes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(r => r != null && (r.items.Length > 0 || r.identity?.hair != null)).OrderBy(r => r.name).ToList();
            var anchor = new GameObject("OutfitCheckAnchor");
            foreach (var recipe in recipes)
            foreach (string quality in new[] { "Low", "Mid", "HighMid", "High", "Ultra" })
            {
                int q = Array.IndexOf(QualitySettings.names, quality);
                if (q < 0) { Check("quality " + quality, false, "missing"); continue; }
                QualitySettings.SetQualityLevel(q, false);
                var avatar = await AvatarFactory.CreateAsync(recipe.ToRuntime(), anchor.transform);
                try
                {
                    string label = $"{recipe.name} {quality}";
                    int expected = recipe.items.Count(i => i != null);
                    Check(label + " built", avatar?.Root != null);
                    if (avatar?.Root == null) continue;
                    await Task.Yield();
                    Check(label + " items equipped", avatar.EquippedItems.Count == expected, $"{avatar.EquippedItems.Count}/{expected}");
                    Check(label + " assembled per region", avatar.Appearance.SegmentedGarmentCount == expected, $"{avatar.Appearance.SegmentedGarmentCount}/{expected}");
                    if (recipe.identity?.hair != null)
                        Check(label + " hair", avatar.Root.GetComponentsInChildren<SegmentedGarmentPart>().Any(p => p.name.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0));
                    foreach (var item in avatar.EquippedItems)
                    foreach (var r in avatar.Appearance.RenderersOf(item))
                        Check($"{label} {item.Definition.stableId} one draw", r.RuntimeMesh.subMeshCount == 1 && r.Renderer.sharedMaterials.Length == 1,
                              $"{r.VisibleTriangles}/{r.TotalTriangles} triangles visible");
                    if (quality != "Ultra") continue;
                    Capture(avatar, recipe.name + "_front", 0);
                    Capture(avatar, recipe.name + "_back", 180);
                    Capture(avatar, recipe.name + "_side", 90);
                }
                finally { avatar?.Dispose(); }
                await Task.Yield();
            }
            Check("Body masks released", BodyVisibilityMask.CachedCount == 0);
        }
        catch (Exception e) { Check("Exception", false, e.ToString()); }
        File.WriteAllText(Folder + "/validation.txt", Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void Capture(AvatarInstance avatar, string name, float yaw)
    {
        var go = new GameObject("OutfitCaptureCamera");
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 34;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.22f, .24f, .27f);
        var focus = avatar.Root.position + Vector3.up * .95f;
        camera.transform.position = focus + Quaternion.Euler(5, yaw + 180, 0) * new Vector3(0, 0, -4.2f);
        camera.transform.LookAt(focus);
        var rt = new RenderTexture(700, 900, 24);
        var image = new Texture2D(700, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        image.Apply();
        File.WriteAllBytes($"{Folder}/{name}.png", image.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(image);
        UnityEngine.Object.Destroy(go);
    }
}
