#if UNITY_EDITOR
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

/// <summary>
/// Release / content-update workflow for Addressables (Tools > MusicGame > Content > Release):
///
///   Build Release Content      full Addressables build for the app version being SHIPPED (run it right
///                              before building that player). Archives its addressables_content_state.bin
///                              under ContentState/&lt;platform&gt;/&lt;version&gt;/ — keep that file (it is
///                              committed) for as long as that app version is supported.
///   Build Content Update       for an app ALREADY published: compares the project with that version's
///                              archived state, moves only the changed shipped entries (+ the entries
///                              that depend on them) into a new remote "Content Update" group (one
///                              bundle per asset) and builds the new remote catalog + bundles into
///                              ServerData/&lt;platform&gt;. Upload that folder; the installed app picks it
///                              up at its next catalog check without a store release.
///
/// Both use the ACTIVE Addressables profile unless -profile is given (batch). The production profile
/// must point Remote.LoadPath at the real server (see ContentDeliverySetup / docs).
///
/// Batch:
///   -executeMethod ContentUpdateTools.BuildReleaseContentFromCommandLine [-profile X] [-contentVersion V] [-stateArchive dir]
///   -executeMethod ContentUpdateTools.BuildContentUpdateFromCommandLine  [-profile X] [-contentVersion V] [-stateArchive dir]
/// </summary>
public static class ContentUpdateTools
{
    public const string DefaultArchiveRoot = "ContentState";
    public const string UpdateGroupPrefix  = "Content Update";

    [MenuItem("Tools/MusicGame/Content/Release/Build Release Content (archive state)")]
    public static void BuildReleaseMenu() => Debug.Log(BuildReleaseContent(null, null, out _));

    [MenuItem("Tools/MusicGame/Content/Release/Build Content Update (for current version)")]
    public static void BuildUpdateMenu() => Debug.Log(BuildContentUpdate(null, null, out _));

    public static void BuildReleaseContentFromCommandLine()
    {
        string report = WithProfile(Arg("-profile"), () => BuildReleaseContent(Arg("-contentVersion"), Arg("-stateArchive"), out bool ok) + (ok ? "" : "\nFAILED"));
        Finish(report);
    }

    public static void BuildContentUpdateFromCommandLine()
    {
        string report = WithProfile(Arg("-profile"), () => BuildContentUpdate(Arg("-contentVersion"), Arg("-stateArchive"), out bool ok) + (ok ? "" : "\nFAILED"));
        Finish(report);
    }

    public static string ArchivedStatePath(string version, string archiveRoot = null) =>
        Path.Combine(archiveRoot ?? DefaultArchiveRoot, PlatformFolder(), version ?? PlayerSettings.bundleVersion, "addressables_content_state.bin").Replace('\\', '/');

    private static string PlatformFolder() => EditorUserBuildSettings.activeBuildTarget.ToString();

    // ── Release ──────────────────────────────────────────────────────────────────

    public static string BuildReleaseContent(string version, string archiveRoot, out bool ok)
    {
        var sb = new StringBuilder("[ContentUpdateTools] RELEASE CONTENT\n");
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        version ??= PlayerSettings.bundleVersion;
        sb.AppendLine($"  profile {settings.profileSettings.GetProfileName(settings.activeProfileId)}, player version {PlayerSettings.bundleVersion}, target {PlatformFolder()}");
        if (!SongContentGate(sb)) { ok = false; return sb.ToString(); }

        AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
        ok = string.IsNullOrEmpty(result.Error);
        if (!ok) return sb.AppendLine("  FAIL  " + result.Error).ToString();
        sb.AppendLine($"  ok    built in {result.Duration:0.0}s ({result.AssetBundleBuildResults?.Count ?? 0} bundles)");

        string state = ContentUpdateScript.GetContentStateDataPath(false, settings);
        string archive = ArchivedStatePath(version, archiveRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(archive));
        File.Copy(state, archive, true);
        sb.AppendLine($"  ok    content state archived: {archive}  (keep it while app {version} is supported)");
        string serverData = settings.RemoteCatalogBuildPath.GetValue(settings);
        sb.AppendLine($"  ok    remote catalog → {serverData} (upload it with the release)");
        return sb.ToString();
    }

