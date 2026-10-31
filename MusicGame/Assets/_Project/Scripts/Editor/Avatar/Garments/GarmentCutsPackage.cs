using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// The clothing cuts package produced by the 3D pipeline (AssetsMaterial/3D_pipeline/_System/ClothingCuts,
/// 08_GENERAR_TALLS_I_MASCARES.bat), schemaVersion 3:
///
///   manifest.json                      maskConvention, bodyObject, bodyUv, avatarSha256, regionIds, masks, assets
///   masks/&lt;region&gt;.png               body-UV skin masks, white hidden / black visible
///   garments/…_cuts.fbx                one mesh per source mesh, one TRANSPORT material per (zone, source material)
///   garments/…_cuts.manifest.json      that asset's entry (zones ↔ transport materials ↔ source materials)
///   report.json                        optional producer status/errors
///
/// Load() parses and validates without touching the project; every problem becomes a PackageIssue on the
/// package or on one asset, so a package with valid and invalid entries can still import the valid ones.
/// </summary>
public sealed class GarmentCutsPackage
{
    public const int SupportedSchemaVersion = 3;

    public enum Severity { Info, Warning, Error }

    public sealed class Issue
    {
        public Severity Severity;
        public string Message;
        public override string ToString() => $"{Severity.ToString().ToUpperInvariant()}: {Message}";
    }

    // ── JSON contract ─────────────────────────────────────────────────────────────

    public sealed class MaskConvention { public string white; public string black; public string combine; }
    public sealed class BodyMaskSet { public string id; public string directoryPath; public bool available; }
    public sealed class MaskEntry { public string region; public string file; public int hiddenPixels; }

    public sealed class SubmeshEntry
    {
        public string zone;
        public int sourceMaterialIndex;
        public string sourceMaterial;
        public string transportMaterial;
    }

    public sealed class ObjectEntry
    {
        [JsonProperty("object")] public string objectName;
        public string sourceObject;
        public List<SubmeshEntry> submeshes = new();
        public int verticesBefore, verticesAfter, trianglesBefore, trianglesAfter, blendshapes, unassignedTriangles;
    }

    public sealed class CoverageRuleEntry { public List<string> targetCategories = new(); public List<string> zones = new(); }

    public sealed class CoverageEntry
    {
        public string id;
        public string category;
        public int? layer;
        public List<string> bodyCoverage = new();
        public List<CoverageRuleEntry> garmentCoverage = new();
    }

    public sealed class AssetEntry
    {
        public string source;
        public string sourceSha256;
        public string file;
        public List<ObjectEntry> objects = new();
        public CoverageEntry coverage;
        public bool coverageConfigured;

        // Derived at load time.
        [JsonIgnore] public string GarmentId;
        [JsonIgnore] public string Quality;      // project quality name (Low/Mid/HighMid/High/Ultra), "" = single prefab
        [JsonIgnore] public string FullPath;
        [JsonIgnore] public readonly List<Issue> Issues = new();
        [JsonIgnore] public bool IsValid => Issues.All(i => i.Severity != Severity.Error);
        [JsonIgnore] public string Key => GarmentId + "@" + (string.IsNullOrEmpty(Quality) ? "Default" : Quality);
    }

    public sealed class Manifest
    {
        public int schemaVersion;
        public MaskConvention maskConvention;
        public BodyMaskSet bodyMaskSet;
        public string bodyObject;
        public string bodyUv;
        public string avatarSha256;
        public List<string> regionIds = new();
        public List<MaskEntry> masks = new();
        public List<AssetEntry> assets = new();
    }

    // ── Loaded package ────────────────────────────────────────────────────────────

    public string Root { get; private set; }
    public Manifest Data { get; private set; }
    public readonly List<Issue> Issues = new();
    public JObject Report { get; private set; }
    public IReadOnlyList<AssetEntry> Assets => Data?.assets ?? (IReadOnlyList<AssetEntry>)Array.Empty<AssetEntry>();
    public bool IsUsable => Data != null && Issues.All(i => i.Severity != Severity.Error);

