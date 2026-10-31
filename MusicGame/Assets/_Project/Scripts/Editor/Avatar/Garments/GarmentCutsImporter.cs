using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Imports a clothing cuts package (GarmentCutsPackage) into the project:
///
///   1. Body masks → processed (orientation check, seam padding, see BodyMaskProcessor) into
///      &lt;ArtRoot&gt;/BodyMasks/&lt;region&gt;.png as linear single-channel R8, no mipmaps, Clamp, readable; and the
///      GarmentRegionCatalogSO (regions, categories, masks, body-compatibility record), assigned to the base
///      avatar when it has none.
///   2. Each SELECTED valid asset (one garment × one quality) → &lt;ArtRoot&gt;/&lt;id&gt;/&lt;Quality&gt;/&lt;id&gt;_&lt;Quality&gt;_cuts.fbx
///      (Read/Write on, blendshapes on, Generic/no avatar like the other garments, materials imported so the
///      transport names can be read), then a prefab next to it with AvatarVisualPart + SegmentedGarmentPart:
///      region per submesh resolved from the imported transport material names, every slot rendered with
///      the garment's single final material.
///   3. Per garment: SegmentedGarmentSO (Garment_&lt;id&gt;) with the package coverage, and a WearableItemSO
///      (Wearable_&lt;id&gt;) whose shared variant lists the imported quality prefabs.
///
/// Re-importing is safe: files are overwritten in place (GUIDs and references survive); an existing prefab
/// keeps its final material and extra components; coverage edited by hand is detected
/// (SegmentedGarmentSO.importedCoverageHash) and kept; item fields set by hand (name, slots, content group,
/// colours) are only written on creation. Nothing is imported for invalid assets.
///
/// Window: Tools > MusicGame > Avatars > Garments > Import Cuts Package. Batch: ImportFromCommandLine.
/// </summary>
public static class GarmentCutsImporter
{
    public const string DefaultArtRoot = "Assets/_Project/Art/Garments";
    public const string DefaultContentRoot = "Assets/_Project/Avatar/MakeHuman/Content/Garments";
    public const string DefaultBaseAvatarPath = "Assets/_Project/Avatar/MakeHuman/Content/BaseAvatar_MakeHuman.asset";
    public const string CanonicalFbxPath = "Assets/_Project/Avatar/MakeHuman/Generated/MakeHuman_Canonical.fbx";

    public sealed class Options
    {
        public string ArtRoot = DefaultArtRoot;
        public string ContentRoot = DefaultContentRoot;
        public string BaseAvatarPath = DefaultBaseAvatarPath;
        /// <summary>Write the catalog into the base avatar when it has none (an existing one is never replaced).</summary>
        public bool AssignCatalogToBaseAvatar = true;
        public bool ImportMasks = true;
        public int SeamPaddingTexels = 4;
        /// <summary>Asset keys (GarmentId@Quality) to import; null = every valid asset.</summary>
        public HashSet<string> Selected;
        /// <summary>Explicit final materials by key (GarmentId@Quality) or by GarmentId. Otherwise the prefab's
        /// current one is kept, else one is created from the imported transport material.</summary>
        public Dictionary<string, Material> FinalMaterials = new();
        // Optional destinations allow migration without changing existing asset GUIDs.
        public Dictionary<string, string> ModelPaths = new();
        public Dictionary<string, string> PrefabPaths = new();
        public Dictionary<string, string> ItemPaths = new();
        public HashSet<string> SkipWearableItems = new();
        public bool RegisterAddressables;
        /// <summary>{0} = quality name. Existing groups only.</summary>
        public string AddressablesGroupFormat = "Remote_Enemies_{0}";

        public string CatalogPath => ContentRoot + "/GarmentRegionCatalog.asset";
    }

    public sealed class Result
    {
        public readonly List<string> Log = new();
        public int Errors;
        public GarmentRegionCatalogSO Catalog;
        public readonly Dictionary<string, WearableItemSO> Items = new();
        public readonly Dictionary<string, SegmentedGarmentSO> Garments = new();
        public readonly Dictionary<string, GameObject> Prefabs = new(); // by asset key
        /// <summary>Per asset key: imported vertex count, manifest verticesAfter, boundary duplicate groups.</summary>
        public readonly Dictionary<string, (int unity, int manifest, int boundaryDuplicates, int exactDuplicates)> VertexChecks = new();
        public readonly Dictionary<string, (float onSkin, float flipped)> MaskChecks = new();
        public bool Success => Errors == 0;
        public void Info(string m) => Log.Add("  " + m);
        public void Warn(string m) => Log.Add("  WARNING " + m);
        public void Error(string m) { Errors++; Log.Add("  ERROR " + m); }
    }

    // ── Entry points ──────────────────────────────────────────────────────────────

