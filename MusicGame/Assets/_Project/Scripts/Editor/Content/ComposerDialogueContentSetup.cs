#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Localization;
using UnityEditor.Localization.Addressables;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using Object = UnityEngine.Object;

/// <summary>
/// Keeps every ComposerDialogues_* table (and its Shared Table Data) in ONE dedicated Addressables
/// group instead of the per-language UIText groups. Installed as the project's Localization
/// "Addressable Group Rules" string-table resolver, so Unity Localization itself puts new/edited
/// dialogue tables back there (adding a language, refreshing a collection…) — everything else keeps
/// the stock Localization layout.
/// </summary>
[Serializable]
public class ComposerDialogueTableGroupResolver : GroupResolver
{
    public const string GroupName = "Localization-ComposerDialogues";

    public ComposerDialogueTableGroupResolver() : base("Localization-String-Tables-{LocaleName}", "Localization-Assets-Shared") {}

    public override string GetExpectedGroupName(IList<LocaleIdentifier> locales, Object asset, AddressableAssetSettings aaSettings) =>
        IsDialogueAsset(asset) ? GroupName : base.GetExpectedGroupName(locales, asset, aaSettings);

    protected override string GetExpectedSharedGroupName(IList<LocaleIdentifier> locales, Object asset, AddressableAssetSettings aaSettings) =>
        IsDialogueAsset(asset) ? GroupName : base.GetExpectedSharedGroupName(locales, asset, aaSettings);

    public static bool IsDialogueAsset(Object asset) => asset switch
    {
        LocalizationTable t => t.SharedData != null && IsDialogueCollection(t.SharedData.TableCollectionName),
        SharedTableData s   => IsDialogueCollection(s.TableCollectionName),
        _ => false,
    };

    public static bool IsDialogueCollection(string name) => name != null && name.StartsWith(ComposerDialogueKeys.TablePrefix);
}

/// <summary>
/// Composer dialogue CONTENT tooling (Tools > MusicGame > Content > Composer Dialogues):
///
///   Migrate From UIText     moves every Composer.{id}.Dialogue.* entry out of the general UIText
///                           collection into one String Table Collection per composer
///                           (ComposerDialogues_{id}, ca + en), keeping each key's text AND numeric id,
///                           verifies the copy, and only then removes it from UIText. Idempotent.
///   Configure Addressables  dedicated group (local + "Cannot Change Post Release", one bundle per
///                           table), the group-rules resolver above, label ComposerDialogues, catalog
///                           request timeout, and the LocalContentServer test profile.
///   Validate                every structural/content rule the game relies on (see Validate()).
///
/// Batch: -executeMethod ComposerDialogueContentSetup.RunAllFromCommandLine [-dialogueReference path/to/dialogues.json]
/// </summary>
public static class ComposerDialogueContentSetup
{
    public const string UITextTable      = "UIText";
    public const string CollectionFolder = "Assets/_Project/Localization/ComposerDialogues";
    public const string GroupRulesPath   = "Assets/_Project/Localization/LocalizationAddressableGroupRules.asset";
    public const string RosterPath       = "Assets/_Project/Configs/Fight/OpponentRoster.asset";
    public const string LocalServerProfile = "LocalContentServer";
    public const string LocalServerUrl     = "http://localhost:8765/[BuildTarget]";
    public const int    CatalogTimeoutSeconds = 10;

    // ── Menu ─────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/MusicGame/Content/Composer Dialogues/Migrate From UIText")]
    public static void MigrateMenu() => Debug.Log(Migrate(out _));

    [MenuItem("Tools/MusicGame/Content/Composer Dialogues/Configure Addressables")]
    public static void ConfigureMenu() => Debug.Log(ConfigureAddressables());

    [MenuItem("Tools/MusicGame/Content/Composer Dialogues/Validate")]
    public static void ValidateMenu() => Debug.Log(Validate(null, out _));

