using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.AddressableAssets;
using UnityEditor.AddressableAssets;

[InitializeOnLoad]
public static class WagnerHairSetup
{
    static WagnerHairSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Logs/WagnerHairSetup.request";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        File.Delete(request);
        Run();
    }
    const string Art = "Assets/_Project/Art/Opponents/Wagner/Wagner_hair";
    const string Content = "Assets/_Project/Avatar/MakeHuman/Content/Composers";
    static readonly string[] Qualities = { "Low", "Mid", "HighMid", "High", "Ultra" };
    static readonly string[] SourceDirs = { "LOW", "MEDIUM", "MID-HIGH", "HIGH", "ULTRA" };
    static readonly string[] Suffixes = { "LOW_5k", "MEDIUM_10k", "MIDHIGH_20k", "HIGH_30k", "ULTRA_50k" };

    public static void Run()
    {
        var report = new List<string>();
        try
        {
            var source = Path.GetFullPath("../AssetsMaterial/3D_pipeline/Output/Wagner_hair/Rigged");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new Exception("Missing Addressables settings");
            var itemPath = Content + "/Items/Hair_Wagner_hair.asset";
            var item = AssetDatabase.LoadAssetAtPath<HairItemSO>(itemPath);
            if (item == null) { item = ScriptableObject.CreateInstance<HairItemSO>(); AssetDatabase.CreateAsset(item, itemPath); }
            item.stableId = "wagner_hair";
            item.displayName = "Wagner hair";
            item.contentGroup = "Enemy_wagner";
            item.variantMode = AvatarItemVariantMode.Shared;
            item.sharedVariant = new AvatarItemVariant();
            var canonical = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Avatar/MakeHuman/Generated/MakeHuman_Canonical.fbx");
            var boneNames = canonical.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToHashSet();
            for (int i = 0; i < Qualities.Length; i++)
            {
                string q = Qualities[i], folder = Art + "/" + q;
                Directory.CreateDirectory(folder + "/Textures");
                Directory.CreateDirectory(folder + "/Materials");
                string file = "Wagner_hair_" + Suffixes[i] + "_rigged.fbx";
                string modelPath = folder + "/" + file;
                File.Copy(Path.Combine(source, SourceDirs[i], file), modelPath, true);
                foreach (string texture in new[] { "BaseColor", "Normal", "Metallic", "Roughness" })
                    File.Copy(Path.Combine(source, SourceDirs[i], "Textures", texture + ".png"), folder + "/Textures/" + texture + ".png", true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.importAnimation = false;
                importer.globalScale = 1;
                importer.useFileScale = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
                foreach (string texture in new[] { "BaseColor", "Normal", "Metallic", "Roughness" })
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(folder + "/Textures/" + texture + ".png");
                    ti.textureType = texture == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.sRGBTexture = texture == "BaseColor";
                    ti.SaveAndReimport();
                }
                var metal = ReadPng(folder + "/Textures/Metallic.png");
                var rough = ReadPng(folder + "/Textures/Roughness.png");
                if (metal.width != rough.width || metal.height != rough.height) throw new Exception("Texture size mismatch");
                var pixels = metal.GetPixels32(); var roughPixels = rough.GetPixels32();
                for (int p = 0; p < pixels.Length; p++) pixels[p].a = (byte)(255 - roughPixels[p].r);
                metal.SetPixels32(pixels); metal.Apply();
                string packed = folder + "/Textures/MetallicSmoothness.png";
                File.WriteAllBytes(packed, metal.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(metal); UnityEngine.Object.DestroyImmediate(rough);
                AssetDatabase.ImportAsset(packed, ImportAssetOptions.ForceSynchronousImport);
                var packedImporter = (TextureImporter)AssetImporter.GetAtPath(packed);
                packedImporter.sRGBTexture = false; packedImporter.SaveAndReimport();
                string matPath = folder + "/Materials/M_Wagner_hair_" + q + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (material == null)
                {
                    string template = AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project/Art/Opponents/Monteverdi/Monteverdi_hair/" + q + "/Materials" }).Select(AssetDatabase.GUIDToAssetPath).First();
                    material = new Material(AssetDatabase.LoadAssetAtPath<Material>(template));
                    AssetDatabase.CreateAsset(material, matPath);
                }
                material.name = "M_Wagner_hair_" + q;
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/Textures/BaseColor.png"));
                material.SetTexture("_MainTex", material.GetTexture("_BaseMap"));
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/Textures/Normal.png"));
                material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packed));
                material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP");
                EditorUtility.SetDirty(material);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var root = UnityEngine.Object.Instantiate(model);
                root.name = "Wagner_hair_" + q;
                foreach (var animator in root.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
                var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length == 0) throw new Exception(q + ": no skinned meshes");
                foreach (var renderer in renderers)
                {
                    if (renderer.sharedMesh == null || renderer.bones.Length == 0) throw new Exception(q + ": missing skin");
                    var missing = renderer.bones.Where(b => b == null || !boneNames.Contains(b.name)).ToArray();
                    if (missing.Length != 0) throw new Exception(q + ": incompatible bones");
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMesh.subMeshCount).ToArray();
                }
                var part = root.GetComponent<AvatarVisualPart>() ?? root.AddComponent<AvatarVisualPart>();
                part.skinnedRenderers = renderers;
                part.rootBone = renderers[0].rootBone;
                if (part.rootBone == null) throw new Exception(q + ": missing root bone");
                string prefab = folder + "/Wagner_hair_" + q + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefab);
                UnityEngine.Object.DestroyImmediate(root);
                var guid = AssetDatabase.AssetPathToGUID(prefab);
                item.sharedVariant.qualityPrefabs.variants.Add(new QualityAssetReference { qualityName = q, prefab = new AssetReferenceGameObject(guid) });
                var group = settings.FindGroup("Remote_Enemies_" + q);
                if (group == null) throw new Exception("Missing quality group " + q);
                var entry = settings.CreateOrMoveEntry(guid, group);
                entry.address = "Enemy_wagner/Hair_Wagner_hair/sharedVariant/qualityPrefabs@" + q;
                foreach (string label in new[] { "Category_Enemies", "Enemy_wagner", "Q_" + q }) { settings.AddLabel(label); entry.SetLabel(label, true); }
                report.Add("PASS " + q + ": skinned prefab, compatible canonical bones, PBR material and Addressables entry");
            }
            var identity = AssetDatabase.LoadAssetAtPath<AvatarIdentitySO>(Content + "/Identities/Identity_Wagner.asset");
            identity.hair = item;
            EditorUtility.SetDirty(identity); EditorUtility.SetDirty(item); EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            foreach (var variant in item.sharedVariant.qualityPrefabs.variants)
                if (variant.prefab.editorAsset == null) throw new Exception("Invalid prefab reference " + variant.qualityName);
            report.Add("PASS Wagner identity uses hair; all five quality references resolve");
            File.WriteAllLines("Logs/WagnerHairSetup.txt", report);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e) { report.Add(e.ToString()); File.WriteAllLines("Logs/WagnerHairSetup.txt", report); Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); }
    }
    static Texture2D ReadPng(string path) { var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); if (!t.LoadImage(File.ReadAllBytes(path))) throw new Exception(path); return t; }
}
