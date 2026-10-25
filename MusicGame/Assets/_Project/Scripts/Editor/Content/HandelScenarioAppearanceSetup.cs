#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class HandelScenarioAppearanceSetup
{
    const string Folder = "Assets/_Project/Art/Arenas/Handel";
    const string Report = "Logs/HandelScenarioAppearance.txt";
    static readonly List<string> Checks = new();
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL " + message);
        Checks.Add("PASS " + message);
    }
    public static void RunFromCommandLine()
    {
        try { Apply(); File.WriteAllLines(Report, Checks); EditorApplication.Exit(0); }
        catch (Exception e) { Directory.CreateDirectory("Logs"); File.WriteAllText(Report, string.Join("\n", Checks) + "\n" + e); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    [MenuItem("Tools/MusicGame/Content/Arenas/Apply Corrected Handel Materials")]
    public static void Apply()
    {
        Checks.Clear(); Directory.CreateDirectory("Logs");
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../AssetsMaterial/Scenarios"));
        Directory.CreateDirectory(Folder + "/Textures"); Directory.CreateDirectory(Folder + "/Materials");
        AssetDatabase.Refresh();
        var albedo = Import(source + "/handel_terra_albedo.png", Folder + "/Textures/Handel_Floor_BaseColor.png", false, false);
        var normal = Import(source + "/handel_terra_bump.png", Folder + "/Textures/Handel_Floor_Height.png", false, true);
        var panorama = Import(source + "/handel_skybox.png", Folder + "/Textures/Handel_Skybox.png", true, false);
        var marble = MaterialAt(Folder + "/Materials/Handel_Floor.mat", "Universal Render Pipeline/Lit");
        marble.SetTexture("_BaseMap", albedo); marble.SetTexture("_MainTex", albedo);
        marble.SetColor("_BaseColor", Color.white); marble.SetColor("_Color", Color.white);
        marble.SetTextureScale("_BaseMap", new Vector2(4f, 4f));
        marble.SetTextureScale("_MainTex", new Vector2(4f, 4f));
        marble.SetTexture("_BumpMap", normal); marble.SetFloat("_BumpScale", .14f); marble.EnableKeyword("_NORMALMAP");
        marble.SetFloat("_Metallic", 0f); marble.SetFloat("_Smoothness", .82f);
        marble.SetFloat("_EnvironmentReflections", 1f); marble.SetFloat("_SpecularHighlights", 1f);
        marble.SetFloat("_Surface", 0f); marble.SetOverrideTag("RenderType", "Opaque");
        marble.enableInstancing = true; EditorUtility.SetDirty(marble);
        var sky = MaterialAt(Folder + "/Materials/Handel_Skybox.mat", "Skybox/Panoramic");
        sky.SetTexture("_MainTex", panorama); sky.SetFloat("_Mapping", 1f);
        sky.SetFloat("_ImageType", 0f); sky.SetFloat("_Layout", 0f); sky.SetFloat("_Exposure", 1f);
        // Keep the reference panorama centred along the arena forward direction.
        // Polished marble uses the arena reflection probe.
        sky.SetFloat("_Rotation", 0f); sky.SetColor("_Tint", new Color(.5f, .5f, .5f, 1));
        EditorUtility.SetDirty(sky);
        for (int version = 1; version <= 3; version++)
        {
            string prefabPath = $"{Folder}/Handel_Arena_{version:00}.prefab";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var floor = root.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Floor");
                floor.sharedMaterial = marble;
                floor.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                var env = root.GetComponent<ArenaEnvironmentSettings>();
                if (env == null) env = root.AddComponent<ArenaEnvironmentSettings>();
                env.overrideSkybox = true; env.skybox = sky; env.cameraClearsToSkybox = true;
                env.overrideAmbient = true; env.ambientMode = AmbientMode.Skybox; env.ambientIntensity = 1f;
                env.overrideMainLight = true; env.mainLightColor = new Color(1f, .84f, .68f);
                env.mainLightIntensity = 1.25f; env.mainLightEuler = new Vector3(25, 200, 0);
                env.mainLightShadows = LightShadows.Soft; env.mainLightShadowStrength = .6f;
                var lighting = root.transform.Find("Lighting") ?? root.transform;
                var probeTransform = lighting.Find("HandelFloorReflections");
                if (probeTransform == null) { probeTransform = new GameObject("HandelFloorReflections").transform; probeTransform.SetParent(lighting, false); }
                probeTransform.localPosition = new Vector3(0, 2, 0);
                var probe = probeTransform.GetComponent<ReflectionProbe>();
                if (probe == null) probe = probeTransform.gameObject.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                probe.resolution = 256; probe.boxProjection = true; probe.size = new Vector3(24, 12, 24);
                probe.center = new Vector3(0, 3, 0); probe.intensity = 1f; probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Check(marble.shader.isSupported && sky.shader.isSupported, "URP floor and panoramic skybox shaders supported");
        var heightImporter = (TextureImporter)AssetImporter.GetAtPath(Folder + "/Textures/Handel_Floor_Height.png");
        Check(heightImporter.textureType == TextureImporterType.NormalMap && heightImporter.convertToNormalmap && !heightImporter.sRGBTexture,
            "corrected grayscale bump imported as linear height-to-normal, not as RGB normal pixels");
        Check(marble.GetTexture("_BaseMap") == albedo && marble.GetTexture("_BumpMap") == normal && marble.IsKeywordEnabled("_NORMALMAP"), "corrected albedo and bump assigned, polished non-metallic marble enabled");
        string opponent = File.ReadAllText("Assets/_Project/Configs/Fight/Opponents/Opponent_Handel.asset");
        for (int version = 1; version <= 3; version++)
        {
            string prefabPath = $"{Folder}/Handel_Arena_{version:00}.prefab";
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Check(saved.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Floor").sharedMaterial == marble, $"Handel arena {version:00}: saved floor material");
            var env = saved.GetComponent<ArenaEnvironmentSettings>();
            Check(env != null && env.skybox == sky && env.overrideSkybox && env.cameraClearsToSkybox && saved.GetComponentInChildren<ReflectionProbe>(true) != null,
                $"Handel arena {version:00}: skybox, lighting and floor reflections saved");
            Check(opponent.Contains(AssetDatabase.AssetPathToGUID(prefabPath)), $"Handel arena {version:00}: referenced by opponent used in Fight scene");
        }
        Preview();
        File.WriteAllLines(Report, Checks); Debug.Log(string.Join("\n", Checks));
    }
    static Texture2D Import(string source, string destination, bool sky, bool height)
    {
        Check(File.Exists(source), "source " + Path.GetFileName(source));
        File.Copy(source, destination, true);
        AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(destination);
        importer.textureType = height ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !height; importer.convertToNormalmap = height;
        if (height) importer.heightmapScale = .035f;
        importer.wrapMode = sky ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        if (sky) importer.wrapModeU = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true; importer.maxTextureSize = sky ? 4096 : 2048;
        importer.npotScale = TextureImporterNPOTScale.None; importer.anisoLevel = sky ? 1 : 8;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaSource = TextureImporterAlphaSource.None; importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(destination);
    }
    static Material MaterialAt(string path, string shaderName)
    {
        var shader = Shader.Find(shaderName); if (shader == null) throw new Exception("Missing shader " + shaderName);
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        else material.shader = shader;
        return material;
    }
    static void Preview()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var arena = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Handel_Arena_01.prefab"), scene);
        var camera = new GameObject("HandelMaterialPreview").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.Skybox; camera.fieldOfView = 58; camera.farClipPlane = 500;
        var sun = new GameObject("PreviewSun").AddComponent<Light>(); sun.type = LightType.Directional;
        RenderSettings.sun = sun;
        var snapshot = arena.GetComponent<ArenaEnvironmentSettings>().Apply(sun, camera);
        var positions = new[] { new Vector3(0, 2.4f, -6.8f), new Vector3(0, 8f, -2.5f) };
        var targets = new[] { new Vector3(0, 1.4f, 4), Vector3.zero };
        Directory.CreateDirectory("Logs/HandelPreviews");
        for (int k = 0; k < positions.Length; k++)
        {
            camera.transform.position = positions[k]; camera.transform.LookAt(targets[k]);
            var rt = new RenderTexture(1440, 900, 24); camera.targetTexture = rt;
            camera.Render(); camera.Render();
            RenderTexture.active = rt;
            var pixels = new Texture2D(1440, 900, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); pixels.Apply();
            File.WriteAllBytes($"Logs/HandelPreviews/view_{k}.png", pixels.EncodeToPNG());
            RenderTexture.active = null; camera.targetTexture = null; rt.Release();
            Object.DestroyImmediate(pixels); Object.DestroyImmediate(rt);
        }
        ArenaEnvironmentSettings.Restore(snapshot);
        Check(true, "two Unity previews rendered from saved Handel arena, combat angle and floor detail");
    }
}
#endif