    public string MaskPath(MaskEntry mask) => Path.Combine(Root, mask.file);

    public static GarmentCutsPackage Load(string root)
    {
        var package = new GarmentCutsPackage { Root = Path.GetFullPath(root) };
        string manifestPath = Path.Combine(package.Root, "manifest.json");
        if (!File.Exists(manifestPath)) { package.Add(Severity.Error, $"No manifest.json in '{package.Root}'."); return package; }

        try { package.Data = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(manifestPath)); }
        catch (Exception e) { package.Add(Severity.Error, $"manifest.json could not be parsed: {e.Message}"); return package; }
        if (package.Data == null) { package.Add(Severity.Error, "manifest.json is empty."); return package; }

        string reportPath = Path.Combine(package.Root, "report.json");
        if (File.Exists(reportPath))
        {
            try
            {
                package.Report = JObject.Parse(File.ReadAllText(reportPath));
                string status = (string)package.Report["status"];
                if (status != null && status != "complete") package.Add(Severity.Warning, $"report.json status is '{status}' — check its errors; only the delivered assets can be imported.");
                foreach (var error in package.Report["errors"] as JArray ?? new JArray())
                    package.Add(Severity.Warning, $"producer error for '{error["source"]}': {error["error"]}");
            }
            catch (Exception e) { package.Add(Severity.Warning, $"report.json could not be parsed: {e.Message}"); }
        }

