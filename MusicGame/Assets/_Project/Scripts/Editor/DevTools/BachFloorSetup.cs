#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BachFloorSetup
{
    const string Root = "Assets/_Project/Art/Arenas/Bach";
    const string Tex = Root + "/Textures";
    const string MatPath = Root + "/Materials/Bach_Paviment.mat";
    const string PrefabPath = Root + "/Bach_Arena_01.prefab";
    const string ScenePath = Root + "/Scenes/Bach_Arena_01.unity";

    [MenuItem("Tools/MusicGame/Content/Arenas/Apply Bach Floor")]
    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Scenes");
            AssetDatabase.Refresh();
            var albedo = Import("Bach_Paviment_Albedo.png", true, false);
            Import("Bach_Paviment_Bump.png", false, false);
            var normal = Import("Bach_Paviment_NormalFromBump.png", false, true);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new Exception("URP Lit shader not found");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MatPath); }
            material.shader = shader;
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 0.35f);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.48f);
            material.SetTextureScale("_BaseMap", new Vector2(4, 4));
            material.SetTextureScale("_BumpMap", new Vector2(4, 4));
            EditorUtility.SetDirty(material);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var floor = root.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Floor");
                floor.sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();

            var scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                var arena = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Bach_Arena_01");
                if (arena == null) arena = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
                var floor = arena.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Floor");
                floor.sharedMaterial = material;
                PrefabUtility.RecordPrefabInstancePropertyModifications(floor);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            AssetDatabase.SaveAssets();

            // Validate the serialized assets, including reopening the saved scene.
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var savedFloor = saved.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Floor");
            if (savedFloor.sharedMaterial != material || material.GetTexture("_BaseMap") != albedo || material.GetTexture("_BumpMap") != normal)
                throw new Exception("Prefab/material validation failed");
            var check = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var floor = check.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)).Single(r => r.name == "Floor");
                if (floor.sharedMaterial != material) throw new Exception("Scene floor material validation failed");
            }
            finally { EditorSceneManager.CloseScene(check, true); }
            var report = "PASS\nPrefab: " + PrefabPath + "\nScene: " + ScenePath + "\nMaterial: " + MatPath
                + "\nAlbedo: sRGB; bump: linear; derived normal: Unity height conversion\nUV tiling: 4x4 on existing 20m floor\nMetallic: 0; Smoothness: 0.48\nPrefab and saved scene references verified.\nTexture edge seamlessness is not guaranteed.\n";
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/BachFloorSetup.txt", report);
            Debug.Log(report);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/BachFloorSetup.txt", "FAIL\n" + e);
            Debug.LogException(e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    static Texture2D Import(string filename, bool srgb, bool normal)
    {
        string path = Tex + "/" + filename;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new Exception("Missing texture " + path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = srgb;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        if (normal) { importer.convertToNormalmap = true; importer.heightmapScale = 0.03f; }
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
#endif
