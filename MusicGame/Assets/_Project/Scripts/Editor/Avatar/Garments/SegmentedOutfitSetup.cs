using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Imports every segmented garment of the 3D pipeline (Output/&lt;Piece&gt;/Rigged/Segmented, five qualities) that
/// is not Monteverdi's or Wagner's (MonteverdiCutsSetup / WagnerCutsSetup own those), and dresses the avatars:
///   &lt;Composer&gt;_&lt;kind&gt;  → Art/Opponents/&lt;Composer&gt;/&lt;Piece&gt;/&lt;Q&gt;, Composers/Items/Wearable_&lt;Piece&gt; (Enemy_&lt;id&gt;,
///                         Remote_Enemies_&lt;Q&gt;) added to Avatar_&lt;Composer&gt; (the recipe of the composer's levels);
///                         hair → Hair_&lt;Piece&gt; (HairItemSO) on Identity_&lt;Composer&gt;.
///   &lt;Style&gt;_&lt;kind&gt;     → Art/Player/&lt;Style&gt;/&lt;Piece&gt;/&lt;Q&gt;, Player/Items/Wearable_&lt;Piece&gt; (PlayerClothing_&lt;id&gt;,
///                         Remote_PlayerClothing_&lt;Q&gt;) in Player/Recipes/Avatar_Player_&lt;Style&gt; (the player recipe +
///                         the style's garments), listed in PlayerAvatarConfig.styleRecipes (Runner + Fight).
/// Per quality: the segmented FBX, its textures (Metallic + Roughness → URP MetallicSmoothness), a URP Lit material
/// like the other garments; GarmentCutsImporter builds the prefab, SegmentedGarmentSO and item. Coverage comes from
/// the garment kind and the body zones the garment actually reaches (Coverage below, Wagner's rules generalised).
/// Idempotent. Menu: Tools > MusicGame > Avatars > Garments > Set Up All Segmented Outfits.
/// Batch: -executeMethod SegmentedOutfitSetup.RunFromCommandLine [-validateOutfit]   (Logs/SegmentedOutfitSetup.txt)
/// </summary>
public static class SegmentedOutfitSetup
{
    private const string Content = "Assets/_Project/Avatar/MakeHuman/Content";
    private const string ComposerContent = Content + "/Composers";
    private const string PlayerContent = Content + "/Player";
    private const string Configs = Content + "/Garments";
    private const string PlayerConfigPath = "Assets/_Project/Configs/Player/PlayerAvatarConfig.asset";
    private const string MaterialTemplate = "Assets/_Project/Art/Opponents/Wagner/Wagner_shirt/Low/Materials/M_Wagner_shirt_Low.mat";
    private static readonly string[] OwnSetups = { "monteverdi", "wagner" };

    private static readonly (string dir, string quality, int maxSize)[] Qualities =
    {
        ("LOW", "Low", 1024), ("MEDIUM", "Mid", 2048), ("MID-HIGH", "HighMid", 2048), ("HIGH", "High", 2048), ("ULTRA", "Ultra", 4096),
    };

    private static string OutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../AssetsMaterial/3D_pipeline/Output"));

    private sealed class Piece
    {
        public string Name, Owner, Id, Kind;
        public bool Composer;
        public GameMusicStyle Style;
        public string Art => (Composer ? "Assets/_Project/Art/Opponents/" : "Assets/_Project/Art/Player/") + Owner + "/" + Name;
        public string ContentGroup => Composer ? ContentKeys.Enemy(Owner.ToLowerInvariant()) : ContentKeys.PlayerClothing(Id);
        public WearableItemSO Item;
        public HairItemSO Hair;
    }

    [MenuItem("Tools/MusicGame/Avatars/Garments/Set Up All Segmented Outfits")]
    public static void Menu() => Debug.Log(Run());

    public static void RunFromCommandLine()
    {
        string report = Run();
        Debug.Log(report);
        bool failed = report.Contains("FAIL");
        if (!failed && Environment.GetCommandLineArgs().Contains("-validateOutfit"))
            EditorApplication.delayCall += SegmentedOutfitCheck.RunFromCommandLine;
        else if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
    }