    public static void RunAllFromCommandLine()
    {
        string reference = CommandLineArg("-dialogueReference");
        var sb = new StringBuilder();
        sb.AppendLine(Migrate(out bool migrated));
        if (migrated) sb.AppendLine(ConfigureAddressables());
        sb.AppendLine(Validate(reference, out bool valid));
        Debug.Log(sb.ToString());
        WriteReport(sb.ToString());
        EditorApplication.Exit(migrated && valid ? 0 : 1);
    }

    public static void ValidateFromCommandLine()
    {
        string report = Validate(CommandLineArg("-dialogueReference"), out bool valid);
        Debug.Log(report);
        WriteReport(report);
        EditorApplication.Exit(valid ? 0 : 1);
    }

    // ── Roster ───────────────────────────────────────────────────────────────────

    public static List<string> ComposerIds()
    {
        var roster = AssetDatabase.LoadAssetAtPath<OpponentRosterSO>(RosterPath);
        var ids = new List<string>();
        if (roster == null || roster.opponents == null) return ids;
        foreach (var o in roster.opponents)
            if (o != null && !string.IsNullOrEmpty(o.id) && !ids.Contains(o.id)) ids.Add(o.id);
        return ids;
    }

    // ── 1. Migration ─────────────────────────────────────────────────────────────

    public static string Migrate(out bool ok)
    {
        var sb = new StringBuilder("[ComposerDialogues] MIGRATE\n");
        ok = true;
        AddressableAssetSettingsDefaultObject.GetSettings(true);

        var ui = LocalizationEditorSettings.GetStringTableCollection(UITextTable);
        if (ui == null) { ok = false; return sb.AppendLine("  FAIL  UIText collection not found").ToString(); }
        var locales = LocalizationEditorSettings.GetLocales().ToList();
        var ids = ComposerIds();
        if (ids.Count == 0) { ok = false; return sb.AppendLine($"  FAIL  no composer ids in {RosterPath}").ToString(); }

        // Dialogue-shaped keys in UIText that belong to NO roster composer are reported, never touched.
        foreach (var e in ui.SharedData.Entries)
            if (ComposerDialogueKeys.TryParse(e.Key, out var cid, out _, out _) && !ids.Contains(cid))
            { ok = false; sb.AppendLine($"  FAIL  UIText key '{e.Key}' belongs to unknown composer '{cid}' — left in place"); }

        EnsureFolder(CollectionFolder);
        foreach (string id in ids)
        {
            var moving = ui.SharedData.Entries.Where(e => e.Key != null && e.Key.StartsWith(ComposerDialogueKeys.KeyPrefix(id))).ToList();
            var collection = LocalizationEditorSettings.GetStringTableCollection(ComposerDialogueKeys.TableName(id));
            if (moving.Count == 0)
            {
                sb.AppendLine(collection != null ? $"  ok    {id}: nothing left in UIText (already migrated)" : $"  WARN  {id}: no dialogue keys anywhere");
                continue;
            }
            if (collection == null)
            {
                collection = LocalizationEditorSettings.CreateStringTableCollection(ComposerDialogueKeys.TableName(id), CollectionFolder, locales);
                sb.AppendLine($"  new   {collection.TableCollectionName} ({AssetDatabase.GetAssetPath(collection)})");
            }
            foreach (var locale in locales)
                if (collection.GetTable(locale.Identifier) == null) collection.AddNewTable(locale.Identifier);

            // Copy: same key, same numeric id (when free), same text in every locale.
            foreach (var shared in moving)
            {
                if (collection.SharedData.GetEntry(shared.Key) == null)
                {
                    if (collection.SharedData.GetEntry(shared.Id) == null) collection.SharedData.AddKey(shared.Key, shared.Id);
                    else collection.SharedData.AddKey(shared.Key);
                }
                foreach (var locale in locales)
                {
                    var src = ui.GetTable(locale.Identifier) as StringTable;
                    var dst = collection.GetTable(locale.Identifier) as StringTable;
                    var value = src?.GetEntry(shared.Id)?.Value;
                    if (value != null) dst.AddEntry(shared.Key, value);
                    EditorUtility.SetDirty(dst);
                }
            }
            EditorUtility.SetDirty(collection.SharedData);

            // Verify the copy BEFORE removing anything from UIText.
            int mismatches = 0;
            foreach (var shared in moving)
                foreach (var locale in locales)
                {
                    var src = (ui.GetTable(locale.Identifier) as StringTable)?.GetEntry(shared.Id)?.Value;
                    var dst = (collection.GetTable(locale.Identifier) as StringTable)?.GetEntry(shared.Key)?.Value;
                    if (src != dst) { mismatches++; sb.AppendLine($"  FAIL  {shared.Key} [{locale.Identifier.Code}] differs after copy"); }
                }
            if (mismatches > 0) { ok = false; continue; }

            foreach (var shared in moving)
            {
                foreach (var locale in locales)
                {
                    var src = ui.GetTable(locale.Identifier) as StringTable;
                    if (src != null) { src.RemoveEntry(shared.Id); EditorUtility.SetDirty(src); }
                }
                ui.SharedData.RemoveKey(shared.Id);
            }
            EditorUtility.SetDirty(ui.SharedData);
            sb.AppendLine($"  moved {moving.Count} keys → {collection.TableCollectionName}");
        }
        AssetDatabase.SaveAssets();
        sb.AppendLine(ok ? "  PASS  migration" : "  FAIL  migration (see above)");
        return sb.ToString();
    }

