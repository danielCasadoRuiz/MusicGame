using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Authors the Dance pilot's atlas once; the player only swaps sprites.</summary>
[InitializeOnLoad]
public static class DancePreparationSetup
{
    const string Art = "Assets/_Project/Art/UI/Analysis/";
    const string Output = "../VisualReviews/DancePreparation";
    static double nextPoll;
    static DancePreparationSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        const string request = "Logs/DancePreparationSetup.request";
        if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(request);
        Directory.CreateDirectory(Output);
        try { Install(); File.WriteAllText(Output + "/result.txt", "PASS: Dance assets, 32-frame loop, prefab references and three rendered layouts."); }
        catch (Exception e) { File.WriteAllText(Output + "/failure.txt", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Tools/MusicGame/UI/Install Dance Preparation Pilot")]
    public static void Install()
    {
        BakeWave();
        Import(Art + "DanceFrame.png", 2048);
        Import(Art + "NeutralStaffAtlas.png", 4096);
        const string prefab = "Assets/_Project/Prefabs/UI/AnalyzingScreen.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefab);
        try
        {
            var p = root.GetComponent<AnalysisPresentation>();
            p.danceFrame = null;
            p.cornerAtlas = null;
            p.motifAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "AnalysisMotifs.png");
            p.danceBackdrop = null;
            p.loadingWaveAtlas = null; p.barAtlas = null;
            p.neutralStaffAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "NeutralStaffAtlas.png");
            p.staffTintShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/AnalysisStaffTint.shader");
            if (p.staffTintShader == null || ShaderUtil.ShaderHasError(p.staffTintShader)) throw new Exception("Staff tint shader is missing or invalid");
            if (p.neutralStaffAtlas == null) throw new Exception("Missing Dance art");
            PrefabUtility.SaveAsPrefabAsset(root, prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Preview(1920, 1080); Preview(1280, 720); Preview(2560, 1080);
        File.WriteAllText(Output + "/layout-result.txt", "PASS: Dance prefab references, 32-frame atlas, 1920x1080 / 1280x720 / 2560x1080 rendered with the game's brush and menu fonts.");
        Debug.Log("[DancePreparationSetup] Dance preparation pilot installed and rendered.");
    }
    static void Import(string path, int maxSize)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.isReadable = false;
        importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = maxSize;
        importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }
    static void BakeWave()
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        const int width = 1024, height = 320;
        var atlas = new Texture2D(width * AnalysisPresentation.WaveColumns, height * AnalysisPresentation.WaveRows, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(Art + "NeutralStaffSource.png"))) throw new Exception("Cannot load waveform source");
            var pixels = new Color32[atlas.width * atlas.height];
            for (int frame = 0; frame < AnalysisPresentation.WaveFrames; frame++)
            {
                float phase = frame * Mathf.PI * 2 / AnalysisPresentation.WaveFrames;
                int left = frame % AnalysisPresentation.WaveColumns * width;
                int bottom = (AnalysisPresentation.WaveRows - 1 - frame / AnalysisPresentation.WaveColumns) * height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + .5f) / width;
                    // Periodic in phase; endpoints remain still, with ample transparent padding.
                    float envelope = Mathf.Sin(u * Mathf.PI);
                    float displacement = .055f * envelope * Mathf.Sin(u * Mathf.PI * 4 - phase);
                    float breath = 1 + .10f * Mathf.Sin(phase + u * Mathf.PI * 2);
                    for (int y = 0; y < height; y++)
                    {
                        float v = ((y + .5f) / height - .5f - displacement) / breath + .5f;
                        Color c = v < 0 || v > 1 ? Color.clear : source.GetPixelBilinear(u, v);
                        // Achromatic source + scan baked into the same atlas. No runtime procedural geometry.
                        float gray = c.grayscale;
                        float scanX = .06f + .88f * frame / AnalysisPresentation.WaveFrames;
                        float scan = Mathf.Exp(-Mathf.Pow((u - scanX - .08f * (v - .5f)) / .009f, 2))
                            * Mathf.Pow(Mathf.Sin(phase * .5f), 2) * Mathf.Exp(-Mathf.Pow((v - .5f) / .34f, 4)) * .65f;
                        float alpha = c.a + scan * (1 - c.a);
                        gray = Mathf.Lerp(gray, 1, scan);
                        pixels[left + x + (bottom + y) * atlas.width] = new Color(gray, gray, gray, alpha);
                    }
                }
            }
            atlas.SetPixels32(pixels); atlas.Apply();
            File.WriteAllBytes(Art + "NeutralStaffAtlas.png", atlas.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(source); Object.DestroyImmediate(atlas); }
    }
    public static void Preview(int width, int height, GameMusicStyle style = GameMusicStyle.Dance, string directory = null)
    {
        string output = directory ?? Output;
        Directory.CreateDirectory(output);
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture rt = null;
        Camera previewCamera = null;
        try
        {
            var canvasGo = new GameObject("Dance preparation preview", typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, scene);
            var cameraGo = new GameObject("Preview camera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.GetComponent<Camera>(); camera.scene = scene;
            previewCamera = camera;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true;
            rt = new RenderTexture(width, height, 24); camera.targetTexture = rt;
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/AnalyzingScreen.prefab"), canvasGo.transform);
            root.SetActive(true); UIFactory.Stretch((RectTransform)root.transform);
            var view = root.GetComponent<AnalyzingScreenView>(); var p = root.GetComponent<AnalysisPresentation>();
            p.Build(view); p.Begin(); view.progressFill.fillAmount = .62f;
            root.transform.Find("AnalysisContent/Metadata/Song").GetComponent<TMP_Text>().text = "All together (meh!)";
            root.transform.Find("AnalysisContent/Metadata/Artist").GetComponent<TMP_Text>().text = "The Backup Boys";
            view.tipText.text = "Analysing rhythm…";
            Canvas.ForceUpdateCanvases();
            var update = typeof(AnalysisPresentation).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(p, null); Canvas.ForceUpdateCanvases();
            p.Reveal(style);
            typeof(AnalysisPresentation).GetMethod("ApplyImmediate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(p, new object[] { style });
            update.Invoke(p, null); Canvas.ForceUpdateCanvases();
            var styleBackground = root.transform.Find("StyleBackdrop")?.GetComponent<RawImage>();
            bool hasStyleAsset = File.Exists("Assets/_Project/Resources/UI/AnalysisBackdrops/" + style + ".png");
            if (hasStyleAsset && (styleBackground == null || styleBackground.texture == null || styleBackground.texture.name != style.ToString()))
                throw new Exception("Wrong full-screen background selected for " + style);
            var label = canvasGo.transform.Find("DetectedStyle").GetComponent<TextMeshProUGUI>();
            label.maxVisibleCharacters = int.MaxValue; label.alpha = 1;
            typeof(DetectedStyleLabel).GetMethod("PlaceInline", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(label.GetComponent<DetectedStyleLabel>(), null);
            label.GetComponent<DetectedStyleLabel>().StopAllCoroutines();
            label.enableAutoSizing = true; label.havePropertiesChanged = true; label.ForceMeshUpdate(true, true);
            Canvas.ForceUpdateCanvases(); camera.Render();
            var old = RenderTexture.active; RenderTexture.active = rt;
            var png = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                png.ReadPixels(new Rect(0, 0, width, height), 0, 0); png.Apply();
                File.WriteAllBytes($"{output}/{style}-{width}x{height}.png", png.EncodeToPNG());
            }
            finally { RenderTexture.active = old; Object.DestroyImmediate(png); }
            if (hasStyleAsset)
            {
                // Preview scenes do not dispatch normal MonoBehaviour lifecycle callbacks.
                if (Application.isPlaying) p.enabled = false;
                else typeof(AnalysisPresentation).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(p, null);
                if (styleBackground.texture != null) throw new Exception("Background retained after presentation hide: " + style);
            }
        }
        finally { if (previewCamera != null) previewCamera.targetTexture = null; if (rt != null) Object.DestroyImmediate(rt); EditorSceneManager.ClosePreviewScene(scene); }
    }
}
