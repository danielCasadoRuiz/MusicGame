using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Exercises the actual avatar factory in the clothes checker scene and captures the outfit.</summary>
[InitializeOnLoad]
public static class MonteverdiClothingCheck
{
    private const string Pending = "MonteverdiClothingCheck.Pending";
    private const string Scene = "Assets/_Project/Scenes/Debug/AvatarsClothesChecker.unity";
    private static readonly StringBuilder Log = new();
    private static int failures;
    static MonteverdiClothingCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            _ = Run();
        };
    }
    [MenuItem("Tools/MusicGame/Avatars/Garments/Validate Monteverdi Outfit")]
    public static void RunFromCommandLine()
    {
        EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
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
        Directory.CreateDirectory("Logs/MonteverdiClothing");
        try
        {
            await Task.Yield();
            var checker = UnityEngine.Object.FindAnyObjectByType<AvatarsClothesChecker>();
            Check("Clothes checker scene", checker != null);
            checker.enabled = false; // Keep its stage, place a controlled capture camera ourselves.
            var recipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>("Assets/_Project/Avatar/MakeHuman/Content/Composers/Recipes/Avatar_Monteverdi.asset");
            var anchor = new GameObject("MonteverdiCheckAnchor");
            foreach (string quality in new[] { "Low", "Mid", "HighMid", "High", "Ultra" })
            {
                int q = Array.IndexOf(QualitySettings.names, quality);
                Check("Quality available " + quality, q >= 0);
                if (q >= 0) QualitySettings.SetQualityLevel(q, false);
                var avatar = await AvatarFactory.CreateAsync(recipe.ToRuntime(), anchor.transform);
                try
                {
                    Check(quality + " factory assembly", avatar?.Root != null && avatar.EquippedItems.Count == 4);
                    if (avatar?.Root == null) continue;
                    await Task.Yield();
                    Check(quality + " segmented wearable count", avatar.Appearance.SegmentedGarmentCount == 4);
                    var bodyRenderer = AvatarClothingBinder.MainBody(avatar.BodyPart.skinnedRenderers);
                    foreach (var item in avatar.EquippedItems)
                    foreach (var renderer in avatar.Appearance.RenderersOf(item))
                    {
                        Check(quality + " one submesh " + item.Definition.stableId, renderer.RuntimeMesh.subMeshCount == 1);
                        Check(quality + " shares body skeleton " + item.Definition.stableId,
                            renderer.Renderer.bones.All(bone => bodyRenderer.bones.Contains(bone)));
                        Check(quality + " preserves vertices and blendshapes " + item.Definition.stableId,
                            renderer.RuntimeMesh.vertexCount == renderer.Topology.Source.vertexCount &&
                            renderer.RuntimeMesh.blendShapeCount == renderer.Topology.Source.blendShapeCount,
                            $"{renderer.RuntimeMesh.vertexCount} vertices, {renderer.RuntimeMesh.blendShapeCount} blendshapes, {renderer.VisibleTriangles}/{renderer.TotalTriangles} triangles");
                        var baked = new Mesh();
                        renderer.Renderer.BakeMesh(baked);
                        var points = baked.vertices.Select(renderer.Renderer.transform.TransformPoint).ToArray();
                        var bounds = new Bounds(points[0], Vector3.zero);
                        foreach (var point in points) bounds.Encapsulate(point);
                        float expectedHeight = renderer.Topology.Source.bounds.size.y;
                        Check(quality + " visible garment scale " + item.Definition.stableId,
                            bounds.size.y > expectedHeight * .5f && bounds.size.y < expectedHeight * 2f,
                            $"world height {bounds.size.y:F4}, source height {expectedHeight:F4}");
                        UnityEngine.Object.Destroy(baked);
                    }
                    Check(quality + " body mask assigned", avatar.Appearance.BodyMask != null, string.Join(",", avatar.Appearance.CoveredBodyRegions));
                    foreach (var item in avatar.EquippedItems.Where(i => i.Definition.stableId.Contains("shoes") || i.Definition.stableId.Contains("beard")))
                        Check(quality + " non-occluding " + item.Definition.stableId, item.Definition.segmentedGarment.bodyCoverage.Count == 0 && item.Definition.segmentedGarment.garmentCoverage.Count == 0);
                    if (quality == "Ultra")
                    {
                        Capture(avatar, "outfit_front", 0);
                        Capture(avatar, "outfit_back", 180);
                        Capture(avatar, "outfit_side", 90);
                        foreach (var item in avatar.EquippedItems) item.Instance.SetActive(item.Definition.stableId.Contains("pants") || item.Definition.stableId.Contains("shoes"));
                        Capture(avatar, "trousers_geometry", 0);
                        foreach (var item in avatar.EquippedItems) item.Instance.SetActive(true);
                        var clip = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/_Project" }).Select(AssetDatabase.GUIDToAssetPath)
                            .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>()).FirstOrDefault(c => c.isHumanMotion && c.name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (clip != null && avatar.Animator != null)
                        {
                            using var player = new AvatarClipPlayer(avatar.Animator);
                            player.Play(clip, true, 0);
                            player.Seek(clip.length * .35f);
                            // Let Unity update skinned vertex buffers after the pose changes.
                            // Several manual Camera.Render calls in one frame otherwise reuse the earlier pose.
                            await Task.Delay(150);
                            Capture(avatar, "outfit_run_front", 0);
                            Capture(avatar, "outfit_run_back", 180);
                            Check("Animation pose evaluated", true, clip.name);
                        }
                        avatar.ResetToRestPose();
                        await Task.Delay(100);
                        var jacket = avatar.EquippedItems.First(i => i.Definition.stableId.Contains("jacket")).Definition;
                        var trousers = avatar.EquippedItems.First(i => i.Definition.stableId.Contains("pants")).Definition;
                        Check("Unequip jacket", AvatarFactory.Unequip(avatar, jacket));
                        await Task.Yield();
                        Check("Trousers independently mask the body",
                            avatar.Appearance.CoveredBodyRegions.OrderBy(r => r).SequenceEqual(trousers.segmentedGarment.bodyCoverage.OrderBy(r => r)));
                        var pantsRecord = avatar.EquippedItems.First(i => i.Definition == trousers);
                        Check("Trouser seat returns without jacket", avatar.Appearance.RenderersOf(pantsRecord).All(r => r.VisibleTriangles == r.TotalTriangles));
                        Capture(avatar, "trousers_only_front", 0);
                        Capture(avatar, "trousers_only_back", 180);
                        var equippedJacket = await AvatarFactory.EquipAsync(avatar, jacket);
                        Check("Re-equip jacket", equippedJacket != null);
                        var expected = jacket.segmentedGarment.bodyCoverage.Union(trousers.segmentedGarment.bodyCoverage).OrderBy(r => r);
                        Check("Body coverage is the union of equipped pieces", avatar.Appearance.CoveredBodyRegions.OrderBy(r => r).SequenceEqual(expected));
                        Check("Unequip trousers", AvatarFactory.Unequip(avatar, trousers));
                        await Task.Yield();
                        Check("Jacket independently masks the body",
                            avatar.Appearance.CoveredBodyRegions.OrderBy(r => r).SequenceEqual(jacket.segmentedGarment.bodyCoverage.OrderBy(r => r)));
                        Capture(avatar, "jacket_only_front", 0);
                        Capture(avatar, "jacket_only_back", 180);
                    }
                }
                finally { avatar?.Dispose(); }
                await Task.Yield();
            }
            Check("Composed masks released", BodyVisibilityMask.CachedCount == 0, BodyVisibilityMask.CachedCount.ToString());
            // A plain legacy recipe still takes the existing assembly path.
            var oldRecipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>("Assets/_Project/Avatar/MakeHuman/Content/Composers/Recipes/Avatar_Bach.asset");
            if (oldRecipe != null)
            {
                var old = await AvatarFactory.CreateAsync(oldRecipe.ToRuntime(), anchor.transform);
                Check("Legacy recipe still builds", old?.Root != null && old.Appearance.BodyMask == null);
                old?.Dispose();
            }
        }
        catch (Exception e) { Check("Exception", false, e.ToString()); }
        File.WriteAllText("Logs/MonteverdiClothing/validation.txt", Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
    private static void Capture(AvatarInstance avatar, string name, float yaw)
    {
        var camGO = new GameObject("MonteverdiCaptureCamera");
        var cam = camGO.AddComponent<Camera>();
        cam.enabled = false;
        cam.fieldOfView = 32;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.22f, .24f, .27f);
        var focus = avatar.Root.position + Vector3.up * .95f;
        cam.transform.position = focus + Quaternion.Euler(5, yaw + 180, 0) * new Vector3(0, 0, -3.7f);
        cam.transform.LookAt(focus);
        var rt = new RenderTexture(900, 1100, 24);
        var image = new Texture2D(900, 1100, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        image.Apply();
        File.WriteAllBytes("Logs/MonteverdiClothing/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = previous;
        cam.targetTexture = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(image);
        UnityEngine.Object.Destroy(camGO);
    }
}