        // Shared body masks are delivered once, independently of garment packages.
        var data = package.Data;
        if (data.schemaVersion == SupportedSchemaVersion && data.bodyMaskSet != null &&
            !string.IsNullOrEmpty(data.bodyMaskSet.directoryPath))
        {
            data.masks = new List<MaskEntry>();
            foreach (string region in data.regionIds ?? new List<string>())
            {
                if (region != Path.GetFileName(region)) continue;
                string path = Path.GetFullPath(Path.Combine(data.bodyMaskSet.directoryPath, region + ".png"));
                data.masks.Add(new MaskEntry { region = region, file = path });
            }
        }
        package.Validate();
        return package;
    }

    private void Validate()
    {
        var m = Data;
        if (m.schemaVersion != SupportedSchemaVersion) Add(Severity.Error, $"schemaVersion {m.schemaVersion} is not supported (expected {SupportedSchemaVersion}).");
        if (m.maskConvention == null || m.maskConvention.white != "hidden" || m.maskConvention.black != "visible" || m.maskConvention.combine != "maximum")
            Add(Severity.Error, "maskConvention must be { white: hidden, black: visible, combine: maximum }.");
        if (string.IsNullOrEmpty(m.bodyObject)) Add(Severity.Warning, "bodyObject is empty.");
        if (string.IsNullOrEmpty(m.bodyUv)) Add(Severity.Warning, "bodyUv is empty.");
        if (string.IsNullOrEmpty(m.avatarSha256)) Add(Severity.Warning, "avatarSha256 is empty — body compatibility can only be checked geometrically.");
        m.regionIds ??= new List<string>();
        m.masks ??= new List<MaskEntry>();
        m.assets ??= new List<AssetEntry>();

        var regions = new HashSet<string>(m.regionIds, StringComparer.Ordinal);
        if (regions.Count != m.regionIds.Count) Add(Severity.Error, "regionIds contains duplicates.");
        if (regions.Contains(GarmentRegionCatalogSO.UnassignedRegion)) Add(Severity.Warning, "regionIds lists 'unassigned' — it is implicit and never hidden.");

        var maskRegions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mask in m.masks)
        {
            if (mask == null || string.IsNullOrEmpty(mask.region)) { Add(Severity.Error, "A masks entry has no region."); continue; }
            if (!regions.Contains(mask.region)) Add(Severity.Warning, $"Mask for region '{mask.region}', which is not in regionIds — ignored.");
            if (!maskRegions.Add(mask.region)) Add(Severity.Error, $"Two masks for region '{mask.region}'.");
            if (string.IsNullOrEmpty(mask.file) || !File.Exists(MaskPath(mask))) Add(Severity.Warning, $"Mask file for region '{mask.region}' is missing ('{mask.file}') — that skin will stay visible.");
        }
        foreach (var region in m.regionIds)
            if (!maskRegions.Contains(region)) Add(Severity.Warning, $"Region '{region}' has no body mask — garments covering it will not hide that skin.");

        foreach (var asset in m.assets) ValidateAsset(asset, regions);

        foreach (var group in m.assets.GroupBy(a => a.Key).Where(g => g.Count() > 1))
            foreach (var asset in group)
                asset.Issues.Add(new Issue { Severity = Severity.Error, Message = $"Another asset in the package also resolves to garment '{asset.GarmentId}' quality '{asset.Quality}' — select one source per quality." });
    }

    private void ValidateAsset(AssetEntry asset, HashSet<string> regions)
    {
        void Issue(Severity s, string message) => asset.Issues.Add(new Issue { Severity = s, Message = message });

        asset.Quality = QualityFromSource(asset.source);
        asset.GarmentId = !string.IsNullOrWhiteSpace(asset.coverage?.id) ? asset.coverage.id.Trim() : GarmentIdFromSource(asset.source);
        if (string.IsNullOrEmpty(asset.GarmentId)) Issue(Severity.Error, "No garment id (coverage.id) and none could be derived from the source name.");

        if (string.IsNullOrEmpty(asset.file)) { Issue(Severity.Error, "No 'file'."); return; }
        asset.FullPath = Path.Combine(Root, asset.file);
        if (!File.Exists(asset.FullPath)) Issue(Severity.Error, $"Model '{asset.file}' is missing.");
        else if (!asset.FullPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) Issue(Severity.Error, $"Model '{asset.file}' is not an FBX.");

        string perAsset = Path.ChangeExtension(asset.FullPath, ".manifest.json");
        if (!File.Exists(perAsset)) Issue(Severity.Warning, $"Per-asset manifest '{Path.GetFileName(perAsset)}' is missing (the global entry is used).");
        else
        {
            try
            {
                var own = JsonConvert.DeserializeObject<AssetEntry>(File.ReadAllText(perAsset));
                if (own == null || own.sourceSha256 != asset.sourceSha256 || (own.objects?.Count ?? 0) != asset.objects.Count)
                    Issue(Severity.Warning, $"'{Path.GetFileName(perAsset)}' does not match the global manifest entry (the global entry is used).");
            }
            catch (Exception e) { Issue(Severity.Warning, $"'{Path.GetFileName(perAsset)}' could not be parsed: {e.Message}"); }
        }

        if (asset.objects == null || asset.objects.Count == 0) { Issue(Severity.Error, "No objects."); return; }
        foreach (var obj in asset.objects)
        {
            if (obj.submeshes == null || obj.submeshes.Count == 0) { Issue(Severity.Error, $"Object '{obj.objectName}' has no submeshes."); continue; }
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in obj.submeshes)
            {
                if (string.IsNullOrEmpty(s.transportMaterial)) { Issue(Severity.Error, $"Object '{obj.objectName}': a submesh has no transportMaterial."); continue; }
                if (!tokens.Add(s.transportMaterial)) Issue(Severity.Error, $"Object '{obj.objectName}': transport material '{s.transportMaterial}' is listed twice (ambiguous).");
                if (string.IsNullOrEmpty(s.zone)) Issue(Severity.Warning, $"Object '{obj.objectName}': '{s.transportMaterial}' has no zone — its geometry is always kept.");
                else if (s.zone != GarmentRegionCatalogSO.UnassignedRegion && !regions.Contains(s.zone))
                    Issue(Severity.Warning, $"Object '{obj.objectName}': zone '{s.zone}' is not in regionIds — its geometry is always kept.");
            }
            int sources = obj.submeshes.Select(s => s.sourceMaterial ?? "").Distinct().Count();
            if (sources > 1) Issue(Severity.Warning, $"Object '{obj.objectName}' has {sources} source materials ({string.Join(", ", obj.submeshes.Select(s => s.sourceMaterial).Distinct())}); the garment renders with ONE final material.");
            if (obj.unassignedTriangles > 0 && obj.trianglesAfter > 0)
                Issue(Severity.Info, $"Object '{obj.objectName}': {obj.unassignedTriangles} of {obj.trianglesAfter} triangles are 'unassigned' (always visible).");
        }

        if (!asset.coverageConfigured) Issue(Severity.Warning, "coverageConfigured is false: the garment hides nothing until its coverage is configured.");
        var c = asset.coverage;
        if (c == null) return;
        if (asset.coverageConfigured && c.layer == null) Issue(Severity.Warning, "coverage has no layer (0 is used).");
        if (asset.coverageConfigured && string.IsNullOrEmpty(c.category)) Issue(Severity.Warning, "coverage has no category — no garmentCoverage rule can target it.");
        foreach (var region in c.bodyCoverage ?? new List<string>())
            if (!regions.Contains(region)) Issue(Severity.Warning, $"bodyCoverage region '{region}' is not in regionIds — ignored.");
        foreach (var rule in c.garmentCoverage ?? new List<CoverageRuleEntry>())
        {
            if (rule.targetCategories == null || rule.targetCategories.Count == 0) Issue(Severity.Warning, "A garmentCoverage rule has no targetCategories — it never applies.");
            foreach (var zone in rule.zones ?? new List<string>())
                if (!regions.Contains(zone)) Issue(Severity.Warning, $"garmentCoverage zone '{zone}' is not in regionIds — ignored.");
        }
    }

    private void Add(Severity severity, string message) => Issues.Add(new Issue { Severity = severity, Message = message });

    // ── Naming conventions of the 3D pipeline ─────────────────────────────────────

    private static readonly (Regex token, string quality)[] QualityTokens =
    {
        (new Regex(@"(^|[_\-\s])(MID-?HIGH|MID_HIGH|HIGHMID)([_\-\s.]|$)", RegexOptions.IgnoreCase), "HighMid"),
        (new Regex(@"(^|[_\-\s])ULTRA([_\-\s.]|$)", RegexOptions.IgnoreCase), "Ultra"),
        (new Regex(@"(^|[_\-\s])HIGH([_\-\s.]|$)", RegexOptions.IgnoreCase), "High"),
        (new Regex(@"(^|[_\-\s])(MEDIUM|MID)([_\-\s.]|$)", RegexOptions.IgnoreCase), "Mid"),
        (new Regex(@"(^|[_\-\s])LOW([_\-\s.]|$)", RegexOptions.IgnoreCase), "Low"),
    };

    /// <summary>Project quality level (QualityAssetResolver names) from a pipeline source path:
    /// …_LOW_5k_rigged.fbx → Low, MEDIUM → Mid, MID-HIGH/MIDHIGH → HighMid, HIGH → High, ULTRA → Ultra.</summary>
    public static string QualityFromSource(string source)
    {
        string name = Path.GetFileNameWithoutExtension(source ?? "");
        foreach (var (token, quality) in QualityTokens) if (token.IsMatch(name)) return quality;
        return "";
    }

    /// <summary>Fallback garment id: the source file stem without quality/poly-count/"rigged" tokens, lowercase
    /// (Wagner_pants_HIGH_30k_rigged.fbx → wagner_pants).</summary>
    public static string GarmentIdFromSource(string source)
    {
        string name = Path.GetFileNameWithoutExtension(source ?? "");
        name = Regex.Replace(name, @"[_\-](MID-?HIGH|MIDHIGH|HIGHMID|ULTRA|HIGH|MEDIUM|MID|LOW)([_\-]\d+k)?(?=[_\-]|$)", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"[_\-]rigged$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9_]+", "_").Trim('_');
        return name;
    }
}
