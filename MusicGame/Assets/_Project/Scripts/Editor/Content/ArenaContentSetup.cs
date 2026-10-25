#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Tools > MusicGame > Content > Arenas > Setup Arenas — IDEMPOTENT:
///   • Art/Arenas/_Base/Arena_Base.prefab — the arena CONTRACT, built from the old placeholder floor:
///       Arena_&lt;…&gt; (ArenaEnvironment)
///         Ground/Floor   plane 20×20 m at y = 0, MeshCollider, shared placeholder material
///         Background, Midground, Foreground, Lighting (extra lights only), Interactables — empty slots
///   • Art/Arenas/&lt;Composer&gt;/&lt;Composer&gt;_Arena_01..03.prefab — 36 INDEPENDENT copies (own GUID each)
///   • OpponentDefinition.arenas — each composer references its three (missing refs added, extra kept)
///   • Addressables group Remote_Arenas: one bundle per arena (pack separately), remote paths like the
///     enemy groups (local in the DevelopmentLocal profile), stable addresses Arena/&lt;id&gt;/&lt;NN&gt;, labels
///     Category_Arenas + Arena_&lt;id&gt;; the shared placeholder material is its own entry (one bundle, no duplicates).
/// Existing prefabs are NEVER rebuilt or overwritten (art edits are safe); GUIDs and addresses never change.
/// Batch: -executeMethod ArenaContentSetup.SetupFromCommandLine
/// </summary>
public static class ArenaContentSetup
{
    public const string Root = "Assets/_Project/Art/Arenas";
    public const string BaseFolder = Root + "/_Base";
    public const string BasePath = BaseFolder + "/Arena_Base.prefab";
    public const string MaterialPath = BaseFolder + "/Arena_Placeholder_Ground.mat";
    public const string GroupName = "Remote_Arenas";
    public const string CategoryLabel = "Category_Arenas";
    public const int ArenasPerComposer = 3;

    [MenuItem("Tools/MusicGame/Content/Arenas/Setup Arenas")]
    public static void SetupMenu() => Debug.Log(Setup(out _));

    public static void SetupFromCommandLine()
    {
        string report = Setup(out bool ok);
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/ArenaContentSetup.txt", report, Encoding.UTF8);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static string Setup(out bool ok)
    {
        var sb = new StringBuilder("[ArenaContentSetup]\n");
        ok = true;
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var roster = app != null && app.opponentRoster != null ? app.opponentRoster.opponents.Where(o => o != null).ToList() : new List<OpponentDefinition>();
        if (roster.Count == 0) { ok = false; return sb.AppendLine("  FAIL  no roster").ToString(); }

        EnsureFolder(BaseFolder);
        var material = EnsureMaterial(sb);
        EnsureBasePrefab(material, sb);

        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        var group = EnsureGroup(settings, sb);
        settings.AddLabel(CategoryLabel);
        AddEntry(settings, group, MaterialPath, "Arena/_Shared/PlaceholderGround", CategoryLabel);

        var allGuids = new HashSet<string>();
        foreach (var composer in roster)
        {
            string folderName = Title(composer.id);
            string folder = $"{Root}/{folderName}";
            EnsureFolder(folder);
            string label = "Arena_" + composer.id;
            settings.AddLabel(label);
            composer.arenas ??= new List<AssetReferenceGameObject>();
            int created = 0, linked = 0;
            for (int n = 1; n <= ArenasPerComposer; n++)
            {
                string name = $"{folderName}_Arena_{n:00}";
                string path = $"{folder}/{name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    if (!AssetDatabase.CopyAsset(BasePath, path)) { ok = false; sb.AppendLine($"  FAIL  copy {path}"); continue; }
                    var contents = PrefabUtility.LoadPrefabContents(path);
                    contents.name = name;
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    PrefabUtility.UnloadPrefabContents(contents);
                    created++;
                }
                string guid = AssetDatabase.AssetPathToGUID(path);
                allGuids.Add(guid);
                if (ArenaSceneCatalog.Current != null && ArenaSceneCatalog.Current.Contains(guid)) settings.RemoveAssetEntry(guid);
                else AddEntry(settings, group, path, $"Arena/{composer.id}/{n:00}", CategoryLabel, label);
                if (!composer.arenas.Any(r => r != null && r.AssetGUID == guid)) { composer.arenas.Add(new AssetReferenceGameObject(guid)); linked++; }
            }
            if (linked > 0) EditorUtility.SetDirty(composer);
            int valid = composer.arenas.Count(r => r != null && r.RuntimeKeyIsValid() && AssetDatabase.GUIDToAssetPath(r.AssetGUID).EndsWith(".prefab"));
            if (valid < ArenasPerComposer) { ok = false; sb.AppendLine($"  FAIL  {composer.id}: {valid} valid arena refs"); }
            sb.AppendLine($"  ok    {composer.id,-12} {valid} arenas ({created} new prefab(s), {linked} ref(s) added) → {folder}");
        }

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        sb.Append(Validate(settings, roster, allGuids, ref ok));
        sb.AppendLine(ok ? "  RESULT PASS" : "  RESULT FAIL");
        return sb.ToString();
    }

