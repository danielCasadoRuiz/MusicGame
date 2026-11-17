using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>Builds and loads the six delivered wearables without building unrelated scenes or songs.</summary>
public static class WearableDeliveryBundles
{
    const string Folder = "Logs/WearableDelivery/Bundles";
    static readonly string[] Names = { "classical_gloves", "Dance_jacket", "Dance_trousers", "fight_gloves", "Groove_sweater", "Rock_jacket" };

    public static void RunFromCommandLine()
    {
        int exit = 0;
        try { Build(); }
        catch (Exception e) { exit = 1; Debug.LogException(e); Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/failure.txt", e.ToString()); }
        string restoreProfile = ContentUpdateTools.Arg("-restoreProfile");
        if (!string.IsNullOrEmpty(restoreProfile))
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            settings.activeProfileId = settings.profileSettings.GetProfileId(restoreProfile);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
        EditorApplication.Exit(exit);
    }

    static void CopyTree(string source, string target)
    {
        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, file.Substring(source.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(file, destination, true);
        }
    }

    static void Build()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        string profile = settings.activeProfileId;
        string oldBuild = settings.profileSettings.GetValueByName(profile, AddressableAssetSettings.kRemoteBuildPath);
        string oldLoad = settings.profileSettings.GetValueByName(profile, AddressableAssetSettings.kRemoteLoadPath);
        var schemas = settings.groups.Where(g => g != null).Select(g => (group: g, schema: g.GetSchema<BundledAssetGroupSchema>())).Where(x => x.schema != null).ToArray();
        var included = schemas.ToDictionary(x => x.schema, x => x.schema.IncludeInBuild);
        var selected = Names.SelectMany(name => AssetDatabase.LoadAssetAtPath<WearableItemSO>($"Assets/_Project/Avatar/MakeHuman/Content/Player/Items/Wearable_{name}.asset")
            .sharedVariant.qualityPrefabs.variants).Select(v => v.prefab.AssetGUID).ToHashSet();
        if (selected.Count != 30) throw new Exception("Expected 30 selected wearable prefabs.");
        var moved = new List<(string guid, AddressableAssetGroup group)>();
        string output = Path.GetFullPath(Folder + "/" + EditorUserBuildSettings.activeBuildTarget).Replace('\\', '/');
        string cache = Path.GetFullPath(Addressables.BuildPath).TrimEnd(Path.DirectorySeparatorChar);
        if (!cache.StartsWith(Path.GetFullPath("Library") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected Addressables cache path.");
        string saved = Path.GetFullPath(Folder + "/PreviousRuntime");
        string runtime = Path.GetFullPath(Folder + "/BuiltRuntime");
        var original = new HashSet<string>(Directory.Exists(cache) ? Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Select(f => f.Substring(cache.Length + 1)) : Array.Empty<string>());
        CopyTree(cache, saved);
        var builder = settings.DataBuilders[settings.ActivePlayerDataBuilderIndex];
        string builderState = EditorJsonUtility.ToJson(builder);
        AddressableAssetGroup scratch = null;
        var report = new StringBuilder();
        Directory.CreateDirectory(output);
        try
        {
            scratch = settings.CreateGroup("WearableDelivery_TemporaryExcluded", false, false, false, null, typeof(BundledAssetGroupSchema));
            scratch.GetSchema<BundledAssetGroupSchema>().IncludeInBuild = false;
            foreach (var pair in schemas)
            {
                bool clothing = pair.group.Name.StartsWith("Remote_PlayerClothing_");
                pair.schema.IncludeInBuild = clothing || pair.group.Name == "Core_Shared_Shaders";
                if (!clothing) continue;
                foreach (var entry in pair.group.entries.ToArray())
                    if (!selected.Contains(entry.guid))
                    {
                        moved.Add((entry.guid, pair.group));
                        settings.CreateOrMoveEntry(entry.guid, scratch, false, false);
                    }
            }
            File.WriteAllLines(Folder + "/temporary-entry-moves.txt", moved.Select(x => x.guid + " " + x.group.Name));
            settings.profileSettings.SetValue(profile, AddressableAssetSettings.kRemoteBuildPath, output);
            settings.profileSettings.SetValue(profile, AddressableAssetSettings.kRemoteLoadPath, output);
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (!string.IsNullOrEmpty(result.Error)) throw new Exception(result.Error);
            report.AppendLine($"PASS: built {result.AssetBundleBuildResults.Count} bundles in {result.Duration:0.0}s.");
            string catalog = Directory.GetFiles(output, "catalog*", SearchOption.TopDirectoryOnly).First(p => p.EndsWith(".bin") || p.EndsWith(".json"));
            var catalogHandle = Addressables.LoadContentCatalogAsync(catalog, false);
            var locator = catalogHandle.WaitForCompletion();
            if (locator == null) throw new Exception("Could not load generated catalog.");
            foreach (string guid in selected)
            {
                if (!locator.Locate(guid, typeof(GameObject), out var locations) || locations.Count != 1) throw new Exception("Missing catalog prefab: " + guid);
                var handle = Addressables.LoadAssetAsync<GameObject>(locations[0]);
                var prefab = handle.WaitForCompletion();
                if (prefab == null) throw new Exception("Could not load bundle prefab: " + guid);
                var parts = prefab.GetComponentsInChildren<SegmentedGarmentPart>(true);
                if (parts.Length == 0 || parts.Any(p => !p.TryValidate(out _))) throw new Exception("Invalid bundled masks: " + prefab.name);
                if (prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.sharedMesh == null || r.sharedMaterials.Any(m => m == null || m.shader == null))) throw new Exception("Invalid bundled model/material: " + prefab.name);
                report.AppendLine("PASS: loaded bundled mesh, materials and masks: " + prefab.name);
                Addressables.Release(handle);
            }
            Addressables.Release(catalogHandle);
            CopyTree(cache, runtime);
            report.AppendLine("PASS: all 30 quality prefabs loaded from their generated bundles. Original Addressables groups and profile paths restored.");
            File.WriteAllText(Folder + "/validation.txt", report.ToString());
            Debug.Log(report.ToString());
        }
        finally
        {
            foreach (var item in moved) settings.CreateOrMoveEntry(item.guid, item.group, false, false);
            if (scratch != null) settings.RemoveGroup(scratch);
            foreach (var pair in included) { pair.Key.IncludeInBuild = pair.Value; EditorUtility.SetDirty(pair.Key); }
            settings.profileSettings.SetValue(profile, AddressableAssetSettings.kRemoteBuildPath, oldBuild);
            settings.profileSettings.SetValue(profile, AddressableAssetSettings.kRemoteLoadPath, oldLoad);
            EditorJsonUtility.FromJsonOverwrite(builderState, builder);
            EditorUtility.SetDirty(builder);
            // Keep generated runtime files with the delivery, preserving the original local cache.
            CopyTree(cache, runtime);
            foreach (var file in Directory.GetFiles(cache, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(cache.Length + 1);
                if (!original.Contains(relative))
                {
                    string destination = Path.Combine(runtime, "GeneratedCacheFiles", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Move(file, destination);
                }
            }
            CopyTree(saved, cache);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }
}
