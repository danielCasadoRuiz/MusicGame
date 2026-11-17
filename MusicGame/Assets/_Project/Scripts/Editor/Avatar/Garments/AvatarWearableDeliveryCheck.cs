using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Checks the six wearable delivery, composer gloves, and Runner/Fight equipment at all qualities.</summary>
[InitializeOnLoad]
public static class AvatarWearableDeliveryCheck
{
    const string Content = "Assets/_Project/Avatar/MakeHuman/Content";
    const string Pending = "AvatarWearableDeliveryCheck.Pending";
    const string Folder = "Logs/WearableDelivery";
    static readonly string[] Names = { "classical_gloves", "Dance_jacket", "Dance_trousers", "fight_gloves", "Groove_sweater", "Rock_jacket" };
    static readonly StringBuilder Report = new();
    static int failures;

    static AvatarWearableDeliveryCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            _ = Run();
        };
    }

    public static void RunFromCommandLine()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Debug/AvatarsClothesChecker.unity", OpenSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    static void Check(string label, bool ok)
    {
        Report.AppendLine($"{(ok ? "PASS" : "FAIL")} {label}");
        if (!ok) failures++;
    }

    static async Task Run()
    {
        Directory.CreateDirectory(Folder);
        int originalQuality = QualitySettings.GetQualityLevel();
        try
        {
            await Task.Yield();
            var checker = UnityEngine.Object.FindAnyObjectByType<AvatarsClothesChecker>();
            if (checker != null) checker.enabled = false;
            var config = AssetDatabase.LoadAssetAtPath<PlayerAvatarConfigSO>("Assets/_Project/Configs/Player/PlayerAvatarConfig.asset");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            foreach (string name in Names)
            {
                var item = AssetDatabase.LoadAssetAtPath<WearableItemSO>($"{Content}/Player/Items/Wearable_{name}.asset");
                Check(name + " item and masks", item != null && item.segmentedGarment != null);
                if (item == null) continue;
                var variants = item.sharedVariant.qualityPrefabs.variants;
                Check(name + " five qualities", variants.Count == 5 && variants.Select(v => v.qualityName).Distinct().Count() == 5);
                foreach (var variant in variants)
                {
                    var entry = settings.FindAssetEntry(variant.prefab.AssetGUID);
                    Check(name + " " + variant.qualityName + " addressable", entry != null && entry.parentGroup.Name == "Remote_PlayerClothing_" + variant.qualityName
                        && entry.labels.Contains(item.ContentGroupOrDefault) && entry.labels.Contains("Q_" + variant.qualityName));
                    var prefab = variant.prefab.editorAsset;
                    Check(name + " " + variant.qualityName + " valid mask", prefab != null && prefab.GetComponentsInChildren<SegmentedGarmentPart>(true).All(p => p.TryValidate(out _)));
                }
            }
            var classic = AssetDatabase.LoadAssetAtPath<WearableItemSO>($"{Content}/Player/Items/Wearable_classical_gloves.asset");
            var composers = AssetDatabase.FindAssets("t:AvatarRecipeSO", new[] { Content + "/Composers/Recipes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            foreach (var recipe in composers)
            {
                Check(recipe.name + " classical gloves exactly once", classic != null && recipe.items.Count(i => i == classic) == 1 && !recipe.items.Contains(config.combatGloves));
                Check(recipe.name + " shared glove download label", classic != null && classic.sharedVariant.qualityPrefabs.variants.All(v =>
                    settings.FindAssetEntry(v.prefab.AssetGUID).labels.Contains(ContentKeys.Enemy(recipe.name.Substring("Avatar_".Length).ToLowerInvariant()))));
            }
            var playerRecipes = AssetDatabase.FindAssets("t:AvatarRecipeSO", new[] { Content + "/Player/Recipes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            foreach (var recipe in playerRecipes)
            {
                var before = recipe.items.ToArray();
                Check(recipe.name + " Runner excludes fight gloves", config.combatGloves != null && !config.CreateRuntimeRecipe(recipe, false).Items.Contains(config.combatGloves));
                Check(recipe.name + " Fight includes gloves once", config.CreateRuntimeRecipe(recipe, true).Items.Count(i => i == config.combatGloves) == 1);
                Check(recipe.name + " source recipe preserved", before.SequenceEqual(recipe.items) && !recipe.items.Contains(config.combatGloves));
            }
            var selected = playerRecipes.Where(r => new[] { "Avatar_Player_Dance", "Avatar_Player_Groove", "Avatar_Player_Rock" }.Contains(r.name)).ToArray();
            Check("three updated style recipes", selected.Length == 3);
            Check("Groove jacket replaced by sweater", selected.First(r => r.name == "Avatar_Player_Groove").items.Any(i => i.stableId == "groove_sweater")
                && selected.First(r => r.name == "Avatar_Player_Groove").items.All(i => i.stableId != "groove_jacket"));
            Check("Dance trousers equipped", selected.First(r => r.name == "Avatar_Player_Dance").items.Any(i => i.stableId == "dance_trousers"));
            var anchor = new GameObject("DeliveryCheckAnchor");
            foreach (string quality in new[] { "Low", "Mid", "HighMid", "High", "Ultra" })
            {
                QualitySettings.SetQualityLevel(Array.IndexOf(QualitySettings.names, quality), false);
                foreach (var recipe in selected)
                foreach (bool combat in new[] { false, true })
                    await Build(config.CreateRuntimeRecipe(recipe, combat), anchor.transform, $"{recipe.name}_{(combat ? "Fight" : "Runner")}_{quality}", quality == "Ultra");
                await Build(composers.First(r => r.name == "Avatar_Wagner").ToRuntime(), anchor.transform, "Wagner_ClassicGloves_" + quality, quality == "Ultra");
                if (quality == "Ultra") await Build(composers.First(r => r.name == "Avatar_Beethoven").ToRuntime(), anchor.transform, "Beethoven_Hair_" + quality, true);
                if (quality == "Ultra") await Build(composers.First(r => r.name == "Avatar_Mozart").ToRuntime(), anchor.transform, "Mozart_Matte_" + quality, true);
                if (quality == "Ultra") await Build(composers.First(r => r.name == "Avatar_Vivaldi").ToRuntime(), anchor.transform, "Vivaldi_Matte_" + quality, true);
                File.WriteAllText(Folder + "/validation.txt", Report.ToString());
            }
            UnityEngine.Object.Destroy(anchor);
        }
        catch (Exception e) { Check("Exception " + e, false); }
        finally { QualitySettings.SetQualityLevel(originalQuality, false); }
        File.WriteAllText(Folder + "/validation.txt", Report.ToString());
        Debug.Log(Report.ToString());
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static async Task Build(AvatarRecipe recipe, Transform anchor, string label, bool capture)
    {
        var instance = await AvatarFactory.CreateAsync(recipe, anchor);
        try
        {
            Check(label + " built", instance?.Root != null);
            if (instance?.Root == null) return;
            Check(label + " all equipment", instance.EquippedItems.Count == recipe.Items.Count);
            Check(label + " segmented equipment", instance.Appearance.SegmentedGarmentCount == recipe.Items.Count);
            Check(label + " valid masks", instance.Root.GetComponentsInChildren<SegmentedGarmentPart>(true).All(p => p.TryValidate(out _)));
            Check(label + " materials and bones", instance.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .All(r => r.sharedMesh != null && r.sharedMaterials.All(m => m != null) && r.bones.All(b => b != null)));
            if (capture) Capture(instance, label);
        }
        finally { instance?.Dispose(); }
        var unload = Resources.UnloadUnusedAssets();
        while (!unload.isDone) await Task.Yield();
        GC.Collect();
    }

    static void Capture(AvatarInstance instance, string label)
    {
        var go = new GameObject("DeliveryCamera");
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 34;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.22f, .24f, .27f);
        var focus = instance.Root.position + Vector3.up * .95f;
        camera.transform.position = focus + new Vector3(0, .1f, 4.2f);
        camera.transform.LookAt(focus);
        var rt = new RenderTexture(700, 900, 24);
        var image = new Texture2D(700, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        image.ReadPixels(new Rect(0, 0, 700, 900), 0, 0);
        image.Apply();
        File.WriteAllBytes(Folder + "/" + label + ".png", image.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(image);
        UnityEngine.Object.Destroy(go);
    }
}
