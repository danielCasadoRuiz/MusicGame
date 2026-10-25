#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BeethovenScenarioAppearanceSetup
{
    const string Root = "Assets/_Project/Art/Arenas/Beethoven";
    const string Prefab = Root + "/Beethoven_Arena_01.prefab";
    const string Report = "Logs/BeethovenScenarioAppearance.txt";

    [MenuItem("Tools/MusicGame/Content/Arenas/Apply Beethoven Scenario Textures")]
    public static void Apply()
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../AssetsMaterial/Scenarios"));
        foreach (string suffix in new[] { "skybox", "floor_albedo", "floor_bump" })
            if (!File.Exists(Path.Combine(source, "Beethoven_scenario_" + suffix + ".png")))
                throw new FileNotFoundException("Missing Beethoven source: " + suffix);
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(Prefab)) throw new Exception("Missing Beethoven arena");
        string guid = AssetDatabase.AssetPathToGUID(Prefab);
        string opponentPath = "Assets/_Project/Configs/Fight/Opponents/Opponent_Beethoven.asset";
        if (!File.ReadAllText(opponentPath).Contains("m_AssetGUID: " + guid))
            throw new Exception("Beethoven opponent does not reference this arena");

        Directory.CreateDirectory(Root + "/Textures");
        Directory.CreateDirectory(Root + "/Materials");
        AssetDatabase.Refresh();
        Texture2D albedo = Import(source, "floor_albedo", "Floor_BaseColor", false, false);
        Texture2D normal = Import(source, "floor_bump", "Floor_Height", true, false);
        Texture2D sky = Import(source, "skybox", "Skybox", false, true);
        Material floor = MaterialAt(Root + "/Materials/Beethoven_Floor.mat", "Universal Render Pipeline/Lit");
        floor.SetTexture("_BaseMap", albedo);
        floor.SetTexture("_MainTex", albedo);
        floor.SetColor("_BaseColor", Color.white);
        floor.SetColor("_Color", Color.white);
        floor.SetTextureScale("_BaseMap", new Vector2(2, 2));
        floor.SetTextureScale("_MainTex", new Vector2(2, 2));
        floor.SetFloat("_Metallic", 0);
        floor.SetFloat("_Smoothness", .8f);
        floor.SetTexture("_BumpMap", normal);
        floor.SetFloat("_BumpScale", .12f);
        floor.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(floor);

        Material skyMat = MaterialAt(Root + "/Materials/Beethoven_Skybox.mat", "Skybox/Panoramic");
        skyMat.SetTexture("_MainTex", sky);
        skyMat.SetFloat("_Mapping", 1);
        skyMat.SetFloat("_ImageType", 0);
        skyMat.SetFloat("_Layout", 0);
        skyMat.SetFloat("_Exposure", 1);
        skyMat.SetFloat("_Rotation", 0);
        skyMat.SetColor("_Tint", new Color(.5f, .5f, .5f, 1));
        EditorUtility.SetDirty(skyMat);

        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var renderer = root.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Floor");
            renderer.sharedMaterial = floor;
            var env = root.GetComponent<ArenaEnvironmentSettings>();
            if (!env) env = root.AddComponent<ArenaEnvironmentSettings>();
            env.overrideSkybox = true;
            env.skybox = skyMat;
            env.cameraClearsToSkybox = true;
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();

        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        var settings = saved.GetComponent<ArenaEnvironmentSettings>();
        if (!settings || !settings.overrideSkybox || settings.skybox != skyMat || !settings.cameraClearsToSkybox)
            throw new Exception("Saved skybox assignment invalid");
        if (saved.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Floor").sharedMaterial != floor)
            throw new Exception("Saved floor assignment invalid");
        if (floor.GetTexture("_BaseMap") != albedo || floor.GetTexture("_BumpMap") != normal || !floor.IsKeywordEnabled("_NORMALMAP"))
            throw new Exception("Floor texture assignments invalid");
        if (albedo.width != normal.width || albedo.height != normal.height)
            throw new Exception("Albedo/height dimensions differ");
        var dependencies = AssetDatabase.GetDependencies(Prefab, true);
        foreach (var texture in new[] { albedo, normal, sky })
            if (!dependencies.Contains(AssetDatabase.GetAssetPath(texture)))
                throw new Exception("Missing arena dependency: " + texture.name);
        if (AssetDatabase.AssetPathToGUID(Prefab) != guid) throw new Exception("Arena GUID changed");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(Report, "PASS Beethoven_Arena_01: corrected source textures imported; URP floor albedo and height converted to normal assigned; smoothness 0.8, metallic 0, normal strength 0.12; panoramic skybox and camera override assigned. Saved prefab dependencies and Opponent_Beethoven reference verified. Arena 02/03 and shared Fight scene unchanged.\n");
        Debug.Log(File.ReadAllText(Report));
    }

    static Texture2D Import(string source, string suffix, string target, bool height, bool sky)
    {
        string path = Root + "/Textures/Beethoven_" + target + ".png";
        File.Copy(Path.Combine(source, "Beethoven_scenario_" + suffix + ".png"), path, true);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = height ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !height;
        importer.wrapMode = sky ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.mipmapEnabled = !sky;
        importer.maxTextureSize = sky ? 4096 : 2048;
        importer.anisoLevel = sky ? 1 : 8;
        if (height) { importer.convertToNormalmap = true; importer.heightmapScale = .025f; }
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material MaterialAt(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (!shader) throw new Exception("Missing shader: " + shaderName);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        else material.shader = shader;
        return material;
    }

    public static void RunFromCommandLine()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception e)
        {
            Directory.CreateDirectory("Logs"); File.WriteAllText(Report, e.ToString());
            Debug.LogException(e); EditorApplication.Exit(1);
        }
    }
}
#endif
