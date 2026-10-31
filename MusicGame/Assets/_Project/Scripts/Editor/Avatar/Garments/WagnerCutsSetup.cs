using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Imports Wagner's cut garments into their existing assets and addressable prefabs.</summary>
public static class WagnerCutsSetup
{
    private const string Art = "Assets/_Project/Art/Opponents/Wagner";
    private const string Content = "Assets/_Project/Avatar/MakeHuman/Content/Composers";
    private const string Configs = "Assets/_Project/Avatar/MakeHuman/Content/Garments";
    private static readonly string[] Pieces = { "Wagner_hair", "Wagner_shirt", "Wagner_jacket", "Wagner_pants" };

    [MenuItem("Tools/MusicGame/Avatars/Garments/Set Up Wagner Cuts")]
    public static void RunFromCommandLine()
    {
        var log = new StringBuilder();
        try
        {
            var recipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(Content + "/Recipes/Avatar_Wagner.asset");
            if (recipe?.baseAvatar?.garmentRegionCatalog == null || recipe.baseAvatar.garmentRegionCatalog.bodyMasks.Count != 63)
                throw new InvalidOperationException("The shared avatar body-mask catalog must be installed first.");
            string sourceRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../AssetsMaterial/3D_pipeline/Output"));
            foreach (string piece in Pieces)
            {
                string id = piece.ToLowerInvariant();
                var package = GarmentCutsPackage.Load(Path.Combine(sourceRoot, piece, "Rigged/Segmented"));
                if (!package.IsUsable) throw new InvalidOperationException(string.Join("\n", package.Issues));
                var options = new GarmentCutsImporter.Options
                {
                    ArtRoot = "Assets/_Project/Avatar/MakeHuman/Generated",
                    ContentRoot = Configs,
                    BaseAvatarPath = AssetDatabase.GetAssetPath(recipe.baseAvatar),
                    ImportMasks = false,
                    AssignCatalogToBaseAvatar = false,
                };
                if (piece.EndsWith("hair", StringComparison.Ordinal)) options.SkipWearableItems.Add(id);
                else options.ItemPaths[id] = Content + "/Items/Wearable_" + piece + ".asset";
                foreach (var asset in package.Assets)
                {
                    if (asset.GarmentId != id || !asset.coverageConfigured)
                        throw new InvalidOperationException("Missing garment coverage: " + asset.source);
                    string folder = Art + "/" + piece + "/" + asset.Quality;
                    string[] models = Directory.GetFiles(folder, "*.fbx");
                    if (models.Length != 1) throw new InvalidOperationException($"Expected one existing FBX in {folder}.");
                    string prefabPath = folder + "/" + piece + "_" + asset.Quality + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    var renderer = prefab != null ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
                    if (renderer?.sharedMaterial == null) throw new InvalidOperationException("Missing authored material: " + prefabPath);
                    options.ModelPaths[asset.Key] = models[0].Replace('\\', '/');
                    options.PrefabPaths[asset.Key] = prefabPath;
                    options.FinalMaterials[asset.Key] = renderer.sharedMaterial;
                }
                var result = GarmentCutsImporter.Import(package, options);
                log.AppendLine(string.Join("\n", result.Log));
                if (!result.Success) throw new InvalidOperationException($"{piece}: {result.Errors} import errors.");
            }
            log.AppendLine("Shared body masks reused; no additional mask textures created.");
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/WagnerCutsImport.txt", log.ToString());
            Debug.Log(log.ToString());
            if (Environment.GetCommandLineArgs().Contains("-validateOutfit"))
                EditorApplication.delayCall += WagnerClothingCheck.RunFromCommandLine;
            else if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            log.AppendLine(e.ToString());
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/WagnerCutsImport.txt", log.ToString());
            Debug.LogError(e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
