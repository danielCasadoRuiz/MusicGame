using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Occlusion masks in motion: every composer / player-style recipe at Ultra, posed at three moments of the
/// runner's locomotion clip (RunnerStyle_Default), captured front / side / back into Logs/SegmentedOutfitsPosed.
/// Menu: Tools > MusicGame > Avatars > Garments > Capture Outfits Running. Batch: -executeMethod SegmentedOutfitPoseCheck.RunFromCommandLine.</summary>
[InitializeOnLoad]
public static class SegmentedOutfitPoseCheck
{
    private const string Pending = "SegmentedOutfitPoseCheck.Pending";
    private const string Folder = "Logs/SegmentedOutfitsPosed";
    private const string StylePath = "Assets/_Project/Configs/Player/RunnerAnimation/Styles/RunnerStyle_Default.asset";

    static SegmentedOutfitPoseCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            _ = Run();
        };
    }

    [MenuItem("Tools/MusicGame/Avatars/Garments/Capture Outfits Running")]
    public static void RunFromCommandLine()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Debug/AvatarsClothesChecker.unity", OpenSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    private static async Task Run()
    {
        Directory.CreateDirectory(Folder);
        string report = "";
        try
        {
            await Task.Yield();
            var checker = UnityEngine.Object.FindAnyObjectByType<AvatarsClothesChecker>();
            if (checker != null) checker.enabled = false;
            var clip = AssetDatabase.LoadAssetAtPath<RunnerAnimationStyleSO>(StylePath)?.locomotion?.FirstOrDefault()?.clip
                       ?? throw new InvalidOperationException("No locomotion clip in " + StylePath);
            int q = Array.IndexOf(QualitySettings.names, "Ultra");
            if (q >= 0) QualitySettings.SetQualityLevel(q, false);
            var recipes = AssetDatabase.FindAssets("t:AvatarRecipeSO", new[] { "Assets/_Project/Avatar/MakeHuman/Content/Composers/Recipes", "Assets/_Project/Avatar/MakeHuman/Content/Player/Recipes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(r => r != null && r.items.Length > 0).OrderBy(r => r.name).ToList();
            var anchor = new GameObject("PoseCheckAnchor");
            foreach (var recipe in recipes)
            {
                var avatar = await AvatarFactory.CreateAsync(recipe.ToRuntime(), anchor.transform);
                try
                {
                    if (avatar?.Root == null || avatar.Animator == null) { report += $"FAIL {recipe.name}: no avatar/animator\n"; continue; }
                    var playable = AnimationPlayableUtilities.PlayClip(avatar.Animator, clip, out var graph);
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    try
                    {
                        foreach (float phase in new[] { 0.15f, 0.4f, 0.65f })
                        {
                            playable.SetTime(clip.length * phase);
                            graph.Evaluate();
                            await Task.Yield();
                            foreach (var (view, yaw) in new[] { ("front", 0f), ("side", 90f), ("back", 180f) })
                                Capture(avatar, $"{recipe.name}_{(int)(phase * 100):00}_{view}", yaw);
                        }
                    }
                    finally { graph.Destroy(); }
                    report += $"PASS {recipe.name}\n";
                }
                finally { avatar?.Dispose(); }
                await Task.Yield();
                var unload = Resources.UnloadUnusedAssets();
                while (!unload.isDone) await Task.Yield();
                GC.Collect();
            }
        }
        catch (Exception e) { report += "FAIL " + e + "\n"; }
        File.WriteAllText(Folder + "/report.txt", report);
        Debug.Log(report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }

    private static void Capture(AvatarInstance avatar, string name, float yaw)
    {
        var go = new GameObject("PoseCaptureCamera");
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 34;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.22f, .24f, .27f);
        var focus = avatar.Root.position + Vector3.up * .95f;
        camera.transform.position = focus + Quaternion.Euler(5, yaw + 180, 0) * new Vector3(0, 0, -4.2f);
        camera.transform.LookAt(focus);
        var rt = new RenderTexture(500, 700, 24);
        var image = new Texture2D(500, 700, TextureFormat.RGB24, false);
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
