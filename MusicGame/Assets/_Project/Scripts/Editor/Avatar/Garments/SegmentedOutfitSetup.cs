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
using Newtonsoft.Json.Linq;

/// <summary>
/// Imports every garment of the 3D pipeline that has occlusion masks (Output/&lt;Piece&gt;/Rigged/&lt;Q&gt;/*.fbx, five
/// qualities, + Rigged/Masks from 08_GENERAR_MASCARES.bat) and dresses the avatars:
///   &lt;Composer&gt;_&lt;kind&gt;  → Art/Opponents/&lt;Composer&gt;/&lt;Piece&gt;/&lt;Q&gt;, Composers/Items/Wearable_&lt;Piece&gt; (Enemy_&lt;id&gt;,
///                         Remote_Enemies_&lt;Q&gt;) added to Avatar_&lt;Composer&gt; (the recipe of the composer's levels);
///                         hair → Hair_&lt;Piece&gt; (HairItemSO) on Identity_&lt;Composer&gt; (plain prefab: hides nothing).
///   &lt;Style&gt;_&lt;kind&gt;     → Art/Player/&lt;Style&gt;/&lt;Piece&gt;/&lt;Q&gt;, Player/Items/Wearable_&lt;Piece&gt; (PlayerClothing_&lt;id&gt;,
///                         Remote_PlayerClothing_&lt;Q&gt;) in Player/Recipes/Avatar_Player_&lt;Style&gt; (the player recipe +
///                         the style's garments), listed in PlayerAvatarConfig.styleRecipes (Runner + Fight).
/// Per quality: the RIGGED FBX as it is (GarmentMaskImporter: + per-vertex anchors → _occlusion.bytes, prefab with
/// SegmentedGarmentPart v4), its textures (Metallic + Roughness → URP MetallicSmoothness) and a URP Lit material like
/// the other garments — an authored material already on the prefab (Monteverdi, Wagner) is kept. An existing model
/// file in the quality folder is overwritten in place, so prefab / Addressables references survive.
/// Layering comes from the garment kind (Layering below): which layer it sits on and which categories it hides.
/// Garment kinds are normalised (Kind): tshirt / top → shirt, cardigan / overshirt (worn open over a shirt) → jacket,
/// &lt;trousers&gt;_belt → trousers; anything else (necklace, beard…) is an accessory that hides nothing.
/// Idempotent. Menu: Tools > MusicGame > Avatars > Garments > Set Up All Segmented Outfits (or … New Segmented Outfits:
/// pieces whose five prefabs already carry masks at least as new as the pipeline's are kept, only dressed).
/// Batch: -executeMethod SegmentedOutfitSetup.RunFromCommandLine [-onlyNew [-reimport Piece1,Piece2]] [-validateOutfit]
/// (Logs/SegmentedOutfitSetup.txt); -reimport re-imports those pieces even with -onlyNew.
/// </summary>
public static class SegmentedOutfitSetup
{
    private const string Content = "Assets/_Project/Avatar/MakeHuman/Content";
    private const string ComposerContent = Content + "/Composers";
    private const string PlayerContent = Content + "/Player";
    private const string Configs = Content + "/Garments";
    private const string PlayerConfigPath = "Assets/_Project/Configs/Player/PlayerAvatarConfig.asset";
    private const string MaterialTemplate = "Assets/_Project/Art/Opponents/Wagner/Wagner_shirt/Low/Materials/M_Wagner_shirt_Low.mat";
    /// <summary>Pipeline folder → name in the game (assets, items) when they differ.</summary>
    private static readonly Dictionary<string, string> GameNames = new(StringComparer.OrdinalIgnoreCase) { ["monteverdi_pants_4"] = "Monteverdi_pants" };

    private static readonly (string dir, string quality, int maxSize)[] Qualities =
    {
        ("LOW", "Low", 1024), ("MEDIUM", "Mid", 2048), ("MID-HIGH", "HighMid", 2048), ("HIGH", "High", 2048), ("ULTRA", "Ultra", 4096),
    };

    private static string OutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../AssetsMaterial/3D_pipeline/Output"));

    private sealed class Piece
    {
        public string Name, Folder, Owner, Id, Kind;
        public bool Composer;
        public GameMusicStyle Style;
        public string Art => (Composer ? "Assets/_Project/Art/Opponents/" : "Assets/_Project/Art/Player/") + Owner + "/" + Name;
        public string ContentGroup => Composer ? ContentKeys.Enemy(Owner.ToLowerInvariant()) : ContentKeys.PlayerClothing(Id);
        public WearableItemSO Item;
        public HairItemSO Hair;
    }