    // ── Validation (configuration — not a real remote download) ─────────────────

    private static string Validate(AddressableAssetSettings settings, List<OpponentDefinition> roster, HashSet<string> guids, ref bool ok)
    {
        var sb = new StringBuilder();
        var group = settings.FindGroup(GroupName);
        var schema = group.GetSchema<BundledAssetGroupSchema>();
        bool separate = schema.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        bool remote = schema.LoadPath.GetName(settings) == AddressableAssetSettings.kRemoteLoadPath;
        if (!separate) ok = false;
        sb.AppendLine($"  {(separate ? "PASS" : "FAIL")}  {GroupName}: pack separately (one bundle per arena), {(remote ? "remote" : "LOCAL")} load path");
        int arenaEntries = group.entries.Count(e => e.AssetPath.EndsWith(".prefab"));
        bool unique = guids.Count == roster.Count * ArenasPerComposer;
        int sceneBacked = guids.Count(g => ArenaSceneCatalog.Current != null && ArenaSceneCatalog.Current.Contains(g));
        if (!unique || arenaEntries + sceneBacked < guids.Count) ok = false;
        sb.AppendLine($"  {(unique ? "PASS" : "FAIL")}  {guids.Count} distinct arena prefabs (GUIDs), {arenaEntries} Addressable entries");
        // Character content and arenas never share a group / label.
        var enemyGroups = settings.groups.Where(g => g != null && g.Name.StartsWith("Remote_Enemies")).ToList();
        bool mixed = enemyGroups.Any(g => g.entries.Any(e => guids.Contains(e.guid)));
        if (mixed) ok = false;
        sb.AppendLine($"  {(mixed ? "FAIL" : "PASS")}  arenas are not in the enemy (character) groups — downloaded independently");
        bool labelsSeparate = group.entries.All(e => !e.labels.Any(l => l.StartsWith("Enemy_")));
        sb.AppendLine($"  {(labelsSeparate ? "PASS" : "FAIL")}  no Enemy_<id> label on arenas (preloading a rival never downloads its arenas)");
        if (!labelsSeparate) ok = false;
        return sb.ToString();
    }

    // ── Assets ───────────────────────────────────────────────────────────────

    private static Material EnsureMaterial(StringBuilder sb)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null) return mat;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader) { name = "Arena_Placeholder_Ground" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.42f, 0.42f, 0.45f));
        else mat.color = new Color(0.42f, 0.42f, 0.45f);
        AssetDatabase.CreateAsset(mat, MaterialPath);
        sb.AppendLine($"  new   {MaterialPath} ({shader.name})");
        return mat;
    }

    private static void EnsureBasePrefab(Material material, StringBuilder sb)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(BasePath) != null) return;
        var root = new GameObject("Arena_Base");
        root.AddComponent<ArenaEnvironment>();
        var ground = new GameObject("Ground");
        ground.transform.SetParent(root.transform, false);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); // same 20×20 m floor as the old placeholder, with its MeshCollider
        floor.name = "Floor";
        floor.transform.SetParent(ground.transform, false);
        floor.transform.localScale = new Vector3(2f, 1f, 2f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = material;
        foreach (var slot in new[] { "Background", "Midground", "Foreground", "Lighting", "Interactables" })
            new GameObject(slot).transform.SetParent(root.transform, false);
        PrefabUtility.SaveAsPrefabAsset(root, BasePath);
        Object.DestroyImmediate(root);
        sb.AppendLine($"  new   {BasePath}");
    }

    private static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings, StringBuilder sb)
    {
        var group = settings.FindGroup(GroupName);
        if (group == null)
        {
            group = settings.CreateGroup(GroupName, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            sb.AppendLine($"  new   group {GroupName}");
        }
        var schema = group.GetSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        schema.IncludeInBuild = true;
        group.GetSchema<ContentUpdateGroupSchema>().StaticContent = false; // updatable after release, like the enemy groups
        EditorUtility.SetDirty(schema);
        return group;
    }

    private static void AddEntry(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address, params string[] labels)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid)) return;
        var entry = settings.FindAssetEntry(guid);
        if (entry == null || entry.parentGroup != group) entry = settings.CreateOrMoveEntry(guid, group);
        if (entry.address != address) entry.address = address; // stable address (re-runs keep it)
        foreach (var l in labels) entry.SetLabel(l, true, true);
    }

    private static string Title(string id) => string.IsNullOrEmpty(id) ? "Unknown" : char.ToUpperInvariant(id[0]) + id.Substring(1);

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