    public static string Run()
    {
        var log = new StringBuilder("[SegmentedOutfitSetup]\n");
        try
        {
            var reference = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(ComposerContent + "/Recipes/Avatar_Wagner.asset");
            var baseAvatar = reference != null ? reference.baseAvatar : null;
            if (baseAvatar?.garmentRegionCatalog == null || baseAvatar.garmentRegionCatalog.bodyMasks.Count == 0)
                throw new InvalidOperationException("The shared avatar body-mask catalog must be installed first.");
            var template = AssetDatabase.LoadAssetAtPath<Material>(MaterialTemplate) ?? throw new InvalidOperationException("Missing " + MaterialTemplate);
            var settings = AddressableAssetSettingsDefaultObject.Settings ?? throw new InvalidOperationException("Missing Addressables settings");

            var pieces = Discover(log);
            foreach (var piece in pieces)
            {
                try { Import(piece, baseAvatar, template, settings, log); }
                catch (Exception e) { log.AppendLine($"  FAIL  {piece.Name}: {e.Message}\n{e.StackTrace}"); }
            }
            DressComposers(pieces.Where(p => p.Composer).ToList(), log);
            DressPlayer(pieces.Where(p => !p.Composer).ToList(), log);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
        catch (Exception e) { log.AppendLine("  FAIL  " + e); }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/SegmentedOutfitSetup.txt", log.ToString());
        return log.ToString();
    }

    // ── Discovery ────────────────────────────────────────────────────────────────

    private static List<Piece> Discover(StringBuilder log)
    {
        var pieces = new List<Piece>();
        foreach (string dir in Directory.GetDirectories(OutputRoot).OrderBy(d => d))
        {
            string name = Path.GetFileName(dir);
            if (!File.Exists(Path.Combine(dir, "Rigged/Segmented/manifest.json"))) continue;
            int cut = name.IndexOf('_');
            if (cut <= 0) { log.AppendLine($"  skip  {name}: no owner prefix"); continue; }
            string owner = name.Substring(0, cut);
            if (OwnSetups.Contains(owner.ToLowerInvariant())) continue;
            var piece = new Piece { Name = name, Owner = owner, Id = name.ToLowerInvariant(), Kind = name.Substring(name.LastIndexOf('_') + 1).ToLowerInvariant() };
            if (File.Exists(ComposerContent + "/Recipes/Avatar_" + owner + ".asset")) piece.Composer = true;
            else if (Enum.TryParse(owner, true, out GameMusicStyle style) && style != GameMusicStyle.Unknown) piece.Style = style;
            else { log.AppendLine($"  skip  {name}: '{owner}' is neither a composer nor a music style"); continue; }
            pieces.Add(piece);
        }
        log.AppendLine($"  found {pieces.Count}: {string.Join(", ", pieces.Select(p => p.Name))}");
        return pieces;
    }

    // ── One piece, five qualities ────────────────────────────────────────────────

    private static void Import(Piece piece, BaseAvatarDefinitionSO baseAvatar, Material template, AddressableAssetSettings settings, StringBuilder log)
    {
        string source = Path.Combine(OutputRoot, piece.Name, "Rigged");
        var package = GarmentCutsPackage.Load(Path.Combine(source, "Segmented"));
        if (!package.IsUsable) throw new InvalidOperationException(string.Join("\n", package.Issues));
        string groupFormat = piece.Composer ? "Remote_Enemies_{0}" : "Remote_PlayerClothing_{0}";
        if (!piece.Composer) foreach (var q in Qualities) EnsureGroup(settings, string.Format(groupFormat, q.quality), "Remote_Enemies_" + q.quality);

        bool hair = piece.Kind == "hair";
        if (!hair) piece.Item = PrepareItem(piece);
        var options = new GarmentCutsImporter.Options
        {
            ContentRoot = Configs,
            BaseAvatarPath = AssetDatabase.GetAssetPath(baseAvatar),
            ImportMasks = false,
            AssignCatalogToBaseAvatar = false,
            RegisterAddressables = !hair,
            AddressablesGroupFormat = groupFormat,
        };
        if (hair) options.SkipWearableItems.Add(piece.Id);
        else options.ItemPaths[piece.Id] = AssetDatabase.GetAssetPath(piece.Item);

        foreach (var asset in package.Assets)
        {
            var q = Qualities.FirstOrDefault(x => x.quality == asset.Quality);
            if (q.dir == null) throw new InvalidOperationException($"Unknown quality '{asset.Quality}' ({asset.source}).");
            asset.GarmentId = piece.Id;
            asset.coverage = Coverage(piece, asset.objects.SelectMany(o => o.submeshes).Select(s => s.zone).ToHashSet());
            asset.coverageConfigured = true;
            asset.Issues.RemoveAll(i => i.Message.StartsWith("coverageConfigured is false", StringComparison.Ordinal));

            string folder = $"{piece.Art}/{asset.Quality}";
            GarmentCutsImporter.EnsureFolder(folder + "/Textures");
            GarmentCutsImporter.EnsureFolder(folder + "/Materials");
            string prefix = $"{piece.Name}_{asset.Quality}";
            options.ModelPaths[asset.Key] = $"{folder}/{prefix}.fbx";
            options.PrefabPaths[asset.Key] = $"{folder}/{prefix}.prefab";
            options.FinalMaterials[asset.Key] = BuildMaterial(Path.Combine(source, q.dir, "Textures"), folder, prefix, q.maxSize, template);
        }
        var result = GarmentCutsImporter.Import(package, options);
        foreach (string line in result.Log.Where(l => l.Contains("ERROR") || l.Contains("WARNING") || l.StartsWith("RESULT"))) log.AppendLine("    " + line.Trim());
        if (!result.Success) throw new InvalidOperationException($"{result.Errors} import error(s)");

        string category = ContentKeys.Category(piece.Composer ? ContentCategory.Enemies : ContentCategory.PlayerClothing);
        if (hair) piece.Hair = WriteHair(piece, result, settings, category);
        else
            foreach (var v in piece.Item.sharedVariant.qualityPrefabs.variants)
                settings.FindAssetEntry(v.prefab.AssetGUID)?.SetLabel(category, true, true, false);

        var config = result.Garments[piece.Id];
        log.AppendLine($"  PASS  {piece.Name}: {result.Prefabs.Count} qualities, '{config.category}' layer {config.layer}, " +
                       $"hides skin [{string.Join(" ", config.bodyCoverage)}]" +
                       (config.garmentCoverage.Count > 0 ? $", over {string.Join("; ", config.garmentCoverage.Select(r => string.Join("/", r.targetCategories) + ": " + r.zones.Count + " zones"))}" : ""));
    }

    /// <summary>The item, created with this kind's slots / legacy layer / content group (hand edits survive re-runs).</summary>
    private static WearableItemSO PrepareItem(Piece piece)
    {
        string folder = piece.Composer ? ComposerContent + "/Items" : PlayerContent + "/Items";
        string path = $"{folder}/Wearable_{piece.Name}.asset";
        var item = AssetDatabase.LoadAssetAtPath<WearableItemSO>(path);
        if (item != null) return item;
        GarmentCutsImporter.EnsureFolder(folder);
        item = ScriptableObject.CreateInstance<WearableItemSO>();
        item.stableId = piece.Id;
        item.displayName = piece.Name.Replace('_', ' ');
        item.contentGroup = piece.Composer ? piece.ContentGroup : "";
        item.variantMode = AvatarItemVariantMode.Shared;
        (item.occupiedSlots, item.layer) = piece.Kind switch
        {
            "jacket" or "coat" or "waistcoat" => (AvatarSlot.UpperBody, GarmentLayer.Outerwear),
            "shirt" => (AvatarSlot.Accessory, GarmentLayer.Shirt),   // under an UpperBody jacket, like Wagner's shirt
            "pants" or "trousers" => (AvatarSlot.LowerBody, GarmentLayer.Pants),
            "shoes" or "boots" or "sneakers" => (AvatarSlot.Feet, GarmentLayer.Outerwear),
            _ => (AvatarSlot.None, GarmentLayer.Accessory),          // beard
        };
        AssetDatabase.CreateAsset(item, path);
        return item;
    }

    private static HairItemSO WriteHair(Piece piece, GarmentCutsImporter.Result result, AddressableAssetSettings settings, string category)
    {
        string path = $"{ComposerContent}/Items/Hair_{piece.Name}.asset";
        var item = AssetDatabase.LoadAssetAtPath<HairItemSO>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<HairItemSO>();
            item.stableId = piece.Id;
            item.displayName = piece.Name.Replace('_', ' ');
            item.contentGroup = piece.ContentGroup;
            item.variantMode = AvatarItemVariantMode.Shared;
            AssetDatabase.CreateAsset(item, path);
        }
        item.sharedVariant ??= new AvatarItemVariant();
        item.sharedVariant.qualityPrefabs ??= new QualityAssetCollection();
        item.sharedVariant.qualityPrefabs.variants.Clear();
        foreach (var q in Qualities)
        {
            if (!result.Prefabs.TryGetValue(piece.Id + "@" + q.quality, out var prefab)) continue;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            item.sharedVariant.qualityPrefabs.variants.Add(new QualityAssetReference { qualityName = q.quality, prefab = new AssetReferenceGameObject(guid) });
            var group = settings.FindGroup("Remote_Enemies_" + q.quality);
            if (group == null) continue;
            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = $"{item.ContentGroupOrDefault}/Hair_{piece.Name}/sharedVariant/qualityPrefabs@{q.quality}";
            foreach (string label in new[] { category, item.ContentGroupOrDefault, "Q_" + q.quality }) { settings.AddLabel(label); entry.SetLabel(label, true); }
        }
        EditorUtility.SetDirty(item);
        return item;
    }

