#if UNITY_EDITOR
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
using UnityEngine.Rendering;

/// <summary>
/// Tools > MusicGame > Content > Setup Quality & Content Delivery — idempotent (safe to re-run; never
/// duplicates assets / groups / labels / profiles, never overwrites later manual URP tweaks):
///   1. Five Unity quality levels (Low / Mid / HighMid / High / Ultra) on every platform, LOD Bias 1
///      (no distance LOD), VSync 1 (desktop; mobile ignores it — FPS via DeviceGraphicsApplier).
///   2. Five independent URP assets Assets/Settings/Quality/URP_&lt;Level&gt;.asset (Low/Mid on the
///      Mobile renderer, HighMid/High/Ultra on the PC renderer — renderer features preserved), each
///      assigned to its level; GraphicsSettings default = URP_High. Initial values only on creation.
///   3. Addressables profiles: DevelopmentLocal (ACTIVE — "remote" groups build/load from the local
///      Addressables folder: fully offline) and ProductionCCD (remote groups → ServerData/[BuildTarget],
///      loaded from Unity Cloud Content Delivery via [CcdProjectId]/[CcdEnvironment]/[CcdBucketId]).
///      Remote catalog + content-update restriction check ON (content_state.bin workflow).
///   4. Every QualityAssetCollection in any ScriptableObject → group per category+quality, PACK
///      SEPARATELY (one bundle per asset → independently updatable), labels: Category_&lt;X&gt; +
///      logical group + Q_&lt;Quality&gt;, address "&lt;asset&gt;@&lt;Quality&gt;". Core content → local static
///      groups; Enemies / Styles / PlayerClothing → remote updatable groups.
///   5. Shared dependencies of variant prefabs (used by ≥2 of them) → their own per-quality shared
///      groups (no duplication, never mixing qualities); shaders (incl. URP package shaders) → ONE local
///      Core_Shared_Shaders bundle; cross-quality sharing is reported.
/// Validate Quality Variants — report only.
/// </summary>
public static class ContentDeliverySetup
{
    public static readonly string[] Levels = { "Low", "Mid", "HighMid", "High", "Ultra" };
    private const string ConfigPath = "Assets/_Project/Configs/Content/ContentDeliveryConfig.asset";
    private const string UrpFolder = "Assets/Settings/Quality";
    public const string DevProfile = "DevelopmentLocal";
    public const string CcdProfile = "ProductionCCD";
    private const string LocalBuild = "[UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]";
    private const string LocalLoad = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]";
    private const string CcdLoad = "https://[CcdProjectId].client-api.unity3dusercontent.com/client_api/v1/environments/[CcdEnvironment]/buckets/[CcdBucketId]/release_by_badge/latest/entry_by_path/content/?path=";

    [MenuItem("Tools/MusicGame/Content/Setup Quality & Content Delivery")]
    public static void SetupMenu() => Debug.Log(Setup());

    [MenuItem("Tools/MusicGame/Content/Validate Quality Variants")]
    public static void ValidateMenu() => Debug.Log(Validate(out _));

    public static void SetupFromCommandLine()
    {
        string report = Setup();
        File.WriteAllText("Logs/ContentDeliverySetup.txt", report);
        Debug.Log(report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Setup()
    {
        var sb = new StringBuilder("[ContentDeliverySetup]\n");
        var urp = SetupUrpAssets(sb);
        SetupQualityLevels(urp, sb);
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { sb.AppendLine("  ERROR  no AddressableAssetSettings"); return sb.ToString(); }
        SetupProfiles(settings, sb);
        SetupCatalogAndCoreGroups(settings, sb);
        AssignQualityVariants(settings, sb);
        IsolateSharedDependencies(settings, sb);
        EnsureConfigAsset();
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
        AssetDatabase.SaveAssets();
        sb.Append(Validate(out _));
        return sb.ToString();
    }

    // ── 1-2. URP assets + quality levels ────────────────────────────────────────

    // Initial values (applied ONCE, when the asset is created). addLightsMode: 0 off, 1 per pixel, 2 per vertex.
    public static readonly Dictionary<string, (float renderScale, int msaa, int shadowRes, float shadowDistance, int cascades, int addLightsMode, int addLightsPerObject, bool addLightShadows, int addShadowRes, bool softShadows)> UrpDefaults = new()
    {
        ["Low"]     = (0.7f,  1, 512,  20f, 1, 2, 2, false, 512,  false),
        ["Mid"]     = (0.85f, 1, 1024, 30f, 1, 1, 2, false, 1024, false),
        ["HighMid"] = (1f,    2, 1024, 40f, 2, 1, 4, false, 1024, true),
        ["High"]    = (1f,    4, 2048, 50f, 4, 1, 4, true,  2048, true),
        ["Ultra"]   = (1f,    8, 4096, 70f, 4, 1, 8, true,  4096, true),
    };

    private static Dictionary<string, RenderPipelineAsset> SetupUrpAssets(StringBuilder sb)
    {
        var result = new Dictionary<string, RenderPipelineAsset>();
        if (!AssetDatabase.IsValidFolder(UrpFolder)) AssetDatabase.CreateFolder("Assets/Settings", "Quality");
        foreach (var level in Levels)
        {
            string path = $"{UrpFolder}/URP_{level}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
            if (asset == null)
            {
                string source = level is "Low" or "Mid" ? "Assets/Settings/Mobile_RPAsset.asset" : "Assets/Settings/PC_RPAsset.asset";
                if (!AssetDatabase.CopyAsset(source, path)) { sb.AppendLine($"  ERROR  could not copy {source} → {path}"); continue; }
                asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
                var so = new SerializedObject(asset);
                var v = UrpDefaults[level];
                void F(string n, float x) { var p = so.FindProperty(n); if (p != null) p.floatValue = x; else sb.AppendLine($"  WARN   URP field {n} missing"); }
                void I(string n, int x) { var p = so.FindProperty(n); if (p != null) p.intValue = x; else sb.AppendLine($"  WARN   URP field {n} missing"); }
                void B(string n, bool x) { var p = so.FindProperty(n); if (p != null) p.boolValue = x; else sb.AppendLine($"  WARN   URP field {n} missing"); }
                F("m_RenderScale", v.renderScale);
                I("m_MSAA", v.msaa);
                I("m_MainLightShadowmapResolution", v.shadowRes);
                F("m_ShadowDistance", v.shadowDistance);
                I("m_ShadowCascadeCount", v.cascades);
                I("m_AdditionalLightsRenderingMode", v.addLightsMode);
                I("m_AdditionalLightsPerObjectLimit", v.addLightsPerObject);
                B("m_AdditionalLightShadowsSupported", v.addLightShadows);
                I("m_AdditionalLightsShadowmapResolution", v.addShadowRes);
                B("m_SoftShadowsSupported", v.softShadows);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                sb.AppendLine($"  PASS  created {path} (from {Path.GetFileNameWithoutExtension(source)}: scale {v.renderScale}, MSAA {v.msaa}x, shadows {v.shadowRes}px/{v.shadowDistance}m/{v.cascades} cascades, add. lights mode {v.addLightsMode} ×{v.addLightsPerObject})");
            }
            else sb.AppendLine($"  {path} exists — kept (values not overwritten)");
            result[level] = asset;
        }
        if (result.TryGetValue("High", out var high) && GraphicsSettings.defaultRenderPipeline != high)
        {
            GraphicsSettings.defaultRenderPipeline = high;
            sb.AppendLine("  PASS  GraphicsSettings default render pipeline = URP_High");
        }
        return result;
    }

    private static void SetupQualityLevels(Dictionary<string, RenderPipelineAsset> urp, StringBuilder sb)
    {
        var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
        if (asset == null) { sb.AppendLine("  ERROR  QualitySettings asset not found"); return; }
        var so = new SerializedObject(asset);
        var levels = so.FindProperty("m_QualitySettings");
        var current = Enumerable.Range(0, levels.arraySize).Select(i => levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue).ToArray();

        if (!current.SequenceEqual(Levels))
        {
            if (current.Length != 2) { sb.AppendLine($"  ERROR  unexpected quality levels [{string.Join(", ", current)}] — configure the five levels by hand"); return; }
            levels.InsertArrayElementAtIndex(0); // [Mobile, PC] → [Mobile, Mobile, PC, PC, PC]
            levels.InsertArrayElementAtIndex(2);
            levels.InsertArrayElementAtIndex(2);
            so.FindProperty("m_CurrentQuality").intValue = 3;
        }

        for (int i = 0; i < Levels.Length; i++)
        {
            var l = levels.GetArrayElementAtIndex(i);
            l.FindPropertyRelative("name").stringValue = Levels[i];
            l.FindPropertyRelative("excludedTargetPlatforms").ClearArray(); // every level on every platform
            l.FindPropertyRelative("lodBias").floatValue = 1f;               // no distance-based LOD
            l.FindPropertyRelative("vSyncCount").intValue = 1;               // desktop VSync (mobile uses targetFrameRate)
            if (urp.TryGetValue(Levels[i], out var rp)) l.FindPropertyRelative("customRenderPipeline").objectReferenceValue = rp;
        }
        var perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
        if (perPlatform != null)
            for (int i = 0; i < perPlatform.arraySize; i++)
            {
                var p = perPlatform.GetArrayElementAtIndex(i);
                string key = p.FindPropertyRelative("first").stringValue;
                p.FindPropertyRelative("second").intValue = key switch { "Android" => 1, "iPhone" => 2, "WebGL" => 1, _ => 3 };
            }
        so.ApplyModifiedPropertiesWithoutUndo();
        sb.AppendLine("  PASS  quality: 5 levels on every platform, each with its own URP asset, LOD Bias 1, VSync 1; defaults Android Mid / iOS HighMid / Standalone High");
    }

    // ── 3. Profiles ──────────────────────────────────────────────────────────────

    private static void SetupProfiles(AddressableAssetSettings settings, StringBuilder sb)
    {
        var ps = settings.profileSettings;
        foreach (var (name, value) in new[] { ("CcdProjectId", "YOUR-CCD-PROJECT-ID"), ("CcdEnvironment", "production"), ("CcdBucketId", "YOUR-CCD-BUCKET-ID") })
            if (!ps.GetVariableNames().Contains(name)) ps.CreateValue(name, value);

        string Ensure(string name)
        {
            string id = ps.GetProfileId(name);
            return string.IsNullOrEmpty(id) ? ps.AddProfile(name, settings.activeProfileId) : id;
        }
        string dev = Ensure(DevProfile);
        string ccd = Ensure(CcdProfile);
        foreach (string id in new[] { dev, ps.GetProfileId("Default") })
        {
            if (string.IsNullOrEmpty(id)) continue;
            ps.SetValue(id, AddressableAssetSettings.kRemoteBuildPath, LocalBuild);   // "remote" groups are local in development
            ps.SetValue(id, AddressableAssetSettings.kRemoteLoadPath, LocalLoad);     // no server, no placeholder URL contacted
        }
        ps.SetValue(ccd, AddressableAssetSettings.kRemoteBuildPath, "ServerData/[BuildTarget]");
        ps.SetValue(ccd, AddressableAssetSettings.kRemoteLoadPath, CcdLoad);
        settings.activeProfileId = dev;
        sb.AppendLine($"  PASS  profiles: {DevProfile} (ACTIVE, fully local) and {CcdProfile} (remote → CCD: set CcdProjectId / CcdEnvironment / CcdBucketId)");
    }

    // ── Catalog + core groups ────────────────────────────────────────────────────

    private static void SetupCatalogAndCoreGroups(AddressableAssetSettings settings, StringBuilder sb)
    {
        settings.BuildRemoteCatalog = true;
        settings.RemoteCatalogBuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        settings.RemoteCatalogLoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
        settings.CheckForContentUpdateRestrictionsOption = CheckForContentUpdateRestrictionsOptions.ListUpdatedAssetsWithRestrictions;

        settings.AddLabel(ContentKeys.Core);
        foreach (var g in settings.groups)
        {
            if (g == null || g.Name.StartsWith("Remote_")) continue;
            var cu = g.GetSchema<ContentUpdateGroupSchema>();
            if (cu != null && !cu.StaticContent) { cu.StaticContent = true; EditorUtility.SetDirty(cu); }
        }
        var avatars = settings.FindGroup("Avatars");
        if (avatars != null) foreach (var e in avatars.entries) e.SetLabel(ContentKeys.Core, true, true, false);
        sb.AppendLine("  PASS  remote catalog ON, content-update restriction check ON; existing local groups = static Core");
    }

    // ── 4. Quality variants → per-category/per-quality groups, one bundle per asset ──

    private static void AssignQualityVariants(AddressableAssetSettings settings, StringBuilder sb)
    {
        foreach (var l in Levels) settings.AddLabel(QualityAssetResolver.LabelFor(l));
        foreach (ContentCategory c in System.Enum.GetValues(typeof(ContentCategory))) settings.AddLabel(ContentKeys.Category(c));

        int placed = 0;
        foreach (var (owner, collection, propertyPath) in FindCollections())
        {
            string groupLabel = GroupLabelOf(owner);
            var category = ContentKeys.CategoryOf(groupLabel);
            settings.AddLabel(groupLabel);
            string assetId = $"{groupLabel}/{owner.name}/{propertyPath.Replace(".Array.data", "").Replace('.', '/')}";
            foreach (var v in collection.variants)
            {
                int li = v != null ? QualityAssetResolver.IndexOf(Levels, v.qualityName) : -1;
                if (li < 0 || !v.IsAssigned) continue;
                var group = VariantGroup(settings, category, Levels[li]);
                var entry = settings.CreateOrMoveEntry(v.prefab.AssetGUID, group, false, false);
                if (entry == null) continue;
                entry.address = ContentKeys.VariantAddress(assetId, Levels[li]);
                entry.SetLabel(ContentKeys.Category(category), true, true, false);
                entry.SetLabel(groupLabel, true, true, false);
                entry.SetLabel(QualityAssetResolver.LabelFor(Levels[li]), true, true, false);
                placed++;
            }
        }
        // Variant groups always pack separately (an older setup may have used another mode).
        foreach (var g in settings.groups.Where(g => g != null && IsVariantGroup(g.Name)))
        {
            var s = g.GetSchema<BundledAssetGroupSchema>();
            if (s != null && s.BundleMode != BundledAssetGroupSchema.BundlePackingMode.PackSeparately)
            { s.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately; EditorUtility.SetDirty(s); }
        }
        sb.AppendLine($"  PASS  quality variants placed: {placed} (one bundle each; Core local, Enemies/Styles/PlayerClothing remote)");
    }

    public static string GroupLabelOf(ScriptableObject owner) =>
        owner is IContentGroupProvider p ? p.ContentGroupOrDefault : ContentKeys.PlayerClothing(owner.name);

    private static bool IsVariantGroup(string name) => name.StartsWith("Remote_") || name.StartsWith("Core_");

    private static AddressableAssetGroup VariantGroup(AddressableAssetSettings settings, ContentCategory category, string quality) =>
        category == ContentCategory.Core
            ? EnsureGroup(settings, $"Core_{quality}", remote: false)
            : EnsureGroup(settings, $"Remote_{category}_{quality}", remote: true);

    private static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings, string name, bool remote)
    {
        var group = settings.FindGroup(name);
        if (group != null) return group;
        group = settings.CreateGroup(name, false, false, true, null, typeof(ContentUpdateGroupSchema), typeof(BundledAssetGroupSchema));
        var schema = group.GetSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings, remote ? AddressableAssetSettings.kRemoteBuildPath : AddressableAssetSettings.kLocalBuildPath);
        schema.LoadPath.SetVariableByName(settings, remote ? AddressableAssetSettings.kRemoteLoadPath : AddressableAssetSettings.kLocalLoadPath);
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        schema.UseAssetBundleCache = true;
        schema.UseAssetBundleCrc = true;
        group.GetSchema<ContentUpdateGroupSchema>().StaticContent = !remote; // remote = updatable after release
        return group;
    }

    // ── 5. Shared dependencies ───────────────────────────────────────────────────

    private static void IsolateSharedDependencies(AddressableAssetSettings settings, StringBuilder sb)
    {
        var variants = new List<(string path, string quality, ContentCategory category)>();
        foreach (var (owner, collection, _) in FindCollections())
        {
            var category = ContentKeys.CategoryOf(GroupLabelOf(owner));
            foreach (var v in collection.variants)
            {
                int li = v != null ? QualityAssetResolver.IndexOf(Levels, v.qualityName) : -1;
                if (li >= 0 && v.IsAssigned) variants.Add((AssetDatabase.GUIDToAssetPath(v.prefab.AssetGUID), Levels[li], category));
            }
        }

        var users = new Dictionary<string, List<(string path, string quality, ContentCategory category)>>();
        foreach (var v in variants)
            foreach (var dep in AssetDatabase.GetDependencies(v.path, true))
            {
                if (dep == v.path || dep.EndsWith(".cs") || dep.EndsWith(".hlsl") || dep.EndsWith(".cginc")) continue;
                bool shader = IsShader(dep);
                if (!dep.StartsWith("Assets/") && !(shader && dep.StartsWith("Packages/"))) continue; // package shaders (URP Lit…) count too
                var existing = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(dep));
                if (existing != null && !existing.parentGroup.Name.Contains("_Shared")) continue; // already addressable elsewhere: has its own bundle
                if (!users.TryGetValue(dep, out var list)) users[dep] = list = new();
                list.Add(v);
            }

        int perQuality = 0, crossQuality = 0, shaders = 0;
        foreach (var kv in users.Where(kv => kv.Value.Select(u => u.path).Distinct().Count() >= 2))
        {
            var qualities = kv.Value.Select(u => u.quality).Distinct().ToList();
            bool core = kv.Value.All(u => u.category == ContentCategory.Core);
            if (IsShader(kv.Key))
            {
                // Shaders: ONE local copy shipped with the app, referenced by every variant bundle (they
                // were otherwise duplicated into each bundle — ~95 KB per URP Lit copy).
                settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(kv.Key), EnsureGroup(settings, "Core_Shared_Shaders", remote: false), false, false);
                shaders++;
                continue;
            }
            string name = qualities.Count == 1 ? (core ? $"Core_Shared_{qualities[0]}" : $"Remote_Shared_{qualities[0]}")
                                               : (core ? "Core_Shared_Common" : "Remote_Shared_Common");
            settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(kv.Key), EnsureGroup(settings, name, remote: !core), false, false);
            if (qualities.Count == 1) perQuality++;
            else { crossQuality++; sb.AppendLine($"  WARN   {kv.Key} is shared by {string.Join("/", qualities)} variants — make sure a low level does not pull a high-resolution asset"); }
        }
        sb.AppendLine($"  PASS  shared dependencies isolated: {perQuality} per-quality, {crossQuality} cross-quality, {shaders} shader(s) → local Core_Shared_Shaders (never duplicated inside variant bundles)");
    }

    private static bool IsShader(string path) => path.EndsWith(".shader") || path.EndsWith(".shadergraph") || path.EndsWith(".shadersubgraph");

    /// <summary>Every QualityAssetCollection inside any ScriptableObject under Assets/_Project.</summary>
    public static IEnumerable<(ScriptableObject owner, QualityAssetCollection collection, string propertyPath)> FindCollections()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/_Project" }))
        {
            var owner = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (owner == null) continue;
            var it = new SerializedObject(owner).GetIterator();
            while (it.NextVisible(true))
            {
                if (it.type != nameof(QualityAssetCollection)) continue;
                if (it.boxedValue is QualityAssetCollection c && c.variants != null && c.variants.Count > 0)
                    yield return (owner, c, it.propertyPath);
            }
        }
    }

    private static void EnsureConfigAsset()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        if (app == null) return;
        var cfg = AssetDatabase.LoadAssetAtPath<ContentDeliveryConfigSO>(ConfigPath);
        if (cfg == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            cfg = ScriptableObject.CreateInstance<ContentDeliveryConfigSO>();
            AssetDatabase.CreateAsset(cfg, ConfigPath);
        }
        if (app.contentDelivery != cfg) { app.contentDelivery = cfg; EditorUtility.SetDirty(app); }
    }

    // ── Validation ──────────────────────────────────────────────────────────────

    public static string Validate(out int problems)
    {
        var sb = new StringBuilder("[ContentDeliverySetup] Validate\n");
        problems = 0;
        var names = QualitySettings.names;

        var levels = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").First()).FindProperty("m_QualitySettings");
        var rps = new HashSet<Object>();
        for (int i = 0; i < levels.arraySize; i++)
        {
            var l = levels.GetArrayElementAtIndex(i);
            var rp = l.FindPropertyRelative("customRenderPipeline").objectReferenceValue;
            string n = l.FindPropertyRelative("name").stringValue;
            if (rp == null || rp.name != "URP_" + n || !rps.Add(rp)) { sb.AppendLine($"  ERROR  quality '{n}': URP asset '{(rp != null ? rp.name : "none")}' is missing or not its own"); problems++; }
            if (!Mathf.Approximately(l.FindPropertyRelative("lodBias").floatValue, 1f)) { sb.AppendLine($"  ERROR  quality '{n}': LOD Bias != 1"); problems++; }
            if (l.FindPropertyRelative("excludedTargetPlatforms").arraySize > 0) { sb.AppendLine($"  ERROR  quality '{n}': excluded on some platform"); problems++; }
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        int collections = 0;
        foreach (var (owner, c, path) in FindCollections())
        {
            collections++;
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            int assigned = 0;
            foreach (var v in c.variants)
            {
                if (v == null) continue;
                bool known = QualityAssetResolver.IndexOf(names, v.qualityName) >= 0;
                bool hasRef = v.prefab != null && !string.IsNullOrEmpty(v.prefab.AssetGUID);
                if (!known && hasRef) { sb.AppendLine($"  ERROR  {owner.name} › {path}: unknown quality level '{v.qualityName}'"); problems++; }
                else if (known && !seen.Add(v.qualityName)) { sb.AppendLine($"  ERROR  {owner.name} › {path}: duplicate '{v.qualityName}' entry"); problems++; }
                if (!hasRef) continue;
                assigned++;
                var entry = settings != null ? settings.FindAssetEntry(v.prefab.AssetGUID) : null;
                if (entry == null) { sb.AppendLine($"  ERROR  {owner.name} › {path}: '{v.qualityName}' prefab is not Addressable (run Setup)"); problems++; }
                else if (!entry.labels.Contains(QualityAssetResolver.LabelFor(v.qualityName))) { sb.AppendLine($"  ERROR  {owner.name} › {path}: '{v.qualityName}' prefab lacks its Q_ label (run Setup)"); problems++; }
            }
            if (assigned > 0 && assigned < names.Length)
                sb.AppendLine($"  WARN   {owner.name} › {path}: {assigned}/{names.Length} variants (missing ones use the fallback rule)");
        }
        sb.AppendLine($"  {(problems == 0 ? "PASS" : "FAIL")}  {names.Length} quality levels, {collections} quality collection(s), {problems} problem(s)");
        return sb.ToString();
    }
}
#endif
