using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Checks the real Wagner outfit, shared masks and shirt/jacket layering in play mode.</summary>
[InitializeOnLoad]
public static class WagnerClothingCheck
{
    private const string Pending = "WagnerClothingCheck.Pending";
    private static readonly StringBuilder Log = new();
    private static int failures;
    static WagnerClothingCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            _ = Run();
        };
    }
    [MenuItem("Tools/MusicGame/Avatars/Garments/Validate Wagner Outfit")]
    public static void RunFromCommandLine()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Debug/AvatarsClothesChecker.unity", OpenSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }
    private static void Check(string label, bool passed, string detail = "")
    {
        Log.AppendLine($"{(passed ? "PASS" : "FAIL")} {label}: {detail}");
        if (!passed) failures++;
    }
    private static async Task Run()
    {
        Directory.CreateDirectory("Logs/WagnerClothing");
        try
        {
            await Task.Yield();
            var checker = UnityEngine.Object.FindAnyObjectByType<AvatarsClothesChecker>();
            if (checker == null) throw new InvalidOperationException("Clothes checker scene is not loaded.");
            checker.enabled = false;
            var recipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>("Assets/_Project/Avatar/MakeHuman/Content/Composers/Recipes/Avatar_Wagner.asset");
            var anchor = new GameObject("WagnerCheckAnchor");
            foreach (string quality in new[] { "Low", "Mid", "HighMid", "High", "Ultra" })
            {
                int q = Array.IndexOf(QualitySettings.names, quality);
                if (q < 0) throw new InvalidOperationException("Missing quality " + quality);
                QualitySettings.SetQualityLevel(q, false);
                var avatar = await AvatarFactory.CreateAsync(recipe.ToRuntime(), anchor.transform);
                try
                {
                    Check(quality + " factory assembly", avatar?.Root != null && avatar.EquippedItems.Count == 3);
                    if (avatar?.Root == null) continue;
                    await Task.Yield();
                    Check(quality + " segmented wearable count", avatar.Appearance.SegmentedGarmentCount == 3);
                    var body = AvatarClothingBinder.MainBody(avatar.BodyPart.skinnedRenderers);
                    foreach (var item in avatar.EquippedItems)
                    foreach (var renderer in avatar.Appearance.RenderersOf(item))
                    {
                        string label = quality + " " + item.Definition.stableId;
                        Check(label + " one submesh and material", renderer.RuntimeMesh.subMeshCount == 1 && renderer.Renderer.sharedMaterials.Length == 1);
                        Check(label + " source vertices and blendshapes preserved",
                            renderer.RuntimeMesh.vertexCount == renderer.Topology.Source.vertexCount && renderer.RuntimeMesh.blendShapeCount == renderer.Topology.Source.blendShapeCount,
                            $"{renderer.RuntimeMesh.vertexCount} vertices, {renderer.RuntimeMesh.blendShapeCount} blendshapes, {renderer.VisibleTriangles}/{renderer.TotalTriangles} triangles");
                        Check(label + " shared skeleton", renderer.Renderer.bones.All(b => body.bones.Contains(b)));
                        var baked = new Mesh();
                        renderer.Renderer.BakeMesh(baked);
                        var points = baked.vertices.Select(renderer.Renderer.transform.TransformPoint).ToArray();
                        var bounds = new Bounds(points[0], Vector3.zero);
                        foreach (var point in points) bounds.Encapsulate(point);
                        float height = renderer.Topology.Source.bounds.size.y;
                        Check(label + " visible geometry scale", bounds.size.y > height * .5f && bounds.size.y < height * 2f, bounds.size.ToString("F4"));
                        UnityEngine.Object.Destroy(baked);
                    }
                    var shirt = avatar.EquippedItems.First(i => i.Definition.stableId == "wagner_shirt");
                    var jacket = avatar.EquippedItems.First(i => i.Definition.stableId == "wagner_jacket");
                    var trousers = avatar.EquippedItems.First(i => i.Definition.stableId == "wagner_pants");
                    var hidden = new List<string>();
                    avatar.Appearance.GetHiddenRegions(shirt, hidden);
                    Check(quality + " jacket hides shirt sleeves and back", hidden.Contains("back_upper") && hidden.Contains("forearm_lower_l") && hidden.Contains("forearm_lower_r"));
                    Check(quality + " shirt front, neck and cuffs remain eligible", !hidden.Any(r => r.StartsWith("chest_") || r.StartsWith("abdomen_") || r.StartsWith("neck_") || r.StartsWith("wrist_")));
                    int shirtVisible = avatar.Appearance.RenderersOf(shirt).Sum(r => r.VisibleTriangles);
                    int shirtTotal = avatar.Appearance.RenderersOf(shirt).Sum(r => r.TotalTriangles);
                    Check(quality + " actual shirt triangles hidden", shirtVisible > 0 && shirtVisible < shirtTotal, $"{shirtVisible}/{shirtTotal}");
                    Check(quality + " body mask composed", avatar.Appearance.BodyMask != null);
                    Check(quality + " coverage union", CoverageMatches(avatar, shirt.Definition, jacket.Definition, trousers.Definition));
                    var hair = avatar.Root.GetComponentsInChildren<SegmentedGarmentPart>().First(p => p.name.Contains("hair"));
                    Check(quality + " hair uses one material and no skin coverage", hair.renderers.All(r => r.renderer.sharedMesh.subMeshCount == 1 && r.renderer.sharedMaterials.Length == 1));
                    if (quality != "Ultra") continue;

                    Capture(avatar, "outfit_front", 0);
                    Capture(avatar, "outfit_back", 180);
                    Capture(avatar, "outfit_side", 90);
                    var clip = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/_Project" }).Select(AssetDatabase.GUIDToAssetPath)
                        .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
                        .FirstOrDefault(c => c.isHumanMotion && c.name.IndexOf("punch", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (clip != null && avatar.Animator != null)
                    {
                        using (var player = new AvatarClipPlayer(avatar.Animator))
                        {
                            player.Play(clip, true, 0);
                            player.Seek(clip.length * .35f);
                            await Task.Delay(150);
                            Capture(avatar, "outfit_animated_front", 0);
                            Capture(avatar, "outfit_animated_back", 180);
                            Check("Animated shared outfit", true, clip.name);
                        }
                        avatar.ResetToRestPose();
                        await Task.Delay(100);
                    }

                    var jacketDefinition = jacket.Definition;
                    var shirtDefinition = shirt.Definition;
                    Check("Unequip jacket", AvatarFactory.Unequip(avatar, jacketDefinition));
                    await Task.Yield();
                    Check("Shirt sleeves and back restored", avatar.Appearance.RenderersOf(shirt).All(r => r.VisibleTriangles == r.TotalTriangles));
                    Check("Trouser seat restored", avatar.Appearance.RenderersOf(trousers).All(r => r.VisibleTriangles == r.TotalTriangles));
                    Check("Shirt masks skin independently", CoverageMatches(avatar, shirtDefinition, trousers.Definition));
                    Capture(avatar, "shirt_without_jacket_front", 0);
                    Capture(avatar, "shirt_without_jacket_back", 180);

                    Check("Re-equip jacket", await AvatarFactory.EquipAsync(avatar, jacketDefinition) != null);
                    Check("Shirt layering restored", avatar.Appearance.RenderersOf(shirt).Sum(r => r.VisibleTriangles) == shirtVisible);
                    Check("Unequip shirt", AvatarFactory.Unequip(avatar, shirtDefinition));
                    await Task.Yield();
                    Check("Open jacket masks skin independently", CoverageMatches(avatar, jacketDefinition, trousers.Definition));
                    Check("Bare front visible through open jacket", !avatar.Appearance.CoveredBodyRegions.Contains("chest_front_center") && !avatar.Appearance.CoveredBodyRegions.Contains("abdomen_front_center"));
                    Capture(avatar, "jacket_without_shirt_front", 0);
                    Capture(avatar, "jacket_without_shirt_back", 180);
                    var restored = await AvatarFactory.EquipAsync(avatar, shirtDefinition);
                    Check("Re-equip shirt", restored != null && avatar.Appearance.RenderersOf(restored).Sum(r => r.VisibleTriangles) == shirtVisible);
                    Check("Full coverage union restored", CoverageMatches(avatar, jacketDefinition, shirtDefinition, trousers.Definition));
                }
                finally { avatar?.Dispose(); }
                await Task.Yield();
            }
            Check("Body masks released", BodyVisibilityMask.CachedCount == 0);
        }
        catch (Exception e) { Check("Exception", false, e.ToString()); }
        File.WriteAllText("Logs/WagnerClothing/validation.txt", Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
    private static bool CoverageMatches(AvatarInstance avatar, params WearableItemSO[] items) =>
        avatar.Appearance.CoveredBodyRegions.OrderBy(r => r).SequenceEqual(items.SelectMany(i => i.segmentedGarment.bodyCoverage).Distinct().OrderBy(r => r));

    private static void Capture(AvatarInstance avatar, string name, float yaw)
    {
        var go = new GameObject("WagnerCaptureCamera");
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 34;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.22f, .24f, .27f);
        var focus = avatar.Root.position + Vector3.up * .95f;
        camera.transform.position = focus + Quaternion.Euler(5, yaw + 180, 0) * new Vector3(0, 0, -4.2f);
        camera.transform.LookAt(focus);
        var rt = new RenderTexture(900, 1100, 24);
        var image = new Texture2D(900, 1100, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        image.Apply();
        File.WriteAllBytes("Logs/WagnerClothing/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(image);
        UnityEngine.Object.Destroy(go);
    }
}
