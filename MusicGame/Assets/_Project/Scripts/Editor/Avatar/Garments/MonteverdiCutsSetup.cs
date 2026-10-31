using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Imports the approved segmented Monteverdi sources into their existing quality assets.</summary>
public static class MonteverdiCutsSetup
{
    private const string Art = "Assets/_Project/Art/Opponents/Monteverdi";
    private const string Content = "Assets/_Project/Avatar/MakeHuman/Content/Composers";
    private const string Configs = "Assets/_Project/Avatar/MakeHuman/Content/Garments";
    private const string SharedArt = "Assets/_Project/Avatar/MakeHuman/Generated";
    private static readonly string[] Pieces = { "Monteverdi_jacket", "monteverdi_pants_4", "Monteverdi_shoes", "Monteverdi_beard", "Monteverdi_hair" };

    [MenuItem("Tools/MusicGame/Avatars/Garments/Set Up Monteverdi Cuts")]
    public static void RunFromCommandLine()
    {
        var log = new StringBuilder();
        try
        {
            var recipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(Content + "/Recipes/Avatar_Monteverdi.asset");
            if (recipe == null) throw new InvalidOperationException("Monteverdi recipe not found.");
            string sourceRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../AssetsMaterial/3D_pipeline/Output"));
            bool first = true;
            foreach (string piece in Pieces)
            {
                string assetName = piece == "monteverdi_pants_4" ? "Monteverdi_pants" : piece;
                string id = assetName.ToLowerInvariant();
                var package = GarmentCutsPackage.Load(Path.Combine(sourceRoot, piece, "Rigged/Segmented"));
                if (!package.IsUsable) throw new InvalidOperationException(string.Join("\n", package.Issues));
                var options = new GarmentCutsImporter.Options
                {
                    ArtRoot = SharedArt,
                    ContentRoot = Configs,
                    BaseAvatarPath = AssetDatabase.GetAssetPath(recipe.baseAvatar),
                    ImportMasks = first,
                };
                if (assetName == "Monteverdi_hair") options.SkipWearableItems.Add(id);
                else options.ItemPaths[id] = Content + "/Items/Wearable_" + assetName + ".asset";
                foreach (var asset in package.Assets)
                {
                    asset.GarmentId = id;
                    var coverage = Coverage(id, package.Data.regionIds);
                    asset.coverage = coverage;
                    asset.coverageConfigured = true;
                    asset.Issues.RemoveAll(issue => issue.Message.StartsWith("coverageConfigured is false", StringComparison.Ordinal));
                    string folder = Art + "/" + assetName + "/" + asset.Quality;
                    string[] models = Directory.GetFiles(folder, "*.fbx");
                    if (models.Length != 1) throw new InvalidOperationException($"Expected one existing FBX in {folder}.");
                    string prefabPath = folder + "/" + assetName + "_" + asset.Quality + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    var renderer = prefab != null ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
                    if (renderer == null || renderer.sharedMaterial == null) throw new InvalidOperationException($"No authored material: {prefabPath}.");
                    options.ModelPaths[asset.Key] = models[0].Replace('\\', '/');
                    options.PrefabPaths[asset.Key] = prefabPath;
                    options.FinalMaterials[asset.Key] = renderer.sharedMaterial;
                }
                var result = GarmentCutsImporter.Import(package, options);
                log.AppendLine(string.Join("\n", result.Log));
                if (!result.Success) throw new InvalidOperationException($"{piece}: {result.Errors} import errors.");
                first = false;
            }
            log.AppendLine(SkinVisibilityShaderSetup.Apply());
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/MonteverdiCutsImport.txt", log.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode && Environment.GetCommandLineArgs().Contains("-validateOutfit"))
                EditorApplication.delayCall += MonteverdiClothingCheck.RunFromCommandLine;
            else if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            log.AppendLine(e.ToString());
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/MonteverdiCutsImport.txt", log.ToString());
            Debug.LogError(e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    private static GarmentCutsPackage.CoverageEntry Coverage(string id, List<string> regions)
    {
        var coverage = new GarmentCutsPackage.CoverageEntry { id = id, layer = 0, category = "accessory" };
        if (id.EndsWith("jacket", StringComparison.Ordinal))
        {
            coverage.category = "jacket";
            coverage.layer = 40;
            coverage.bodyCoverage = regions.Where(r => r.StartsWith("chest_") || r.StartsWith("abdomen_") ||
                r.StartsWith("back_") || r.StartsWith("shoulder_") || r.StartsWith("upperarm_") || r.StartsWith("forearm_")).ToList();
            coverage.garmentCoverage.Add(new GarmentCutsPackage.CoverageRuleEntry
            {
                targetCategories = new List<string> { "shirt", "top" },
                zones = new List<string>(coverage.bodyCoverage),
            });
            // Its long closed skirt covers the trouser seat; legs remain visible below it.
            coverage.garmentCoverage.Add(new GarmentCutsPackage.CoverageRuleEntry
            {
                targetCategories = new List<string> { "trousers" },
                zones = new List<string> { "buttocks" },
            });
        }
        else if (id.EndsWith("pants", StringComparison.Ordinal))
        {
            coverage.category = "trousers";
            coverage.layer = 20;
            coverage.bodyCoverage = regions.Where(r => r.StartsWith("pelvis_") || r == "buttocks" || r.StartsWith("thigh_")).ToList();
        }
        else if (id.EndsWith("shoes", StringComparison.Ordinal)) { coverage.category = "shoes"; coverage.layer = 30; }
        else if (id.EndsWith("beard", StringComparison.Ordinal)) { coverage.category = "beard"; coverage.layer = 50; }
        else if (id.EndsWith("hair", StringComparison.Ordinal)) { coverage.category = "hair"; coverage.layer = 50; }
        return coverage;
    }

    /// <summary>Revises authored coverage after visual inspection without re-importing model files.</summary>
    public static void UpdateCoverageFromCommandLine()
    {
        try
        {
            var recipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(Content + "/Recipes/Avatar_Monteverdi.asset");
            foreach (string id in new[] { "monteverdi_jacket", "monteverdi_pants", "monteverdi_shoes", "monteverdi_hair", "monteverdi_beard" })
            {
                var config = AssetDatabase.LoadAssetAtPath<SegmentedGarmentSO>(Configs + "/Garment_" + id + ".asset");
                if (config == null) throw new InvalidOperationException("Missing garment config " + id);
                var coverage = Coverage(id, recipe.baseAvatar.garmentRegionCatalog.regionIds);
                config.category = coverage.category;
                config.layer = coverage.layer ?? 0;
                config.bodyCoverage = coverage.bodyCoverage;
                config.garmentCoverage = coverage.garmentCoverage.Select(r => new GarmentCoverageRule
                    { targetCategories = r.targetCategories, zones = r.zones }).ToList();
                config.coverageConfigured = true;
                config.importedCoverageHash = config.ComputeCoverageHash();
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();
            if (Environment.GetCommandLineArgs().Contains("-validateOutfit"))
                EditorApplication.delayCall += MonteverdiClothingCheck.RunFromCommandLine;
            else if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