    // ── Content update ───────────────────────────────────────────────────────────

    public static string BuildContentUpdate(string version, string archiveRoot, out bool ok)
    {
        var sb = new StringBuilder("[ContentUpdateTools] CONTENT UPDATE\n");
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        version ??= PlayerSettings.bundleVersion;
        string state = ArchivedStatePath(version, archiveRoot);
        if (!File.Exists(state)) state = ContentUpdateScript.GetContentStateDataPath(false, settings);
        ok = File.Exists(state);
        if (!ok) return sb.AppendLine($"  FAIL  no content state for {version} ({state}) — the release was not built with Build Release Content").ToString();
        sb.AppendLine($"  state {state}");
        if (!SongContentGate(sb)) { ok = false; return sb.ToString(); }

        // Shipped ("Cannot Change Post Release") entries that changed since the release + dependants.
        var modified = ContentUpdateScript.GatherModifiedEntriesWithDependencies(settings, state);
        var entries = new HashSet<AddressableAssetEntry>();
        foreach (var kv in modified) { entries.Add(kv.Key); foreach (var d in kv.Value) entries.Add(d); }
        entries.RemoveWhere(e => e == null || e.parentGroup == null || IsUpdateGroup(e.parentGroup));
        foreach (var e in entries.OrderBy(e => e.address)) sb.AppendLine($"  moved {e.address}  ({e.parentGroup.Name})");

        if (entries.Count > 0)
        {
            var group = settings.CreateGroup($"{UpdateGroupPrefix} {version}", false, false, true, null,
                                             typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately; // one changed table = one download
            group.GetSchema<ContentUpdateGroupSchema>().StaticContent = false;
            settings.MoveEntries(entries.ToList(), group, true);
            sb.AppendLine($"  ok    {entries.Count} entries → {group.Name} (remote)");
        }
        else sb.AppendLine("  info  no shipped entry changed (remote groups may still have changes)");

        var result = ContentUpdateScript.BuildContentUpdate(settings, state);
        ok = result != null && string.IsNullOrEmpty(result.Error);
        if (!ok) return sb.AppendLine("  FAIL  " + (result?.Error ?? "no result")).ToString();
        sb.AppendLine($"  ok    update built → {settings.RemoteCatalogBuildPath.GetValue(settings)} (upload the whole folder)");
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    /// <summary>Published song catalog must be complete and consistent before ANY content build.</summary>
    private static bool SongContentGate(StringBuilder sb)
    {
        string report = SongContentValidator.Validate(EditorUserBuildSettings.activeBuildTarget, out int errors);
        if (errors == 0) { sb.AppendLine("  ok    published song content validated"); return true; }
        sb.Append(report).AppendLine("  FAIL  published song content invalid — nothing built");
        return false;
    }

    public static bool IsUpdateGroup(AddressableAssetGroup g) => g != null && g.Name.StartsWith(UpdateGroupPrefix);

    // ── Helpers ──────────────────────────────────────────────────────────────────

    public static string WithProfile(string profileName, Func<string> action)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        string previous = settings.activeProfileId;
        if (!string.IsNullOrEmpty(profileName))
        {
            string id = settings.profileSettings.GetProfileId(profileName);
            if (string.IsNullOrEmpty(id)) return $"FAIL  unknown Addressables profile '{profileName}'";
            settings.activeProfileId = id;
        }
        try { return action(); }
        finally { settings.activeProfileId = previous; EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets(); }
    }

    public static string Arg(string name) => ComposerDialogueContentSetup.CommandLineArg(name);

    private static void Finish(string report)
    {
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.AppendAllText("Logs/ContentUpdateTools.txt", report + "\n", Encoding.UTF8);
        EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }
}
#endif
