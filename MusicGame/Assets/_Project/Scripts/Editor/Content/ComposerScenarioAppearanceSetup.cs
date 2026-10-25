#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ComposerScenarioAppearanceSetup
{
    const string Root = "Assets/_Project/Art/Arenas/";
    static readonly string[] Composers = { "Brahms", "Chopin", "Haydn", "Mozart", "Pachelbel", "Tchaikovsky", "Wagner", "Vivaldi" };
    [MenuItem("Tools/MusicGame/Content/Arenas/Apply Composer Scenario Textures")]
    public static void Apply()
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../AssetsMaterial/Scenarios"));
        var report = new List<string>();
        foreach (string composer in Composers)
        {
            string prefix = composer == "Tchaikovsky" ? "Txaikovski" : composer;
            string skySource = Directory.GetFiles(source, prefix + "*skybox.png").SingleOrDefault();
            if (composer == "Vivaldi") skySource = Path.GetFullPath(Path.Combine(source, "../3D_pipeline/Vivaldi_scenario_skybox.png"));
            if (string.IsNullOrEmpty(skySource) || !File.Exists(skySource)) throw new FileNotFoundException("Missing sky: " + composer);
            string folder = Root + composer;
            Directory.CreateDirectory(folder + "/Textures");
            Directory.CreateDirectory(folder + "/Materials");
            AssetDatabase.Refresh();
            Texture2D sky = Import(skySource, folder + "/Textures/" + composer + "_Skybox.png", true, false);
            Material skyMat = MaterialAt(folder + "/Materials/" + composer + "_Skybox.mat", "Skybox/Panoramic");
            skyMat.SetTexture("_MainTex", sky);
            skyMat.SetFloat("_Mapping", 1); skyMat.SetFloat("_ImageType", 0); skyMat.SetFloat("_Layout", 0);
            skyMat.SetFloat("_Exposure", 1); skyMat.SetFloat("_Rotation", 0); skyMat.SetColor("_Tint", new Color(.5f,.5f,.5f,1));
            EditorUtility.SetDirty(skyMat);
            string floorSource = Directory.GetFiles(source, prefix + "*floor*.png").SingleOrDefault(p => p.Contains("basecolor") || p.Contains("albedo"));
            string bumpSource = Directory.GetFiles(source, prefix + "*floor*bump.png").SingleOrDefault();
            Material floorMat = null;
            if (floorSource != null)
            {
                Texture2D albedo = Import(floorSource, folder + "/Textures/" + composer + "_Floor_BaseColor.png", false, false);
                Texture2D normal = bumpSource == null ? null : Import(bumpSource, folder + "/Textures/" + composer + "_Floor_Height.png", false, true);
                floorMat = MaterialAt(folder + "/Materials/" + composer + "_Floor.mat", "Universal Render Pipeline/Lit");
                floorMat.SetTexture("_BaseMap", albedo); floorMat.SetTexture("_MainTex", albedo);
                floorMat.SetColor("_BaseColor", Color.white); floorMat.SetColor("_Color", Color.white);
                floorMat.SetTextureScale("_BaseMap", new Vector2(2,2)); floorMat.SetTextureScale("_MainTex", new Vector2(2,2));
                floorMat.SetFloat("_Metallic", 0); floorMat.SetFloat("_Smoothness", composer == "Brahms" ? .35f : .65f);
                if (normal != null) { floorMat.SetTexture("_BumpMap", normal); floorMat.SetFloat("_BumpScale", .2f); floorMat.EnableKeyword("_NORMALMAP"); }
                EditorUtility.SetDirty(floorMat);
            }
            string prefabPath = folder + "/" + composer + "_Arena_01.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var settings = root.GetComponent<ArenaEnvironmentSettings>();
                if (settings == null) settings = root.AddComponent<ArenaEnvironmentSettings>();
                settings.overrideSkybox = true; settings.skybox = skyMat; settings.cameraClearsToSkybox = true;
                if (floorMat != null)
                {
                    var floor = root.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.gameObject.name == "Floor");
                    floor.sharedMaterial = floorMat;
                }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var env = saved.GetComponent<ArenaEnvironmentSettings>();
            if (!env || !env.overrideSkybox || env.skybox != skyMat || !env.cameraClearsToSkybox) throw new Exception("Skybox validation failed: " + composer);
            if (floorMat != null && saved.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Floor").sharedMaterial != floorMat) throw new Exception("Floor validation failed: " + composer);
            if (skyMat.shader == null || !skyMat.shader.isSupported || (floorMat != null && !floorMat.shader.isSupported)) throw new Exception("Unsupported shader: " + composer);
            report.Add("PASS " + composer + "_Arena_01: skybox" + (floorMat != null ? ", floor base color, height converted to normal" : "; no named floor source found, existing floor retained"));
        }
        report.Add("Monteverdi, arena versions 02+ and unrelated assets untouched. Missing named sources: Bach, Beethoven, Handel; Vivaldi floor.");
        Directory.CreateDirectory("Logs"); File.WriteAllLines("Logs/ComposerScenarioAppearance.txt", report);
        Debug.Log(string.Join("\n", report));
    }
    static Texture2D Import(string source, string target, bool sky, bool height)
    {
        if (!File.Exists(target) || !File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target))) File.Copy(source, target, true);
        AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(target);
        importer.textureType = height ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !height; importer.wrapMode = sky ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.mipmapEnabled = !sky; importer.maxTextureSize = sky ? 4096 : 2048;
        if (height) { importer.convertToNormalmap = true; importer.heightmapScale = .05f; }
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(target);
    }
    static Material MaterialAt(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName); if (shader == null) throw new Exception("Missing shader " + shaderName);
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material,path); }
        else material.shader = shader;
        return material;
    }
    public static void RunFromCommandLine()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception error) { Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/ComposerScenarioAppearance.txt", error.ToString()); Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
#endif
