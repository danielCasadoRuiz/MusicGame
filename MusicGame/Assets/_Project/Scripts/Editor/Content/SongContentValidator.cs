#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MusicGame.Audio.Analysis;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// PRE-BUILD gate of the published song catalog (P3). Run by Tools > MusicGame > Content > Songs > Validate,
/// by ContentUpdateTools before any release / update content build, by SongContentBuild, and before any
/// player build (IPreprocessBuildWithReport). Errors block the build. Per playable song:
///   - a published profile exists, parses, CRC ok, is for this songId, carries style tags;
///   - format / pipeline / params / tag model = this project's current versions;
///   - master identity = the CURRENT master bytes (+ normalisation policy);
///   - playback revision = the CURRENT import settings of the played clip for the build target;
///   - identity time conversion (a measured zero lag) — no unapplied offsets;
///   - an index entry with exactly these revisions (index ↔ profile ↔ audio from one bake).
/// Addressables: Remote_Songs is remote (build + load paths), updatable (not static), PackSeparately; audio,
/// profile and index entries sit there with the expected addresses and labels; no audio/profile/index entry in
/// any other group. Player: no build scene and nothing under a Resources folder depends on a master, profile
/// or the index (they would be copied into the player).
/// Batch: -executeMethod SongContentValidator.ValidateFromCommandLine [-validateReport path]
/// </summary>
public static class SongContentValidator
{
    [MenuItem("Tools/MusicGame/Content/Songs/Validate Published Song Content")]
    public static void ValidateMenu()
    {
        string report = Validate(EditorUserBuildSettings.activeBuildTarget, out int errors);
        if (errors > 0) Debug.LogError(report); else Debug.Log(report);
    }

    public static void ValidateFromCommandLine()
    {
        string report = Validate(EditorUserBuildSettings.activeBuildTarget, out int errors);
        string outPath = ContentUpdateTools.Arg("-validateReport") ?? "Logs/SongContentValidator.txt";
        File.WriteAllText(outPath, report, Encoding.UTF8);
        Debug.Log(report);
        EditorApplication.Exit(errors > 0 ? 1 : 0);
    }