    // ── Textures + material (URP Lit, like the other garments) ───────────────────

    private static Material BuildMaterial(string textures, string folder, string prefix, int maxSize, Material template)
    {
        string Tex(string map) => $"{folder}/Textures/{prefix}_{map}.png";
        Copy(Path.Combine(textures, "BaseColor.png"), Tex("BaseColor"));
        bool normal = File.Exists(Path.Combine(textures, "Normal.png"));
        if (normal) Copy(Path.Combine(textures, "Normal.png"), Tex("Normal"));
        string metal = Path.Combine(textures, "Metallic.png"), rough = Path.Combine(textures, "Roughness.png");
        bool packed = File.Exists(metal) && File.Exists(rough);
        if (packed && (!File.Exists(Tex("MetallicSmoothness")) || File.GetLastWriteTimeUtc(Tex("MetallicSmoothness")) < File.GetLastWriteTimeUtc(rough)))
        {
            var m = ReadPng(metal);
            var r = ReadPng(rough);
            if (m.width != r.width || m.height != r.height) throw new InvalidOperationException($"{prefix}: Metallic / Roughness sizes differ");
            var mp = m.GetPixels32();
            var rp = r.GetPixels32();
            for (int i = 0; i < mp.Length; i++) mp[i] = new Color32(mp[i].r, mp[i].r, mp[i].r, (byte)(255 - rp[i].r));
            m.SetPixels32(mp);
            m.Apply();
            File.WriteAllBytes(Tex("MetallicSmoothness"), m.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(m);
            UnityEngine.Object.DestroyImmediate(r);
            AssetDatabase.ImportAsset(Tex("MetallicSmoothness"), ImportAssetOptions.ForceSynchronousImport);
        }
        ConfigureTexture(Tex("BaseColor"), TextureImporterType.Default, true, maxSize);
        if (normal) ConfigureTexture(Tex("Normal"), TextureImporterType.NormalMap, false, maxSize);
        if (packed) ConfigureTexture(Tex("MetallicSmoothness"), TextureImporterType.Default, false, maxSize);

        string path = $"{folder}/Materials/M_{prefix}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(template) { name = "M_" + prefix };
            AssetDatabase.CreateAsset(material, path);
        }
        var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex("BaseColor"));
        material.SetTexture("_BaseMap", baseMap);
        material.SetTexture("_MainTex", baseMap);
        material.SetTexture("_BumpMap", normal ? AssetDatabase.LoadAssetAtPath<Texture2D>(Tex("Normal")) : null);
        material.SetTexture("_MetallicGlossMap", packed ? AssetDatabase.LoadAssetAtPath<Texture2D>(Tex("MetallicSmoothness")) : null);
        if (normal) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
        if (packed) material.EnableKeyword("_METALLICSPECGLOSSMAP"); else material.DisableKeyword("_METALLICSPECGLOSSMAP");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigureTexture(string path, TextureImporterType type, bool srgb, int maxSize)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null || (ti.textureType == type && ti.sRGBTexture == srgb && ti.maxTextureSize == maxSize)) return;
        ti.textureType = type;
        ti.sRGBTexture = srgb;
        ti.maxTextureSize = maxSize;
        ti.SaveAndReimport();
    }

    private static bool Copy(string source, string assetPath)
    {
        if (!File.Exists(source)) throw new FileNotFoundException(source);
        string full = Path.GetFullPath(assetPath);
        if (File.Exists(full) && new FileInfo(full).Length == new FileInfo(source).Length && File.GetLastWriteTimeUtc(full) >= File.GetLastWriteTimeUtc(source)) return false;
        File.Copy(source, full, true);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        return true;
    }

    private static Texture2D ReadPng(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!t.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("Cannot read " + path);
        return t;
    }

    // ── Coverage ─────────────────────────────────────────────────────────────────

    private static readonly string[][] Arm =
        { new[] { "shoulder" }, new[] { "upperarm_upper" }, new[] { "upperarm_lower" }, new[] { "forearm_upper" }, new[] { "forearm_lower" }, new[] { "wrist" }, new[] { "hand" } };
    private static readonly string[][] Leg =
    {
        new[] { "thigh_upper_front", "thigh_upper_back" }, new[] { "thigh_lower_front", "thigh_lower_back" }, new[] { "knee_front", "knee_back" },
        new[] { "shin_upper_front", "shin_upper_back" }, new[] { "shin_lower_front", "shin_lower_back" }, new[] { "ankle" }, new[] { "foot" },
    };

    /// <summary>A limb step's skin is hidden only when the garment also reaches the NEXT step (so the step is fully
    /// wrapped, not just touched by a hem), and never past `last`.</summary>
    private static IEnumerable<string> Limb(string[][] chain, HashSet<string> zones, string last)
    {
        foreach (string side in new[] { "_l", "_r" })
            for (int i = 0; i + 1 < chain.Length; i++)
            {
                if (!chain[i + 1].Any(s => zones.Contains(s + side))) break;
                foreach (string s in chain[i]) if (zones.Contains(s + side)) yield return s + side;
                if (chain[i].Contains(last)) break;
            }
    }

    /// <summary>Wagner's authored rules, generalised: shirts hide the torso and wrapped sleeves; jackets (open
    /// fronts) the back and sleeves, plus the shirt under them, and a long skirt hides the trouser seat;
    /// waistcoats the back; trousers the hips and wrapped legs down to the shins; shoes the feet (and ankles when
    /// they rise above them); hair and beards hide nothing.</summary>
    private static GarmentCutsPackage.CoverageEntry Coverage(Piece piece, HashSet<string> zones)
    {
        var c = new GarmentCutsPackage.CoverageEntry { id = piece.Id };
        List<string> Present(params string[] regions) => regions.Where(zones.Contains).ToList();
        switch (piece.Kind)
        {
            case "shirt":
                c.category = "shirt"; c.layer = 20;
                c.bodyCoverage = Limb(Arm, zones, "wrist").Concat(Present("chest_front_center", "chest_front_l", "chest_front_r",
                    "abdomen_front_center", "abdomen_front_l", "abdomen_front_r", "back_upper", "back_lower")).ToList();
                break;
            case "jacket": case "coat":
                c.category = "jacket"; c.layer = 40;
                c.bodyCoverage = Limb(Arm, zones, "forearm_lower").Concat(Present("back_upper", "back_lower")).ToList();
                c.garmentCoverage.Add(new GarmentCutsPackage.CoverageRuleEntry { targetCategories = new List<string> { "shirt", "top" }, zones = new List<string>(c.bodyCoverage) });
                if (zones.Any(z => z.StartsWith("thigh_")) && zones.Contains("buttocks"))
                    c.garmentCoverage.Add(new GarmentCutsPackage.CoverageRuleEntry { targetCategories = new List<string> { "trousers" }, zones = new List<string> { "buttocks" } });
                break;
            case "waistcoat":
                c.category = "waistcoat"; c.layer = 30;
                c.bodyCoverage = Present("back_upper", "back_lower");
                c.garmentCoverage.Add(new GarmentCutsPackage.CoverageRuleEntry { targetCategories = new List<string> { "shirt", "top" }, zones = new List<string>(c.bodyCoverage) });
                break;
            case "pants": case "trousers":
                c.category = "trousers"; c.layer = 20;
                c.bodyCoverage = Present("pelvis_front_center", "pelvis_front_l", "pelvis_front_r", "buttocks")
                    .Concat(Limb(Leg, zones, "shin_lower_back")).ToList();
                break;
            case "shoes": case "boots": case "sneakers":
                c.category = "shoes"; c.layer = 30;
                bool high = zones.Any(z => z.StartsWith("shin_lower"));
                c.bodyCoverage = Present("foot_l", "foot_r").Concat(high ? Present("ankle_l", "ankle_r") : new List<string>()).ToList();
                break;
            case "beard":
                c.category = "beard"; c.layer = 50;
                break;
            default:
                c.category = piece.Kind; c.layer = 50;
                break;
        }
        return c;
    }

    // ── Avatars ──────────────────────────────────────────────────────────────────

    private static void DressComposers(List<Piece> pieces, StringBuilder log)
    {
        foreach (var composer in pieces.GroupBy(p => p.Owner))
        {
            var recipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>($"{ComposerContent}/Recipes/Avatar_{composer.Key}.asset");
            var items = recipe.items.Where(i => i != null).ToList();
            foreach (var p in composer.Where(p => p.Item != null))
                if (!items.Contains(p.Item)) items.Add(p.Item);
            recipe.items = items.ToArray();
            EditorUtility.SetDirty(recipe);
            var hair = composer.FirstOrDefault(p => p.Hair != null)?.Hair;
            if (hair != null && recipe.identity != null)
            {
                recipe.identity.hair = hair;
                EditorUtility.SetDirty(recipe.identity);
            }
            log.AppendLine($"  PASS  {recipe.name}: {string.Join(", ", recipe.items.Select(i => i.stableId))}" +
                           (hair != null ? $" + hair {hair.stableId} on {recipe.identity?.name}" : ""));
        }
    }

    private static void DressPlayer(List<Piece> pieces, StringBuilder log)
    {
        var config = AssetDatabase.LoadAssetAtPath<PlayerAvatarConfigSO>(PlayerConfigPath);
        if (config?.avatarRecipe == null) { log.AppendLine("  FAIL  PlayerAvatarConfig has no player recipe"); return; }
        var styleRecipes = (config.styleRecipes ?? Array.Empty<PlayerAvatarConfigSO.StyleRecipe>()).Where(r => r != null).ToList();
        GarmentCutsImporter.EnsureFolder(PlayerContent + "/Recipes");
        foreach (var style in pieces.Where(p => p.Item != null).GroupBy(p => p.Style))
        {
            string path = $"{PlayerContent}/Recipes/Avatar_Player_{style.Key}.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(path);
            if (recipe == null)
            {
                recipe = UnityEngine.Object.Instantiate(config.avatarRecipe);
                AssetDatabase.CreateAsset(recipe, path);
            }
            recipe.name = Path.GetFileNameWithoutExtension(path);
            recipe.items = style.Select(p => p.Item).ToArray();
            EditorUtility.SetDirty(recipe);
            styleRecipes.RemoveAll(r => r.style == style.Key);
            styleRecipes.Add(new PlayerAvatarConfigSO.StyleRecipe { style = style.Key, recipe = recipe });
            log.AppendLine($"  PASS  {recipe.name} ({style.Key}): {string.Join(", ", recipe.items.Select(i => i.stableId))}");
        }
        config.styleRecipes = styleRecipes.OrderBy(r => (int)r.style).ToArray();
        EditorUtility.SetDirty(config);
    }

    /// <summary>A remote, pack-separately group like `like` (Remote_Enemies_&lt;Q&gt;), created once.</summary>
    private static void EnsureGroup(AddressableAssetSettings settings, string name, string like)
    {
        if (settings.FindGroup(name) != null) return;
        var source = settings.FindGroup(like) ?? throw new InvalidOperationException("Missing Addressables group " + like);
        settings.CreateGroup(name, false, false, true, source.Schemas);
    }
}