    [MenuItem("Tools/MusicGame/Avatars/Garments/Set Up All Segmented Outfits")]
    public static void Menu() => Debug.Log(Run());

    [MenuItem("Tools/MusicGame/Avatars/Garments/Set Up New Segmented Outfits")]
    public static void MenuOnlyNew() => Debug.Log(Run(onlyNew: true));

    public static void RunFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        int r = Array.IndexOf(args, "-reimport");
        var reimport = r >= 0 && r + 1 < args.Length ? args[r + 1].Split(',', StringSplitOptions.RemoveEmptyEntries) : null;
        string report = Run(args.Contains("-onlyNew"), reimport);
        Debug.Log(report);
        bool failed = report.Contains("FAIL");
        if (!failed && Environment.GetCommandLineArgs().Contains("-validateOutfit"))
            EditorApplication.delayCall += SegmentedOutfitCheck.RunFromCommandLine;
        else if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
    }

    public static string Run(bool onlyNew = false, IEnumerable<string> reimport = null)
    {
        var log = new StringBuilder("[SegmentedOutfitSetup]\n");
        try
        {
            var reference = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(ComposerContent + "/Recipes/Avatar_Wagner.asset");
            var baseAvatar = reference != null ? reference.baseAvatar : null;
            if (baseAvatar == null) throw new InvalidOperationException("No base avatar (Avatar_Wagner recipe).");
            var template = AssetDatabase.LoadAssetAtPath<Material>(MaterialTemplate) ?? throw new InvalidOperationException("Missing " + MaterialTemplate);
            var settings = AddressableAssetSettingsDefaultObject.Settings ?? throw new InvalidOperationException("Missing Addressables settings");

            var pieces = Discover(log);
            var bodyRenderer = BodyRenderer(baseAvatar);
            var body = GarmentMaskImporter.Body.From(bodyRenderer);
            log.AppendLine($"  body  {bodyRenderer.sharedMesh.name}: {body.Positions.Length} vertices, {body.Triangles.Length / 3} triangles");
            foreach (var piece in pieces)
            {
                bool forced = reimport != null && (reimport.Contains(piece.Name, StringComparer.OrdinalIgnoreCase) || reimport.Contains(piece.Folder, StringComparer.OrdinalIgnoreCase));
                if (onlyNew && !forced && LoadExisting(piece)) { log.AppendLine($"  keep  {piece.Name}: already in the game with current masks"); continue; }
                try
                {
                    Import(piece, baseAvatar, body, template, settings, log);
                    // Texture sources down to their per-quality import size (4K originals stay in Output).
                    log.Append(TextureSourceShrinker.Run(piece.Art).Replace("[TextureSourceShrinker]\n", ""));
                }
                catch (Exception e) { log.AppendLine($"  FAIL  {piece.Name}: {e.Message}\n{e.StackTrace}"); }
                // Each piece loads five models + textures: free them before the next one (a full run otherwise
                // exhausts memory), and keep the report current in case the editor dies.
                AssetDatabase.SaveAssets();
                EditorUtility.UnloadUnusedAssetsImmediate(true);
                GC.Collect();
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/SegmentedOutfitSetup.txt", log.ToString());
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
            string folder = Path.GetFileName(dir);
            if (!File.Exists(Path.Combine(dir, "Rigged/Masks/manifest.json")))
            {
                if (Directory.Exists(Path.Combine(dir, "Rigged"))) log.AppendLine($"  skip  {folder}: no Rigged/Masks (run 08_GENERAR_MASCARES.bat)");
                continue;
            }
            string name = GameNames.TryGetValue(folder, out var renamed) ? renamed : folder;
            int cut = name.IndexOf('_');
            if (cut <= 0) { log.AppendLine($"  skip  {folder}: no owner prefix"); continue; }
            string owner = name.Substring(0, cut);
            var piece = new Piece { Name = name, Folder = folder, Owner = owner, Id = name.ToLowerInvariant(), Kind = Kind(name) };
            if (File.Exists(ComposerContent + "/Recipes/Avatar_" + owner + ".asset")) piece.Composer = true;
            else if (Enum.TryParse(owner, true, out GameMusicStyle style) && style != GameMusicStyle.Unknown) piece.Style = style;
            else { log.AppendLine($"  skip  {folder}: '{owner}' is neither a composer nor a music style"); continue; }
            pieces.Add(piece);
        }
        log.AppendLine($"  found {pieces.Count}: {string.Join(", ", pieces.Select(p => p.Name))}");
        return pieces;
    }

    private static SkinnedMeshRenderer BodyRenderer(BaseAvatarDefinitionSO baseAvatar)
    {
        var prefab = baseAvatar.baseAvatarPrefab.editorAsset;
        var part = prefab != null ? prefab.GetComponentInChildren<AvatarVisualPart>(true) : null;
        return AvatarClothingBinder.MainBody(part != null ? part.skinnedRenderers : null)
               ?? throw new InvalidOperationException("The base avatar prefab has no body renderer.");
    }

    /// <summary>The garment kind from the piece name's last word, normalised to the kinds the rules below know.</summary>
    private static string Kind(string name)
    {
        string[] words = name.ToLowerInvariant().Split('_');
        string kind = words[^1];
        if (kind == "belt" && words.Length > 2 && words[^2] is "trousers" or "pants") kind = words[^2];   // trousers with their belt
        return kind switch
        {
            "tshirt" or "top" => "shirt",
            "cardigan" or "overshirt" => "jacket",
            _ => kind,
        };
    }

    /// <summary>A piece already in the game with current masks: its item (or hair) exists and every quality has its
    /// prefab and an occlusion file at least as new as the pipeline's mask.</summary>
    private static bool LoadExisting(Piece piece)
    {
        string folder = piece.Composer ? ComposerContent + "/Items" : PlayerContent + "/Items";
        var manifest = MaskManifest(piece);
        bool AllQualities() => Qualities.All(q =>
        {
            string prefab = $"{piece.Art}/{q.quality}/{piece.Name}_{q.quality}.prefab";
            string bytes = $"{piece.Art}/{q.quality}/{piece.Name}_{q.quality}_occlusion.bytes";
            string gmask = GmaskPath(piece, manifest, q.dir);
            return File.Exists(prefab) && File.Exists(bytes) && gmask != null && File.GetLastWriteTimeUtc(bytes) >= File.GetLastWriteTimeUtc(gmask);
        });
        if (piece.Kind == "hair")
        {
            piece.Hair = AssetDatabase.LoadAssetAtPath<HairItemSO>($"{ComposerContent}/Items/Hair_{piece.Name}.asset");
            return piece.Hair != null && AllQualities();
        }
        piece.Item = AssetDatabase.LoadAssetAtPath<WearableItemSO>($"{folder}/Wearable_{piece.Name}.asset");
        return piece.Item != null && piece.Item.segmentedGarment != null && AllQualities();
    }

    private static JObject MaskManifest(Piece piece) =>
        JObject.Parse(File.ReadAllText(Path.Combine(OutputRoot, piece.Folder, "Rigged/Masks/manifest.json")));

    private static string GmaskPath(Piece piece, JObject manifest, string qualityDir)
    {
        var asset = manifest["assets"]?.FirstOrDefault(a => (string)a["quality"] == qualityDir);
        if (asset == null) return null;
        string path = Path.Combine(OutputRoot, piece.Folder, "Rigged/Masks", (string)asset["file"]);
        return File.Exists(path) ? path : null;
    }

    // ── One piece, five qualities ────────────────────────────────────────────────

    private static void Import(Piece piece, BaseAvatarDefinitionSO baseAvatar, GarmentMaskImporter.Body body, Material template,
                               AddressableAssetSettings settings, StringBuilder log)
    {
        string source = Path.Combine(OutputRoot, piece.Folder, "Rigged");
        var manifest = MaskManifest(piece);
        string groupFormat = piece.Composer ? "Remote_Enemies_{0}" : "Remote_PlayerClothing_{0}";
        if (!piece.Composer) foreach (var q in Qualities) EnsureGroup(settings, string.Format(groupFormat, q.quality), "Remote_Enemies_" + q.quality);

        bool hair = piece.Kind == "hair";
        if (!hair) piece.Item = PrepareItem(piece);
        var prefabs = new List<(string quality, GameObject prefab)>();
        var summaries = new List<string>();
        foreach (var q in Qualities)
        {
            string riggedDir = Path.Combine(source, q.dir);
            var fbx = Directory.GetFiles(riggedDir, "*.fbx");
            if (fbx.Length != 1) throw new InvalidOperationException($"{riggedDir}: expected one rigged FBX, found {fbx.Length}");
            string gmask = GmaskPath(piece, manifest, q.dir) ?? throw new InvalidOperationException($"{piece.Folder}: no {q.dir} mask in Rigged/Masks");

            string folder = $"{piece.Art}/{q.quality}";
            EnsureFolder(folder + "/Textures");
            EnsureFolder(folder + "/Materials");
            string prefix = $"{piece.Name}_{q.quality}";
            string prefabPath = $"{folder}/{prefix}.prefab";
            // An existing model file is overwritten in place (its GUID — and every reference — survives).
            var models = Directory.GetFiles(folder, "*.fbx");
            string modelPath = models.Length == 1 ? models[0].Replace('\\', '/') : $"{folder}/{prefix}.fbx";
            if (models.Length > 1) throw new InvalidOperationException($"{folder}: {models.Length} FBX files — keep only one");

            // The object's shared texture set of this quality (texture_share.py); the old per-model copy as fallback.
            string textures = Path.Combine(source, "..", "Textures", q.dir);
            if (!Directory.Exists(textures)) textures = Path.Combine(riggedDir, "Textures");
            var material = AuthoredMaterial(prefabPath, $"{folder}/Materials/M_{prefix}.mat")
                           ?? RicherMaterial($"{folder}/Materials/M_{prefix}.mat", textures)
                           ?? BuildMaterial(textures, folder, prefix, q.maxSize, template);
            // Trouser legs are never hidden by upper garments (the gmask's keep map, from step 08).
            var result = GarmentMaskImporter.ImportQuality(fbx[0], gmask, modelPath, prefabPath, material, body, plainPrefab: hair, log);
            prefabs.Add((q.quality, result.Prefab));
            summaries.Add($"{q.quality}: {result.Summary}");
        }

        string category = ContentKeys.Category(piece.Composer ? ContentCategory.Enemies : ContentCategory.PlayerClothing);
        if (hair)
        {
            piece.Hair = WriteHair(piece, prefabs, settings, category);
            log.AppendLine($"  PASS  {piece.Name}: hair, 5 qualities (plain prefabs)\n        " + string.Join("\n        ", summaries));
            return;
        }
        var config = WriteConfig(piece, (string)manifest["kind"]);
        piece.Item.segmentedGarment = config;
        piece.Item.sharedVariant ??= new AvatarItemVariant();
        piece.Item.sharedVariant.qualityPrefabs ??= new QualityAssetCollection();
        piece.Item.sharedVariant.qualityPrefabs.variants.Clear();
        foreach (var (quality, prefab) in prefabs)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            piece.Item.sharedVariant.qualityPrefabs.variants.Add(new QualityAssetReference { qualityName = quality, prefab = new AssetReferenceGameObject(guid) });
            Register(settings, guid, string.Format(groupFormat, quality), $"{piece.Item.ContentGroupOrDefault}/Wearable_{piece.Item.stableId}/sharedVariant/qualityPrefabs@{quality}",
                     category, piece.Item.ContentGroupOrDefault, quality);
        }
        EditorUtility.SetDirty(piece.Item);
        log.AppendLine($"  PASS  {piece.Name}: '{config.category}' layer {config.layer}" +
                       (config.hidesCategories.Count > 0 ? $", hides {string.Join("/", config.hidesCategories)} under it" : "") +
                       "\n        " + string.Join("\n        ", summaries));
    }

    /// <summary>The existing generated material when it has maps the pipeline textures cannot provide (an authored
    /// normal / metallic map, e.g. Monteverdi and Wagner): rebuilding it would lose them.</summary>
    private static Material RicherMaterial(string path, string textures)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) return null;
        // Its base map is not the generated copy (<folder>/Textures/<prefix>_BaseColor.png): set up by hand, keep it.
        var baseMap = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
        string generated = path.Replace("/Materials/M_", "/Textures/").Replace(".mat", "_BaseColor.png");
        if (baseMap != null && AssetDatabase.GetAssetPath(baseMap) != generated) return material;
        bool normal = File.Exists(Path.Combine(textures, "Normal.png"));
        bool packed = File.Exists(Path.Combine(textures, "Metallic.png")) && File.Exists(Path.Combine(textures, "Roughness.png"));
        bool richer = (material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") != null && !normal) ||
                      (material.HasProperty("_MetallicGlossMap") && material.GetTexture("_MetallicGlossMap") != null && !packed);
        return richer ? material : null;
    }

    /// <summary>The material already on the prefab when it was authored by hand (not the generated M_&lt;prefix&gt;).</summary>
    private static Material AuthoredMaterial(string prefabPath, string generatedPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return null;
        var material = prefab.GetComponentInChildren<SegmentedGarmentPart>(true)?.finalMaterial
                       ?? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true)?.sharedMaterial;
        if (material == null) return null;
        string path = AssetDatabase.GetAssetPath(material);
        return string.IsNullOrEmpty(path) || path == generatedPath || path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ? null : material;
    }

    /// <summary>An Addressables entry for `guid`: an existing one is kept as it is (address, group, labels).</summary>
    private static void Register(AddressableAssetSettings settings, string guid, string groupName, string address, params string[] labels)
    {
        if (settings.FindAssetEntry(guid) != null) return;
        var group = settings.FindGroup(groupName) ?? throw new InvalidOperationException("Missing Addressables group " + groupName);
        var entry = settings.CreateOrMoveEntry(guid, group);
        entry.address = address;
        for (int i = 0; i < labels.Length; i++)
        {
            string label = i == labels.Length - 1 ? "Q_" + labels[i] : labels[i];
            settings.AddLabel(label);
            entry.SetLabel(label, true);
        }
    }

    /// <summary>Garment_&lt;id&gt;: category, layer and the categories it hides, by kind. The zone coverage of the cut
    /// variants is cleared (masks replace it).</summary>
    private static SegmentedGarmentSO WriteConfig(Piece piece, string maskKind)
    {
        string path = $"{Configs}/Garment_{piece.Id}.asset";
        var config = AssetDatabase.LoadAssetAtPath<SegmentedGarmentSO>(path);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<SegmentedGarmentSO>();
            config.garmentId = piece.Id;
            AssetDatabase.CreateAsset(config, path);
        }
        (config.category, config.layer, config.hidesCategories) = Layering(piece.Kind);
        config.lastImportedPackage = Path.Combine(OutputRoot, piece.Folder, "Rigged/Masks") + $" ({maskKind})";
        EditorUtility.SetDirty(config);
        return config;
    }

    /// <summary>Layer (higher = more outer) and the lower categories a garment hides where it lies over them. Same
    /// layer never hides: shirt and trousers are both 20 (tucked or not), shoes hide nothing (trousers fall over them).
    /// Jackets / coats do not occlude below the hips (masked out by the pipeline), so loose tails never open holes.</summary>
    private static (string category, int layer, List<string> hides) Layering(string kind) => kind switch
    {
        "shirt" => ("shirt", 20, new List<string>()),
        "jacket" or "coat" => ("jacket", 40, new List<string> { "shirt", "top", "waistcoat", "trousers" }),
        "waistcoat" => ("waistcoat", 30, new List<string> { "shirt", "top" }),
        "pants" or "trousers" => ("trousers", 20, new List<string>()),
        "shoes" or "boots" or "sneakers" => ("shoes", 30, new List<string>()),
        _ => (kind, 50, new List<string>()),
    };

    /// <summary>The item, created with this kind's slots / legacy layer / content group (hand edits survive re-runs).</summary>
    private static WearableItemSO PrepareItem(Piece piece)
    {
        string folder = piece.Composer ? ComposerContent + "/Items" : PlayerContent + "/Items";
        string path = $"{folder}/Wearable_{piece.Name}.asset";
        var item = AssetDatabase.LoadAssetAtPath<WearableItemSO>(path);
        if (item != null) return item;
        EnsureFolder(folder);
        item = ScriptableObject.CreateInstance<WearableItemSO>();
        item.stableId = piece.Id;
        item.displayName = piece.Name.Replace('_', ' ');
        item.contentGroup = piece.Composer ? piece.ContentGroup : "";
        item.variantMode = AvatarItemVariantMode.Shared;
        (item.occupiedSlots, item.layer) = piece.Kind switch
        {
            "jacket" or "coat" => (AvatarSlot.UpperBody, GarmentLayer.Outerwear),
            "waistcoat" => (AvatarSlot.None, GarmentLayer.Outerwear),  // worn between the shirt and a jacket / coat
            "shirt" => (AvatarSlot.Accessory, GarmentLayer.Shirt),   // under an UpperBody jacket, like Wagner's shirt
            "pants" or "trousers" => (AvatarSlot.LowerBody, GarmentLayer.Pants),
            "shoes" or "boots" or "sneakers" => (AvatarSlot.Feet, GarmentLayer.Outerwear),
            _ => (AvatarSlot.None, GarmentLayer.Accessory),          // beard
        };
        AssetDatabase.CreateAsset(item, path);
        return item;
    }

    private static HairItemSO WriteHair(Piece piece, List<(string quality, GameObject prefab)> prefabs, AddressableAssetSettings settings, string category)
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
        foreach (var (quality, prefab) in prefabs)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            item.sharedVariant.qualityPrefabs.variants.Add(new QualityAssetReference { qualityName = quality, prefab = new AssetReferenceGameObject(guid) });
            Register(settings, guid, "Remote_Enemies_" + quality, $"{item.ContentGroupOrDefault}/Hair_{piece.Name}/sharedVariant/qualityPrefabs@{quality}",
                     category, item.ContentGroupOrDefault, quality);
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

    private static void EnsureFolder(string path)
    {
        path = path.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
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
        EnsureFolder(PlayerContent + "/Recipes");
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