    /// <summary>Null when the published profile at `profilePath` is current for this master + import settings.</summary>
    public static string CheckProfileAgainstSource(string songId, string assetPath, string profilePath, in AppAnalysisVersions app,
                                                   BuildTarget target, out ProfileHeader header, out byte[] bytes)
    {
        header = null; bytes = null;
        if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath)) return "master audio missing";
        if (!File.Exists(profilePath)) return "no published profile (run the bake)";
        bytes = File.ReadAllBytes(profilePath);
        try { header = ProfileBinary.ReadHeader(bytes, out int hs); if (Crc32.Compute(bytes, hs, (int)header.bodyLength) != header.bodyCrc32) return "profile CRC mismatch (corrupt)"; }
        catch (Exception e) { return "unreadable profile: " + e.Message; }
        if (header.formatVersion != app.FormatVersion) return $"format {header.formatVersion} ≠ {app.FormatVersion}";
        if (header.pipelineVersion != app.PipelineVersion) return $"pipeline {header.pipelineVersion} ≠ {app.PipelineVersion}";
        if (header.paramsHash != app.ParamsHash) return $"analysis params {header.paramsHash:x16} ≠ {app.ParamsHash:x16}";
        if (!header.HasTags || header.tagModelVersion != app.TagModelVersion) return $"style tags missing or from tag model {header.tagModelVersion}";
        if (header.songId != songId) return $"profile is for '{header.songId}'";
        var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
        if (importer == null) return "master is not an audio asset";
        var master = MasterAudio.Identity(songId, assetPath, MasterAudio.ImportNormalizesLevel(importer));
        if (header.audioIdentity != master.Hash) return $"master changed since the bake ({header.audioIdentity:x16} ≠ {master.Hash:x16})";
        ulong playback = MasterAudio.PlaybackRevision(master.Hash, importer, target);
        if (header.playbackRevision != playback) return $"playback import settings changed since the bake for {target} ({header.playbackRevision:x16} ≠ {playback:x16})";
        if (header.timeConversion != ProfileTimeConversion.Identity) return "non-identity time conversion";
        return null;
    }

    /// <summary>Platforms the catalog is published for (mobile + desktop).</summary>
    public static readonly BuildTarget[] PublishTargets = { BuildTarget.StandaloneWindows64, BuildTarget.Android, BuildTarget.iOS };

    public static string Validate(BuildTarget target, out int errors)
    {
        var sb = new StringBuilder($"[SongContentValidator] target {target}\n");
        int err = 0;
        void Error(string m) { err++; sb.AppendLine("  ERROR " + m); }
        void Ok(string m) => sb.AppendLine("  ok    " + m);

        var appSo = Resources.Load<AppConfigSO>("AppConfig");
        var catalog = appSo != null ? appSo.songCatalog : null;
        if (appSo == null || appSo.audioAnalysis == null || catalog == null) { errors = 1; return sb.AppendLine("  ERROR AppConfig.audioAnalysis / songCatalog missing").ToString(); }
        var app = AppAnalysisVersions.Current(appSo.audioAnalysis);
        var index = AssetDatabase.LoadAssetAtPath<SongCatalogIndexSO>(SongContentKeys.IndexAssetPath);
        if (index == null) Error($"no published index at {SongContentKeys.IndexAssetPath} (run the bake)");
        var indexById = index != null ? index.songs.Where(e => e != null && e.songId != null).GroupBy(e => e.songId).ToDictionary(g => g.Key, g => g.First()) : new Dictionary<string, SongCatalogIndexEntry>();

        // ── Profiles vs masters vs index ──
        var songs = catalog.songs.Where(e => e != null && !string.IsNullOrEmpty(e.songId)).ToList();
        var masterPaths = new HashSet<string>();
        int okSongs = 0;
        foreach (var e in songs)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(e.editorGuid);
            masterPaths.Add(assetPath);
            string problem = CheckProfileAgainstSource(e.songId, assetPath, SongContentKeys.ProfileAssetPath(e.songId), app, target, out var h, out _);
            if (problem != null) { Error($"{e.songId} '{e.address}': {problem}"); continue; }
            // One published profile serves every platform: the decoded samples must be the same everywhere
            // (no per-platform override that changes format / quality / rate / mono / normalisation).
            var importer = (AudioImporter)AssetImporter.GetAtPath(assetPath);
            var otherTargets = PublishTargets.Where(t => MasterAudio.PlaybackRevision(h.audioIdentity, importer, t) != h.playbackRevision).ToList();
            if (otherTargets.Count > 0) { Error($"{e.songId}: import settings for {string.Join(", ", otherTargets)} change the decoded audio (platform override) — one profile cannot serve them"); continue; }
            if (MasterAudio.EffectiveSettings(importer, target).loadType == AudioClipLoadType.DecompressOnLoad)
                sb.AppendLine($"  warn  {e.songId}: DecompressOnLoad (keeps the whole PCM resident) — run Setup Remote Song Content");
            if (!indexById.TryGetValue(e.songId, out var ie)) { Error($"{e.songId}: missing from the published index"); continue; }
            if (ie.masterIdentity != h.audioIdentity || ie.playbackRevision != h.playbackRevision || ie.profileCrc != h.bodyCrc32 ||
                ie.playbackSamples != h.playbackSamples || ie.playbackFrequency != h.playbackFrequency || ie.playbackChannels != h.playbackChannels)
            { Error($"{e.songId}: index entry revisions differ from its profile (re-run the bake)"); continue; }
            if (ie.audioAddress != SongContentKeys.AudioAddress(e.songId) || ie.profileAddress != SongContentKeys.ProfileAddress(e.songId) || ie.contentLabel != SongContentKeys.ContentLabel(e.songId))
            { Error($"{e.songId}: index addressing differs ('{ie.audioAddress}' / '{ie.profileAddress}' / '{ie.contentLabel}')"); continue; }
            if (ie.similarity == null || !ie.similarity.IsValid) { Error($"{e.songId}: no valid similarity vector in the index"); continue; }
            okSongs++;
        }
        foreach (var id in indexById.Keys.Where(id => !songs.Any(s => s.songId == id))) Error($"index lists '{id}', which is not a playable catalog song");
        if (index != null && index.contentRevision != SongCatalogBake.ContentRevision(index.songs)) Error("index contentRevision is stale (re-run the bake)");
        Ok($"{okSongs}/{songs.Count} songs: profile current for master + playback revision + versions, index consistent");

        // ── Addressables layout ──
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings != null ? settings.FindGroup(SongContentKeys.GroupName) : null;
        if (group == null) Error($"no '{SongContentKeys.GroupName}' Addressables group (run Tools > MusicGame > Content > Songs > Setup Remote Song Content)");
        else
        {
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            var cu = group.GetSchema<ContentUpdateGroupSchema>();
            if (schema == null) Error("Remote_Songs has no BundledAssetGroupSchema");
            else
            {
                if (schema.BuildPath.GetName(settings) != AddressableAssetSettings.kRemoteBuildPath || schema.LoadPath.GetName(settings) != AddressableAssetSettings.kRemoteLoadPath)
                    Error($"Remote_Songs paths are {schema.BuildPath.GetName(settings)} / {schema.LoadPath.GetName(settings)} — must be Remote");
                if (schema.BundleMode != BundledAssetGroupSchema.BundlePackingMode.PackSeparately) Error($"Remote_Songs bundle mode {schema.BundleMode} — must be PackSeparately (one bundle per audio / profile / index)");
                if (!schema.IncludeInBuild) Error("Remote_Songs is excluded from the build");
            }
            if (cu != null && cu.StaticContent) Error("Remote_Songs is static ('Cannot Change Post Release') — songs must be updatable");

            var byGuid = group.entries.ToDictionary(x => x.guid, x => x);
            foreach (var e in songs)
            {
                string label = SongContentKeys.ContentLabel(e.songId);
                if (!byGuid.TryGetValue(e.editorGuid, out var audio)) { Error($"{e.songId}: audio not in Remote_Songs"); continue; }
                if (audio.address != SongContentKeys.AudioAddress(e.songId) || !audio.labels.Contains(SongContentKeys.SongLabel) || !audio.labels.Contains(label))
                    Error($"{e.songId}: audio entry address/labels wrong ('{audio.address}', [{string.Join(",", audio.labels)}])");
                string pguid = AssetDatabase.AssetPathToGUID(SongContentKeys.ProfileAssetPath(e.songId));
                if (string.IsNullOrEmpty(pguid) || !byGuid.TryGetValue(pguid, out var prof)) { Error($"{e.songId}: profile not in Remote_Songs"); continue; }
                if (prof.address != SongContentKeys.ProfileAddress(e.songId) || !prof.labels.Contains(label) || prof.labels.Contains(SongContentKeys.SongLabel))
                    Error($"{e.songId}: profile entry address/labels wrong ('{prof.address}', [{string.Join(",", prof.labels)}])");
            }
            string iguid = AssetDatabase.AssetPathToGUID(SongContentKeys.IndexAssetPath);
            if (string.IsNullOrEmpty(iguid) || !byGuid.TryGetValue(iguid, out var idx) || idx.address != SongContentKeys.IndexAddress || !idx.labels.Contains(SongContentKeys.IndexLabel))
                Error("index entry missing from Remote_Songs or wrong address/label");
            foreach (var en in group.entries.Where(x => !SongContentKeys.IsSafeAddress(x.address)))
                Error($"'{en.address}' is not a printable-ASCII address — the binary catalog would corrupt it");
            Ok($"Remote_Songs: {group.entries.Count} entries, remote, updatable, PackSeparately, ASCII addresses");

            // Nothing of the catalog content in another group.
            var contentGuids = new HashSet<string>(songs.Select(s => s.editorGuid));
            foreach (var s in songs) contentGuids.Add(AssetDatabase.AssetPathToGUID(SongContentKeys.ProfileAssetPath(s.songId)));
            contentGuids.Add(iguid);
            foreach (var g in settings.groups.Where(g => g != null && g != group))
                foreach (var en in g.entries)
                    if (contentGuids.Contains(en.guid) || PlayableSongScope.IsPlayablePath(en.AssetPath))
                        Error($"'{en.AssetPath}' is in group '{g.Name}' (catalog content must only be in Remote_Songs)");
        }

        // ── Nothing reaches the player ──
        var roots = new List<string>();
        roots.AddRange(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path));
        roots.AddRange(AssetDatabase.FindAssets("", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                                    .Where(p => p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)));
        var forbidden = new HashSet<string>(masterPaths) { SongContentKeys.IndexAssetPath };
        foreach (var s in songs) forbidden.Add(SongContentKeys.ProfileAssetPath(s.songId));
        var leaks = AssetDatabase.GetDependencies(roots.ToArray(), true).Where(forbidden.Contains).ToList();
        foreach (var l in leaks) Error($"'{l}' is referenced by a build scene or a Resources asset — it would ship inside the player");
        if (leaks.Count == 0) Ok($"player: {roots.Count} build scenes / Resources assets reference no master, profile or index");

        sb.AppendLine(err == 0 ? "  PASS  published song content is complete and consistent" : $"  FAIL  {err} error(s) — content must not be built");
        errors = err;
        return sb.ToString();
    }
}

/// <summary>Blocks a PLAYER build whose catalog content is incomplete/incompatible (the Addressables content
/// builds run the same validation from ContentUpdateTools / SongContentBuild).</summary>
public sealed class SongContentPreBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        string text = SongContentValidator.Validate(report.summary.platform, out int errors);
        if (errors > 0) throw new BuildFailedException("Published song content is not valid:\n" + text);
        Debug.Log(text);
    }
}
#endif