    /// <summary>Batch: -executeMethod GarmentCutsImporter.ImportFromCommandLine -garmentPackage &lt;dir&gt;
    /// [-garmentSelect key1,key2] [-garmentAddressables] [-garmentArtRoot …] [-garmentContentRoot …].
    /// Report: Logs/GarmentCutsImport.txt.</summary>
    public static void ImportFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var options = new Options { RegisterAddressables = args.Contains("-garmentAddressables") };
        if (Arg("-garmentArtRoot") is { } art) options.ArtRoot = art;
        if (Arg("-garmentContentRoot") is { } content) options.ContentRoot = content;
        if (Arg("-garmentSelect") is { } select) options.Selected = new HashSet<string>(select.Split(','), StringComparer.OrdinalIgnoreCase);
        var package = GarmentCutsPackage.Load(Arg("-garmentPackage") ?? "");
        var result = Import(package, options);
        File.WriteAllLines("Logs/GarmentCutsImport.txt", result.Log);
        if (Application.isBatchMode) EditorApplication.Exit(result.Success ? 0 : 1);
    }

    public static Result Import(GarmentCutsPackage package, Options options)
    {
        var result = new Result();
        result.Log.Add($"Garment cuts import — {package.Root} ({DateTime.Now:yyyy-MM-dd HH:mm})");
        foreach (var issue in package.Issues) result.Log.Add("  package " + issue);
        if (!package.IsUsable) { result.Error("The package is not usable (see above) — nothing imported."); return result; }

        GarmentDiagnostics.ResetLogged();
        var baseAvatar = AssetDatabase.LoadAssetAtPath<BaseAvatarDefinitionSO>(options.BaseAvatarPath);
        var body = BodyMesh(baseAvatar, out var bodyBones);
        if (body == null) result.Warn($"No base avatar body at '{options.BaseAvatarPath}' — masks and bones cannot be checked against the body.");

        EnsureFolder(options.ContentRoot);
        result.Catalog = ImportCatalog(package, options, body, result);

        var selected = package.Assets.Where(a => options.Selected == null ? a.IsValid : options.Selected.Contains(a.Key)).ToList();
        foreach (var asset in package.Assets.Where(a => !selected.Contains(a)))
            result.Log.Add($"- {asset.Key} ({asset.source}): {(asset.IsValid ? "not selected" : "INVALID, skipped")}" +
                           string.Concat(asset.Issues.Where(i => i.Severity != GarmentCutsPackage.Severity.Info).Select(i => "\n    " + i)));

        foreach (var garment in selected.GroupBy(a => a.GarmentId).OrderBy(g => g.Key))
        {
            var imported = new List<(GarmentCutsPackage.AssetEntry asset, GameObject prefab)>();
            foreach (var asset in garment.OrderBy(a => QualityOrder(a.Quality)))
            {
                result.Log.Add($"- {asset.Key} ({asset.source})");
                foreach (var issue in asset.Issues) result.Log.Add("    " + issue);
                if (!asset.IsValid) { result.Error($"{asset.Key} has errors — not imported (it was explicitly selected)."); continue; }
                var prefab = ImportAsset(asset, options, bodyBones, result);
                if (prefab != null) { imported.Add((asset, prefab)); result.Prefabs[asset.Key] = prefab; }
            }
            if (imported.Count == 0) continue;
            var config = WriteGarmentConfig(garment.Key, imported.Select(i => i.asset).ToList(), package, options, result);
            if (!options.SkipWearableItems.Contains(garment.Key))
                WriteWearableItem(garment.Key, config, imported, options, result);
        }

        AssetDatabase.SaveAssets();
        result.Log.Add(result.Success ? "RESULT: imported without errors" : $"RESULT: {result.Errors} error(s)");
        return result;
    }

    // ── Catalog + masks ───────────────────────────────────────────────────────────

    private static GarmentRegionCatalogSO ImportCatalog(GarmentCutsPackage package, Options options, Mesh body, Result result)
    {
        var m = package.Data;
        var catalog = LoadOrCreate<GarmentRegionCatalogSO>(options.CatalogPath, out bool created);
        result.Log.Add($"- Catalog {options.CatalogPath} ({(created ? "created" : "updated")})");

        // Regions/categories only ever grow: ids added by hand (or by older packages) are kept.
        foreach (var region in m.regionIds) if (!catalog.regionIds.Contains(region)) catalog.regionIds.Add(region);
        if (!catalog.regionIds.Contains(GarmentRegionCatalogSO.UnassignedRegion)) catalog.regionIds.Insert(0, GarmentRegionCatalogSO.UnassignedRegion);
        foreach (var category in package.Assets.Select(a => a.coverage?.category).Where(c => !string.IsNullOrEmpty(c)))
            if (!catalog.categories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase))) catalog.categories.Add(category);

        catalog.bodyObject = m.bodyObject;
        catalog.bodyUv = m.bodyUv;
        catalog.avatarSha256 = m.avatarSha256;
        catalog.sourcePackage = package.Root;

        string unitySha = File.Exists(CanonicalFbxPath) ? Sha256(CanonicalFbxPath) : "";
        if (!string.IsNullOrEmpty(m.avatarSha256) && unitySha != m.avatarSha256)
            result.Warn($"avatarSha256 {Short(m.avatarSha256)} differs from the Unity body '{Path.GetFileName(CanonicalFbxPath)}' ({Short(unitySha)}) — compatibility is decided by the UV check below.");

        if (!options.ImportMasks) { EditorUtility.SetDirty(catalog); return catalog; }

        string maskFolder = options.ArtRoot + "/BodyMasks";
        EnsureFolder(maskFolder);
        int resolution = 0;
        bool[] skin = null;
        int[] owners = null;
        var written = new List<(string region, string path, int hidden)>();
        float worst = 1f;
        foreach (var mask in m.masks.Where(k => !string.IsNullOrEmpty(k.region) && m.regionIds.Contains(k.region)))
        {
            string source = package.MaskPath(mask);
            if (!File.Exists(source)) { result.Warn($"mask '{mask.region}': file missing — that skin stays visible."); continue; }
            byte[] values = BodyMaskProcessor.ReadRed(source, out int w, out int h);
            if (w != h || !Mathf.IsPowerOfTwo(w)) { result.Error($"mask '{mask.region}' is {w}x{h} — masks must be square power-of-two."); continue; }
            if (resolution == 0)
            {
                resolution = w;
                if (body != null)
                {
                    skin = BodyMaskProcessor.RasterizeUvIslands(body, resolution);
                    owners = BodyMaskProcessor.NearestSkinOwners(skin, resolution, Mathf.Max(0, options.SeamPaddingTexels));
                }
            }
            else if (w != resolution) { result.Error($"mask '{mask.region}' is {w}x{h}, the others are {resolution}x{resolution} — skipped."); continue; }

            if (skin != null)
            {
                var (asIs, flipped, hidden) = BodyMaskProcessor.HiddenOnSkin(values, skin, resolution);
                result.MaskChecks[mask.region] = (asIs, flipped);
                if (hidden > 0 && asIs < 0.9f && flipped > asIs + 0.2f)
                {
                    result.Warn($"mask '{mask.region}' fits the body UV only when flipped vertically ({asIs:P0} vs {flipped:P0} of its white texels on skin) — flipped.");
                    BodyMaskProcessor.FlipVertically(values, resolution);
                    asIs = flipped;
                }
                if (hidden > 0 && asIs < 0.9f)
                {
                    result.Error($"mask '{mask.region}': only {asIs:P0} of its white texels lie on this body's skin UVs — it was made for another UV layout; skipped (skin stays visible).");
                    continue;
                }
                worst = Mathf.Min(worst, asIs);
                BodyMaskProcessor.Pad(values, owners, skin);
            }
            if (mask.hiddenPixels > 0)
            {
                int black = values.Count(v => v >= 128);
                if (skin == null && Math.Abs(black - mask.hiddenPixels) > mask.hiddenPixels * 0.01f)
                    result.Warn($"mask '{mask.region}': {black} white texels, manifest says {mask.hiddenPixels}.");
            }
            string path = $"{maskFolder}/{mask.region}.png";
            BodyMaskProcessor.WriteGrey(path, values, resolution, resolution);
            written.Add((mask.region, path, mask.hiddenPixels));
        }
        if (skin != null && written.Count > 0)
            result.Info($"{written.Count} masks at {resolution}², white texels on body skin ≥ {worst:P1} (UV orientation/compatibility OK), seam padding {options.SeamPaddingTexels} texels.");

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        catalog.bodyMasks.Clear();
        foreach (var (region, path, hidden) in written)
        {
            ConfigureMaskImporter(path, resolution);
            catalog.bodyMasks.Add(new GarmentRegionCatalogSO.RegionMask { region = region, mask = AssetDatabase.LoadAssetAtPath<Texture2D>(path), hiddenPixels = hidden });
        }
        catalog.maskResolution = resolution;
        catalog.seamPaddingTexels = skin != null ? options.SeamPaddingTexels : 0;
        EditorUtility.SetDirty(catalog);

        var baseAvatar = AssetDatabase.LoadAssetAtPath<BaseAvatarDefinitionSO>(options.BaseAvatarPath);
        if (baseAvatar != null && options.AssignCatalogToBaseAvatar && baseAvatar.garmentRegionCatalog == null)
        {
            baseAvatar.garmentRegionCatalog = catalog;
            EditorUtility.SetDirty(baseAvatar);
            result.Info($"catalog assigned to {Path.GetFileName(options.BaseAvatarPath)}.");
        }
        return catalog;
    }

    /// <summary>Linear single-channel R8, no mipmaps, Clamp, bilinear, uncompressed, CPU-readable (runtime composition).</summary>
    public static void ConfigureMaskImporter(string path, int resolution)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.textureType = TextureImporterType.SingleChannel;
        settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
        settings.sRGBTexture = false;
        settings.mipmapEnabled = false;
        settings.wrapMode = TextureWrapMode.Clamp;
        settings.filterMode = FilterMode.Bilinear;
        settings.aniso = 0;
        settings.readable = true;
        settings.npotScale = TextureImporterNPOTScale.None;
        settings.alphaSource = TextureImporterAlphaSource.None;
        importer.SetTextureSettings(settings);
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = Mathf.Max(resolution, 32);
        var platform = importer.GetDefaultPlatformTextureSettings();
        platform.format = TextureImporterFormat.R8;
        platform.textureCompression = TextureImporterCompression.Uncompressed;
        platform.maxTextureSize = importer.maxTextureSize;
        importer.SetPlatformTextureSettings(platform);
        importer.SaveAndReimport();
    }

    // ── Garment asset (one quality) ───────────────────────────────────────────────

    private static GameObject ImportAsset(GarmentCutsPackage.AssetEntry asset, Options options, HashSet<string> bodyBones, Result result)
    {
        string quality = string.IsNullOrEmpty(asset.Quality) ? "Default" : asset.Quality;
        string folder = options.ModelPaths.TryGetValue(asset.Key, out var existingModel)
            ? Path.GetDirectoryName(existingModel).Replace('\\', '/')
            : $"{options.ArtRoot}/{asset.GarmentId}/{quality}";
        EnsureFolder(folder);
        string modelPath = existingModel ?? $"{folder}/{asset.GarmentId}_{quality}_cuts.fbx";
        File.Copy(asset.FullPath, modelPath, overwrite: true);
        AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        ConfigureModelImporter(modelPath);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) { result.Error($"{asset.Key}: '{modelPath}' did not import as a model."); return null; }
        var modelRenderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        // Manifest object ↔ imported renderer, by name (object, then sourceObject), else the only pair.
        var pairs = new List<(GarmentCutsPackage.ObjectEntry obj, SkinnedMeshRenderer renderer)>();
        foreach (var obj in asset.objects)
        {
            var renderer = modelRenderers.FirstOrDefault(r => r.name == obj.objectName)
                           ?? modelRenderers.FirstOrDefault(r => TransportMaterialResolver.Normalize(r.name) == TransportMaterialResolver.Normalize(obj.objectName))
                           ?? modelRenderers.FirstOrDefault(r => r.name == obj.sourceObject)
                           ?? (asset.objects.Count == 1 && modelRenderers.Length == 1 ? modelRenderers[0] : null);
            if (renderer == null) { result.Error($"{asset.Key}: no imported renderer for manifest object '{obj.objectName}' (renderers: {string.Join(", ", modelRenderers.Select(r => r.name))})."); return null; }
            pairs.Add((obj, renderer));
        }
        foreach (var extra in modelRenderers.Where(r => pairs.All(p => p.renderer != r)))
        { result.Error($"{asset.Key}: imported renderer '{extra.name}' is not in the manifest."); return null; }

        var layouts = new List<(string path, SegmentedGarmentPart.RendererLayout layout, GarmentCutsPackage.ObjectEntry obj)>();
        Material transportSample = null;
        foreach (var (obj, renderer) in pairs)
        {
            var mesh = renderer.sharedMesh;
            string context = $"{asset.Key}/{renderer.name}";
            if (mesh == null) { result.Error($"{context}: no mesh."); return null; }
            if (!mesh.isReadable) { result.Error($"{context}: mesh is not readable after import."); return null; }
            var names = renderer.sharedMaterials.Select(mat => mat != null ? mat.name : "").ToList();
            if (names.Count != mesh.subMeshCount) { result.Error($"{context}: {names.Count} materials for {mesh.subMeshCount} submeshes."); return null; }
            transportSample ??= renderer.sharedMaterials.FirstOrDefault(mat => mat != null);

            var resolved = TransportMaterialResolver.Resolve(names, obj.submeshes, context);
            foreach (var e in resolved.Errors) result.Warn(e);
            foreach (var w in resolved.Warnings) result.Info(w);
            if (resolved.Resolved == 0) { result.Error($"{context}: no submesh could be mapped to a region."); return null; }
            result.Info($"{context}: {resolved.Resolved}/{mesh.subMeshCount} submeshes resolved to regions, {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles, {mesh.blendShapeCount} blendshapes.");

            if (obj.blendshapes > 0 && mesh.blendShapeCount < obj.blendshapes)
            { result.Error($"{context}: {mesh.blendShapeCount} blendshapes imported, manifest has {obj.blendshapes}."); return null; }
            var missingBones = bodyBones == null ? new List<string>() : renderer.bones.Where(b => b == null || !bodyBones.Contains(b.name)).Select(b => b != null ? b.name : "<null>").ToList();
            if (missingBones.Count > 0) { result.Error($"{context}: bones not on the body skeleton: {string.Join(", ", missingBones.Take(8))}."); return null; }

            var (exact, boundary) = CountDuplicateVertices(mesh);
            result.VertexChecks[asset.Key] = (mesh.vertexCount, obj.verticesAfter, boundary, exact);
            string dupMessage = $"{context}: vertices Unity {mesh.vertexCount} vs producer {obj.verticesAfter} (UV/normal seam splits), " +
                                $"{exact} exact-duplicate groups, {boundary} of them split only across region boundaries.";
            if (boundary > mesh.vertexCount / 200) result.Warn(dupMessage + " Unexpected duplication at region boundaries.");
            else result.Info(dupMessage);

            layouts.Add((AnimationUtility.CalculateTransformPath(renderer.transform, model.transform), new SegmentedGarmentPart.RendererLayout
            {
                cutsObject = obj.objectName,
                sourceObject = obj.sourceObject,
                submeshRegions = resolved.Regions,
                transportMaterials = names.ToArray(),
                vertexCount = mesh.vertexCount,
            }, obj));
        }

        string prefabPath = options.PrefabPaths.TryGetValue(asset.Key, out var existingPrefab) ? existingPrefab : $"{folder}/{asset.GarmentId}_{quality}.prefab";
        var final = ResolveFinalMaterial(asset, prefabPath, folder, quality, transportSample, options, result);
        if (final == null) return null;
        return SavePrefab(model, prefabPath, final, layouts.Select(l => (l.path, l.layout)).ToList(), asset, result);
    }

    private static void ConfigureModelImporter(string modelPath)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        bool dirty = false;
        void Set<T>(T current, T wanted, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(current, wanted)) { apply(wanted); dirty = true; } }
        // Same rig/normal conventions as the other garment imports (Art/Opponents/*), plus: Read/Write for the
        // runtime index assembly and imported materials so the transport slot names exist on the renderers.
        Set(importer.animationType, ModelImporterAnimationType.Generic, v => importer.animationType = v);
        Set(importer.avatarSetup, ModelImporterAvatarSetup.NoAvatar, v => importer.avatarSetup = v);
        Set(importer.importAnimation, false, v => importer.importAnimation = v);
        Set(importer.isReadable, true, v => importer.isReadable = v);
        Set(importer.importBlendShapes, true, v => importer.importBlendShapes = v);
        Set(importer.importBlendShapeNormals, ModelImporterNormals.Calculate, v => importer.importBlendShapeNormals = v);
        Set(importer.importNormals, ModelImporterNormals.Import, v => importer.importNormals = v);
        Set(importer.importTangents, ModelImporterTangents.CalculateMikk, v => importer.importTangents = v);
        Set(importer.weldVertices, true, v => importer.weldVertices = v);
        Set(importer.meshCompression, ModelImporterMeshCompression.Off, v => importer.meshCompression = v);
        Set(importer.indexFormat, ModelImporterIndexFormat.Auto, v => importer.indexFormat = v);
        Set(importer.materialImportMode, ModelImporterMaterialImportMode.ImportViaMaterialDescription, v => importer.materialImportMode = v);
        Set(importer.materialLocation, ModelImporterMaterialLocation.InPrefab, v => importer.materialLocation = v);
        Set(importer.importCameras, false, v => importer.importCameras = v);
        Set(importer.importLights, false, v => importer.importLights = v);
        Set(importer.useFileScale, true, v => importer.useFileScale = v);
        Set(importer.globalScale, 1f, v => importer.globalScale = v);
        if (dirty) importer.SaveAndReimport();
    }

    private static Material ResolveFinalMaterial(GarmentCutsPackage.AssetEntry asset, string prefabPath, string folder, string quality,
                                                 Material transportSample, Options options, Result result)
    {
        if (options.FinalMaterials.TryGetValue(asset.Key, out var explicitMaterial) || options.FinalMaterials.TryGetValue(asset.GarmentId, out explicitMaterial))
            if (explicitMaterial != null) return explicitMaterial;

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)?.GetComponentInChildren<SegmentedGarmentPart>(true);
        if (existing != null && existing.finalMaterial != null)
        {
            result.Info($"{asset.Key}: keeps its final material '{existing.finalMaterial.name}'.");
            return existing.finalMaterial;
        }

        if (transportSample == null) { result.Error($"{asset.Key}: no final material given and no imported material to derive one from."); return null; }
        EnsureFolder(folder + "/Materials");
        string path = $"{folder}/Materials/M_{asset.GarmentId}_{quality}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(transportSample) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
            result.Info($"{asset.Key}: final material created from transport material '{transportSample.name}' → {path} (replace it on the prefab if the garment has an authored material).");
        }
        return material;
    }

    /// <summary>Builds (or refreshes) the variant prefab. An existing prefab whose renderer paths still exist is
    /// refreshed in place — extra components and overrides added by hand survive; otherwise it is rebuilt.</summary>
    private static GameObject SavePrefab(GameObject model, string prefabPath, Material final,
                                         List<(string path, SegmentedGarmentPart.RendererLayout layout)> layouts,
                                         GarmentCutsPackage.AssetEntry asset, Result result)
    {
        GameObject root = null;
        bool refresh = false;
        if (File.Exists(prefabPath))
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);
            refresh = layouts.All(l => FindPath(root.transform, l.path)?.GetComponent<SkinnedMeshRenderer>() != null);
            if (!refresh) { PrefabUtility.UnloadPrefabContents(root); root = null; result.Warn($"{asset.Key}: existing prefab no longer matches the model hierarchy — rebuilt (manual changes to it are lost)."); }
        }
        if (root == null)
        {
            root = (GameObject)UnityEngine.Object.Instantiate(model);
            root.name = Path.GetFileNameWithoutExtension(prefabPath);
            foreach (var animator in root.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
        }

        try
        {
            var modelRenderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var renderers = new List<SkinnedMeshRenderer>();
            foreach (var (path, layout) in layouts)
            {
                var smr = FindPath(root.transform, path).GetComponent<SkinnedMeshRenderer>();
                var source = modelRenderers.First(r => AnimationUtility.CalculateTransformPath(r.transform, model.transform) == path);
                if (refresh)
                {
                    // Re-point at the re-imported mesh/bones in case the model's internals changed.
                    smr.sharedMesh = source.sharedMesh;
                    smr.bones = source.bones.Select(b => b != null ? FindPath(root.transform, AnimationUtility.CalculateTransformPath(b, model.transform)) : null).ToArray();
                    smr.rootBone = source.rootBone != null ? FindPath(root.transform, AnimationUtility.CalculateTransformPath(source.rootBone, model.transform)) : null;
                }
                smr.sharedMaterials = Enumerable.Repeat(final, smr.sharedMesh.subMeshCount).ToArray(); // transport materials are never rendered
                layout.renderer = smr;
                renderers.Add(smr);
            }

            var visual = root.GetComponent<AvatarVisualPart>() ?? root.AddComponent<AvatarVisualPart>();
            visual.skinnedRenderers = renderers.ToArray();
            visual.rootBone = renderers[0].rootBone;
            var part = root.GetComponent<SegmentedGarmentPart>() ?? root.AddComponent<SegmentedGarmentPart>();
            part.schemaVersion = SegmentedGarmentPart.SupportedSchemaVersion;
            part.finalMaterial = final;
            part.renderers = layouts.Select(l => l.layout).ToList();
            if (!part.TryValidate(out string reason)) { result.Error($"{asset.Key}: the built prefab does not validate: {reason}."); return null; }

            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool ok);
            if (!ok) { result.Error($"{asset.Key}: could not save '{prefabPath}'."); return null; }
            result.Info($"{asset.Key}: prefab {(refresh ? "refreshed" : "built")} → {prefabPath} (final material '{final.name}').");
            return saved;
        }
        finally
        {
            if (refresh) PrefabUtility.UnloadPrefabContents(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // ── Garment configuration + item ─────────────────────────────────────────────

    private static SegmentedGarmentSO WriteGarmentConfig(string garmentId, List<GarmentCutsPackage.AssetEntry> assets, GarmentCutsPackage package, Options options, Result result)
    {
        string path = $"{options.ContentRoot}/Garment_{garmentId}.asset";
        var config = LoadOrCreate<SegmentedGarmentSO>(path, out bool created);
        if (string.IsNullOrEmpty(config.garmentId)) config.garmentId = garmentId;

        var configured = assets.Where(a => a.coverageConfigured && a.coverage != null).ToList();
        var coverage = configured.FirstOrDefault()?.coverage;
        foreach (var other in configured.Skip(1))
            if (CoverageHash(other.coverage) != CoverageHash(coverage))
                result.Warn($"{garmentId}: '{other.source}' has a different coverage than '{configured[0].source}' — the first one is used (coverage belongs to the garment, not to a quality).");
        foreach (var missing in assets.Where(a => !a.coverageConfigured))
            if (coverage != null) result.Info($"{garmentId}: '{missing.source}' has no coverage file; the garment coverage from '{configured[0].source}' applies to it.");

        bool editedByHand = !created && !string.IsNullOrEmpty(config.importedCoverageHash) && config.ComputeCoverageHash() != config.importedCoverageHash;
        if (coverage == null)
        {
            if (created || !config.coverageConfigured) result.Warn($"{garmentId}: no configured coverage in the package — it hides nothing (configure {path}).");
        }
        else if (editedByHand)
        {
            result.Warn($"{garmentId}: coverage was edited by hand in {path} — kept (package coverage not applied).");
        }
        else
        {
            config.category = coverage.category ?? "";
            config.layer = coverage.layer ?? 0;
            config.bodyCoverage = new List<string>(coverage.bodyCoverage ?? new List<string>());
            config.garmentCoverage = (coverage.garmentCoverage ?? new List<GarmentCutsPackage.CoverageRuleEntry>())
                .Select(r => new GarmentCoverageRule { targetCategories = new List<string>(r.targetCategories ?? new List<string>()), zones = new List<string>(r.zones ?? new List<string>()) })
                .ToList();
            config.coverageConfigured = true;
            config.importedCoverageHash = config.ComputeCoverageHash();
        }
        foreach (var asset in assets) if (!config.importedSources.Contains(asset.source)) config.importedSources.Add(asset.source);
        config.lastImportedPackage = package.Root;
        EditorUtility.SetDirty(config);
        result.Garments[garmentId] = config;
        result.Info($"{garmentId}: config {(created ? "created" : "updated")} → {path} (category '{config.category}', layer {config.layer}, body {config.bodyCoverage.Count} regions, {config.garmentCoverage.Count} garment rules).");
        return config;
    }

    private static void WriteWearableItem(string garmentId, SegmentedGarmentSO config, List<(GarmentCutsPackage.AssetEntry asset, GameObject prefab)> imported, Options options, Result result)
    {
        string path = options.ItemPaths.TryGetValue(garmentId, out var existingItem) ? existingItem : $"{options.ContentRoot}/Wearable_{garmentId}.asset";
        var item = LoadOrCreate<WearableItemSO>(path, out bool created);
        if (created)
        {
            item.stableId = garmentId;
            item.displayName = garmentId;
            item.variantMode = AvatarItemVariantMode.Shared;
            item.occupiedSlots = DefaultSlots(config.category);
            item.layer = DefaultLegacyLayer(config.category);
        }
        item.segmentedGarment = config;
        item.sharedVariant ??= new AvatarItemVariant();
        item.sharedVariant.qualityPrefabs ??= new QualityAssetCollection();
        foreach (var (asset, prefab) in imported)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            if (string.IsNullOrEmpty(asset.Quality)) { item.sharedVariant.prefab = new AssetReferenceGameObject(guid); continue; }
            item.sharedVariant.qualityPrefabs.variants.RemoveAll(v => v.qualityName == asset.Quality);
            item.sharedVariant.qualityPrefabs.variants.Add(new QualityAssetReference { qualityName = asset.Quality, prefab = new AssetReferenceGameObject(guid) });
            if (options.RegisterAddressables) RegisterAddressable(guid, item, asset.Quality, options, result);
        }
        item.sharedVariant.qualityPrefabs.variants.Sort((a, b) => QualityOrder(a.qualityName).CompareTo(QualityOrder(b.qualityName)));
        EditorUtility.SetDirty(item);
        result.Items[garmentId] = item;
        result.Info($"{garmentId}: item {(created ? "created" : "updated")} → {path} (qualities {string.Join(", ", item.sharedVariant.qualityPrefabs.variants.Select(v => v.qualityName))}" +
                    (created ? $"; slots {item.occupiedSlots} by category — adjust if needed" : "") + ").");
    }

    private static void RegisterAddressable(string guid, WearableItemSO item, string quality, Options options, Result result)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings != null ? settings.FindGroup(string.Format(options.AddressablesGroupFormat, quality)) : null;
        if (group == null) { result.Warn($"{item.stableId}@{quality}: Addressables group '{string.Format(options.AddressablesGroupFormat, quality)}' not found — not registered."); return; }
        var entry = settings.CreateOrMoveEntry(guid, group);
        entry.address = $"{item.ContentGroupOrDefault}/Wearable_{item.stableId}/sharedVariant/qualityPrefabs@{quality}";
        foreach (string label in new[] { item.ContentGroupOrDefault, "Q_" + quality }) { settings.AddLabel(label); entry.SetLabel(label, true); }
        EditorUtility.SetDirty(settings);
    }

    /// <summary>Initial slots of a NEW item by category (inner tops use Accessory so they combine with an outer
    /// UpperBody jacket, like the existing Wagner shirt). Only written on creation.</summary>
    private static AvatarSlot DefaultSlots(string category)
    {
        switch ((category ?? "").ToLowerInvariant())
        {
            case "jacket": case "coat": return AvatarSlot.UpperBody;
            case "shirt": case "top": return AvatarSlot.Accessory;
            case "trousers": case "pants": case "skirt": return AvatarSlot.LowerBody;
            case "dress": return AvatarSlot.UpperBody | AvatarSlot.LowerBody;
            case "shoes": return AvatarSlot.Feet;
            case "gloves": return AvatarSlot.Hands;
            case "hat": return AvatarSlot.Headwear;
            default: return AvatarSlot.None;
        }
    }

    private static GarmentLayer DefaultLegacyLayer(string category)
    {
        switch ((category ?? "").ToLowerInvariant())
        {
            case "jacket": case "coat": return GarmentLayer.Outerwear;
            case "trousers": case "pants": case "skirt": return GarmentLayer.Pants;
            default: return GarmentLayer.Shirt;
        }
    }

    // ── Checks / helpers ──────────────────────────────────────────────────────────

    /// <summary>Vertices identical in position, normal, UV0 and skin weights are "exact duplicates" (the
    /// importer welds those unless something forces a split); a duplicate group whose members are used by
    /// different submeshes only is a split at a region boundary — the producer shares boundary vertices, so
    /// these should be ~0.</summary>
    public static (int exactGroups, int boundaryGroups) CountDuplicateVertices(Mesh mesh)
    {
        var positions = mesh.vertices;
        var normals = mesh.normals;
        var uvs = new List<Vector2>();
        mesh.GetUVs(0, uvs);
        var weights = mesh.boneWeights;
        var users = new ulong[positions.Length];
        for (int s = 0; s < mesh.subMeshCount; s++)
            foreach (int i in mesh.GetIndices(s)) users[i] |= 1UL << (s % 64);

        var groups = new Dictionary<(Vector3Int, Vector3Int, Vector2Int, int, int), List<int>>();
        Vector3Int Q3(Vector3 v) => new(Mathf.RoundToInt(v.x * 1e5f), Mathf.RoundToInt(v.y * 1e5f), Mathf.RoundToInt(v.z * 1e5f));
        for (int i = 0; i < positions.Length; i++)
        {
            var uv = uvs.Count > i ? uvs[i] : Vector2.zero;
            var w = weights.Length > i ? weights[i] : default;
            var key = (Q3(positions[i]), normals.Length > i ? Q3(normals[i] * 0.01f) : default, new Vector2Int(Mathf.RoundToInt(uv.x * 1e5f), Mathf.RoundToInt(uv.y * 1e5f)),
                       w.boneIndex0, Mathf.RoundToInt(w.weight0 * 1000f));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>(1);
            list.Add(i);
        }
        int exact = 0, boundary = 0;
        foreach (var list in groups.Values)
        {
            if (list.Count < 2) continue;
            exact++;
            bool disjoint = true;
            for (int a = 0; a < list.Count && disjoint; a++)
                for (int b = a + 1; b < list.Count; b++)
                    if ((users[list[a]] & users[list[b]]) != 0) { disjoint = false; break; }
            if (disjoint) boundary++;
        }
        return (exact, boundary);
    }

    private static Mesh BodyMesh(BaseAvatarDefinitionSO baseAvatar, out HashSet<string> bones)
    {
        bones = null;
        var prefab = baseAvatar != null ? baseAvatar.baseAvatarPrefab.editorAsset : null;
        var part = prefab != null ? prefab.GetComponentInChildren<AvatarVisualPart>(true) : null;
        if (part == null) return null;
        var body = AvatarClothingBinder.MainBody(part.skinnedRenderers);
        if (body == null) return null;
        bones = new HashSet<string>(prefab.GetComponentsInChildren<Transform>(true).Select(t => t.name));
        return body.sharedMesh;
    }

    private static string CoverageHash(GarmentCutsPackage.CoverageEntry c)
    {
        var temp = ScriptableObject.CreateInstance<SegmentedGarmentSO>();
        try
        {
            temp.category = c?.category ?? "";
            temp.layer = c?.layer ?? 0;
            temp.bodyCoverage = c?.bodyCoverage ?? new List<string>();
            temp.garmentCoverage = (c?.garmentCoverage ?? new List<GarmentCutsPackage.CoverageRuleEntry>())
                .Select(r => new GarmentCoverageRule { targetCategories = r.targetCategories, zones = r.zones }).ToList();
            return temp.ComputeCoverageHash();
        }
        finally { UnityEngine.Object.DestroyImmediate(temp); }
    }

    public static int QualityOrder(string quality)
    {
        int i = Array.IndexOf(new[] { "Low", "Mid", "HighMid", "High", "Ultra" }, quality);
        return i < 0 ? 99 : i;
    }

    private static Transform FindPath(Transform root, string path) => string.IsNullOrEmpty(path) ? root : root.Find(path);

    private static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        created = asset == null;
        if (!created) return asset;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    public static void EnsureFolder(string path)
    {
        path = path.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var sb = new StringBuilder();
        foreach (byte b in sha.ComputeHash(stream)) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static string Short(string sha) => string.IsNullOrEmpty(sha) ? "none" : sha.Substring(0, Math.Min(12, sha.Length));
}