    // ── 2. Addressables ──────────────────────────────────────────────────────────

    public static string ConfigureAddressables()
    {
        var sb = new StringBuilder("[ComposerDialogues] ADDRESSABLES\n");
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);

        // Dedicated group: shipped in the player (works offline on a clean install), "Cannot Change
        // Post Release" so a content update moves only the CHANGED tables to a remote update group,
        // one bundle per table so a fix to one composer downloads only that composer's language table.
        var group = settings.FindGroup(ComposerDialogueTableGroupResolver.GroupName);
        if (group == null)
        {
            group = settings.CreateGroup(ComposerDialogueTableGroupResolver.GroupName, false, true, true, null,
                                         typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            sb.AppendLine($"  new   group {group.Name}");
        }
        var bundled = group.GetSchema<BundledAssetGroupSchema>();
        bundled.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
        bundled.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
        bundled.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        bundled.IncludeInBuild = true;
        EditorUtility.SetDirty(bundled);
        var update = group.GetSchema<ContentUpdateGroupSchema>();
        update.StaticContent = true;
        EditorUtility.SetDirty(update);

        // Localization's own group rules: our resolver for string tables, stock for everything else.
        var rules = AssetDatabase.LoadAssetAtPath<AddressableGroupRules>(GroupRulesPath);
        if (rules == null)
        {
            rules = ScriptableObject.CreateInstance<AddressableGroupRules>();
            AssetDatabase.CreateAsset(rules, GroupRulesPath);
            sb.AppendLine($"  new   {GroupRulesPath}");
        }
        if (!(rules.StringTablesResolver is ComposerDialogueTableGroupResolver))
        {
            rules.StringTablesResolver = new ComposerDialogueTableGroupResolver();
            EditorUtility.SetDirty(rules);
        }
        AddressableGroupRules.Instance = rules;

        settings.AddLabel(ComposerDialogueKeys.AddressableLabel);
        int entries = 0;
        foreach (var collection in DialogueCollections())
        {
            collection.RefreshAddressables(); // Localization re-files them through the resolver above
            foreach (var asset in AssetsOf(collection))
            {
                var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
                var entry = settings.FindAssetEntry(guid);
                if (entry == null) { sb.AppendLine($"  FAIL  {asset.name} is not Addressable"); continue; }
                if (entry.parentGroup != group) settings.MoveEntry(entry, group, true);
                entry.SetLabel(ComposerDialogueKeys.AddressableLabel, true, true);
                entries++;
            }
        }
        sb.AppendLine($"  ok    {entries} dialogue assets in {group.Name} (local, static, pack separately, label {ComposerDialogueKeys.AddressableLabel})");

        // Content updates of a PREVIOUS release left changed tables in remote "Content Update" groups;
        // a new app release ships them locally again (moved back above) — drop the emptied groups.
        foreach (var g in settings.groups.Where(g => ContentUpdateTools.IsUpdateGroup(g) && g.entries.Count == 0).ToList())
        {
            string name = g.Name;
            settings.RemoveGroup(g);
            sb.AppendLine($"  ok    removed empty group {name}");
        }

        // A dead/slow network must never hang startup on the catalog request.
        if (settings.CatalogRequestsTimeout <= 0 || settings.CatalogRequestsTimeout > CatalogTimeoutSeconds)
            settings.CatalogRequestsTimeout = CatalogTimeoutSeconds;
        sb.AppendLine($"  ok    catalog request timeout {settings.CatalogRequestsTimeout} s, remote catalog {(settings.BuildRemoteCatalog ? "ON" : "OFF")}");

        // Test profile: everything local except the remote catalog + update bundles, served from
        // ServerData by a local HTTP server (see ContentUpdateTools).
        var ps = settings.profileSettings;
        string pid = ps.GetProfileId(LocalServerProfile);
        if (string.IsNullOrEmpty(pid)) pid = ps.AddProfile(LocalServerProfile, ps.GetProfileId(ContentDeliverySetup.DevProfile) ?? settings.activeProfileId);
        ps.SetValue(pid, AddressableAssetSettings.kRemoteBuildPath, "ServerData/[BuildTarget]");
        ps.SetValue(pid, AddressableAssetSettings.kRemoteLoadPath, LocalServerUrl);
        sb.AppendLine($"  ok    profile {LocalServerProfile}: Remote.LoadPath = {LocalServerUrl}");

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    public static IEnumerable<StringTableCollection> DialogueCollections() =>
        LocalizationEditorSettings.GetStringTableCollections()
            .Where(c => ComposerDialogueTableGroupResolver.IsDialogueCollection(c.TableCollectionName))
            .OrderBy(c => c.TableCollectionName);

    private static IEnumerable<Object> AssetsOf(StringTableCollection collection)
    {
        yield return collection.SharedData;
        foreach (var t in collection.StringTables) yield return t;
    }

    // ── 3. Validation ────────────────────────────────────────────────────────────

    /// <param name="referenceJsonPath">Optional dialogues.json (key/ca/en) to compare every text against.</param>
    public static string Validate(string referenceJsonPath, out bool ok)
    {
        var sb = new StringBuilder("[ComposerDialogues] VALIDATE\n");
        int fails = 0;
        void Fail(string m) { fails++; sb.AppendLine("  FAIL  " + m); }
        void Pass(string m) => sb.AppendLine("  PASS  " + m);

        var locales = LocalizationEditorSettings.GetLocales().ToList();
        var ids = ComposerIds();
        if (ids.Count != 12) Fail($"roster has {ids.Count} composers (expected 12)"); else Pass($"12 composers: {string.Join(", ", ids)}");

        // UIText: general entries only.
        var ui = LocalizationEditorSettings.GetStringTableCollection(UITextTable);
        if (ui == null) Fail("UIText missing");
        else
        {
            int dialogueLeft = ui.SharedData.Entries.Count(e => e.Key != null && e.Key.Contains(".Dialogue."));
            int bios = ui.SharedData.Entries.Count(e => e.Key != null && e.Key.StartsWith("Composer.") && e.Key.EndsWith(".Bio"));
            if (dialogueLeft > 0) Fail($"UIText still has {dialogueLeft} dialogue keys"); else Pass("UIText has no dialogue keys");
            if (ui.SharedData.Entries.Count != 183) Fail($"UIText has {ui.SharedData.Entries.Count} keys (expected 183)"); else Pass("UIText keeps its 183 general keys");
            if (bios != 12) Fail($"UIText has {bios} composer bios (expected 12)"); else Pass("12 composer bios still in UIText");
        }

        // Per composer collections.
        var reference = LoadReference(referenceJsonPath);
        int keys = 0, texts = 0, refChecked = 0;
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings != null ? settings.FindGroup(ComposerDialogueTableGroupResolver.GroupName) : null;
        if (group == null) Fail($"Addressables group {ComposerDialogueTableGroupResolver.GroupName} missing");
        else
        {
            var b = group.GetSchema<BundledAssetGroupSchema>();
            var u = group.GetSchema<ContentUpdateGroupSchema>();
            if (b.BundleMode != BundledAssetGroupSchema.BundlePackingMode.PackSeparately) Fail("dialogue group must pack separately");
            if (!u.StaticContent) Fail("dialogue group must be 'Cannot Change Post Release' (shipped + updatable)");
            if (b.LoadPath.GetName(settings) != AddressableAssetSettings.kLocalLoadPath) Fail("dialogue group must load from Local.LoadPath (offline first run)");
        }

        foreach (string id in ids)
        {
            var c = LocalizationEditorSettings.GetStringTableCollection(ComposerDialogueKeys.TableName(id));
            if (c == null) { Fail($"{ComposerDialogueKeys.TableName(id)} missing"); continue; }
            foreach (var asset in AssetsOf(c))
            {
                var entry = settings?.FindAssetEntry(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)));
                if (entry == null) Fail($"{asset.name} is not Addressable");
                else
                {
                    if (group != null && entry.parentGroup != group && !entry.parentGroup.Name.StartsWith("Content Update"))
                        Fail($"{asset.name} is in group {entry.parentGroup.Name}");
                    if (!entry.labels.Contains(ComposerDialogueKeys.AddressableLabel)) Fail($"{asset.name} lacks label {ComposerDialogueKeys.AddressableLabel}");
                }
            }

            var counts = new Dictionary<ComposerDialogueCategory, int>();
            foreach (var shared in c.SharedData.Entries)
            {
                if (!ComposerDialogueKeys.TryParse(shared.Key, out var kid, out var cat, out _) || kid != id)
                { Fail($"{c.TableCollectionName}: unexpected key '{shared.Key}'"); continue; }
                counts[cat] = (counts.TryGetValue(cat, out int n) ? n : 0) + 1;
                keys++;
                foreach (var locale in locales)
                {
                    var value = (c.GetTable(locale.Identifier) as StringTable)?.GetEntry(shared.Id)?.Value;
                    if (string.IsNullOrWhiteSpace(value)) { Fail($"{shared.Key} [{locale.Identifier.Code}] empty"); continue; }
                    texts++;
                    if (reference != null && reference.TryGetValue((shared.Key, locale.Identifier.Code), out var expected))
                    {
                        refChecked++;
                        if (expected != value) Fail($"{shared.Key} [{locale.Identifier.Code}] differs from the reference text");
                    }
                }
            }
            foreach (ComposerDialogueCategory cat in Enum.GetValues(typeof(ComposerDialogueCategory)))
                if (!counts.TryGetValue(cat, out int n) || n == 0) Fail($"{id} {cat}: no variants");
                else if (n != 10) sb.AppendLine($"  info  {id} {cat}: {n} variants");
            var dupes = c.SharedData.Entries.GroupBy(e => e.Key).Where(g => g.Count() > 1).ToList();
            foreach (var d in dupes) Fail($"duplicate key {d.Key}");
        }
        if (keys == 360 && texts == 720) Pass("360 keys / 720 texts");
        else sb.AppendLine($"  info  {keys} keys / {texts} texts (initial content was 360 / 720)");
        if (reference != null)
        {
            if (refChecked == reference.Count) Pass($"all {refChecked} reference texts identical");
            else Fail($"only {refChecked}/{reference.Count} reference texts found");
        }

        ok = fails == 0;
        sb.AppendLine(ok ? "  RESULT PASS" : $"  RESULT FAIL ({fails})");
        return sb.ToString();
    }

    [Serializable] private class RefLine { public string key, ca, en; }
    [Serializable] private class RefList { public RefLine[] items; }

    private static Dictionary<(string key, string code), string> LoadReference(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var list = JsonUtility.FromJson<RefList>("{\"items\":" + File.ReadAllText(path, Encoding.UTF8) + "}");
        var map = new Dictionary<(string, string), string>();
        foreach (var l in list.items) { map[(l.key, "ca")] = l.ca; map[(l.key, "en")] = l.en; }
        return map;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    public static string CommandLineArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return null;
    }

    private static void WriteReport(string text)
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/ComposerDialogues.txt", text, Encoding.UTF8);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
