#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// Tools > MusicGame > Content > Songs > Setup Remote Song Content — idempotent. Migrates the old LOCAL,
/// PackTogether, static "Songs" group (one 263 MB bundle shipped inside the player) to the remote layout of
/// SongContentKeys:
///   group Remote_Songs: Remote build/load paths, PackSeparately (one bundle per audio clip, per profile, and the
///   index), LZ4, updatable (not static — ContentDeliverySetup leaves "Remote_*" groups alone);
///   audio   — address unchanged, labels "Song" + "Song_&lt;id&gt;";
///   profile — address "SongProfile/&lt;id&gt;", label "Song_&lt;id&gt;" (a song's download key covers both bundles);
///   index   — address "SongCatalogIndex", label "SongIndex".
/// Playback import policy of every playable song (measured in P3 by AudioImportComparison, see the P3 report):
/// Vorbis quality 1.0 (unchanged: the encoded data and its alignment to the master stay identical) loaded
/// CompressedInMemory in the background — ~3.5× less resident memory than DecompressOnLoad and no multi-second
/// decode on load; Streaming was rejected (10–24 ms main-thread seek calls). The load type is NOT part of the
/// playback revision (it does not change the decoded samples), so applying it never invalidates a profile.
/// Also adds the new UI strings of the readiness barrier to UIText (missing keys only, never overwriting).
/// Run the bake first so the profile/index assets exist (missing ones are reported, not invented).
/// Batch: -executeMethod RemoteSongContentSetup.SetupFromCommandLine
/// </summary>
public static class RemoteSongContentSetup
{
    private const string LegacyGroupName = "Songs";

    private static readonly string[] UiKeys =
    {
        "SongSelection.DownloadFailed", "SongSelection.ContentIncompatible",
        "Preparing.Downloading", "Preparing.Loading", "Preparing.Preparing",
    };

    [MenuItem("Tools/MusicGame/Content/Songs/Setup Remote Song Content")]
    public static void SetupMenu() => Debug.Log(Setup());

    public static void SetupFromCommandLine()
    {
        string report = Setup();
        File.WriteAllText("Logs/RemoteSongContentSetup.txt", report, Encoding.UTF8);
        Debug.Log(report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Setup()
    {
        var sb = new StringBuilder("[RemoteSongContentSetup]\n");
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var catalog = app != null ? app.songCatalog : null;
        if (settings == null || catalog == null) return sb.AppendLine("  ERROR no Addressables settings / AppConfig.songCatalog").ToString();

        var group = EnsureGroup(settings, sb);
        settings.AddLabel(SongContentKeys.SongLabel);
        settings.AddLabel(SongContentKeys.IndexLabel);

        int audio = 0, profiles = 0, reimported = 0;
        foreach (var e in catalog.songs.Where(x => x != null && !string.IsNullOrEmpty(x.songId)))
        {
            string label = SongContentKeys.ContentLabel(e.songId);
            settings.AddLabel(label);
            var a = settings.CreateOrMoveEntry(e.editorGuid, group, false, false);
            if (a == null) { sb.AppendLine($"  ERROR {e.songId}: audio GUID {e.editorGuid} not found"); continue; }
            if (ApplyPlaybackImportPolicy(AssetDatabase.GUIDToAssetPath(e.editorGuid))) reimported++;
            a.address = SongContentKeys.AudioAddress(e.songId); // ASCII key; e.address stays the display name
            a.SetLabel(SongContentKeys.SongLabel, true, false, false);
            a.SetLabel(label, true, false, false);
            audio++;

            string profilePath = SongContentKeys.ProfileAssetPath(e.songId);
            string pguid = AssetDatabase.AssetPathToGUID(profilePath);
            if (string.IsNullOrEmpty(pguid)) { sb.AppendLine($"  WARN  {e.songId}: no published profile yet ({profilePath}) — run the bake, then this setup again"); continue; }
            var p = settings.CreateOrMoveEntry(pguid, group, false, false);
            p.address = SongContentKeys.ProfileAddress(e.songId);
            p.SetLabel(SongContentKeys.SongLabel, false, false, false); // never listed as a song (AudioClip-only listing anyway)
            p.SetLabel(label, true, false, false);
            profiles++;
        }
        string iguid = AssetDatabase.AssetPathToGUID(SongContentKeys.IndexAssetPath);
        if (!string.IsNullOrEmpty(iguid))
        {
            var i = settings.CreateOrMoveEntry(iguid, group, false, false);
            i.address = SongContentKeys.IndexAddress;
            i.SetLabel(SongContentKeys.IndexLabel, true, false, false);
            sb.AppendLine("  PASS  index entry");
        }
        else sb.AppendLine($"  WARN  no index yet ({SongContentKeys.IndexAssetPath}) — run the bake");

        RegisterLyrics(settings);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
        AssetDatabase.SaveAssets();
        sb.AppendLine($"  PASS  {audio} audio + {profiles} profile entries in {SongContentKeys.GroupName}; playback import policy applied to {reimported} clip(s)");

        int added = LocalizationSetup.AddMissingEntries(UiKeys);
        sb.AppendLine($"  PASS  UIText: {added} missing entr(y/ies) added");
        return sb.ToString();
    }

    [MenuItem("Tools/MusicGame/Content/Songs/Import Jukebox Lyrics JSON")]
    private static void ImportLyrics()
    {
        string source = EditorUtility.OpenFilePanel("Import Jukebox lyrics", "../AssetsMaterial/Lyrics", "json");
        if (string.IsNullOrEmpty(source)) return;
        try
        {
            string destination = SaveLyrics(source, false);
            if (destination == null) return;
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(destination);
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("Jukebox lyrics import failed", e.Message, "OK");
        }
    }

    public static void ImportLyricsFromCommandLine()
    {
        var args = System.Environment.GetCommandLineArgs();
        int index = System.Array.IndexOf(args, "-lyricsJson");
        try
        {
            if (index < 0 || index + 1 >= args.Length)
                throw new System.ArgumentException("Missing -lyricsJson.");
            string path = SaveLyrics(args[index + 1], true);
            Debug.Log("[Jukebox] Imported lyrics to " + path);
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    private static string SaveLyrics(string source, bool batch)
    {
        string json = File.ReadAllText(source);
        string id = JsonUtility.FromJson<SongLyrics>(json)?.songId;
        var catalog = Resources.Load<AppConfigSO>("AppConfig")?.songCatalog;
        if (catalog == null || string.IsNullOrEmpty(id) ||
            !catalog.songs.Any(song => song != null && song.songId == id))
            throw new System.FormatException("The JSON must identify an original song in the playable catalog.");

        var lyrics = SongLyrics.Parse(json, id);
        if (lyrics.textSource == "whisper_draft" || lyrics.lines.Length == 0)
            throw new System.FormatException("Import a finalized lyrics JSON with at least one line.");

        Directory.CreateDirectory(SongContentKeys.LyricsFolder);
        string destination = $"{SongContentKeys.LyricsFolder}/{id}.json";
        if (File.Exists(destination) && !batch && !EditorUtility.DisplayDialog("Replace Jukebox lyrics?",
                $"Replace the lyrics for {id}?", "Replace", "Cancel")) return null;

        File.WriteAllText(destination, json, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
        TpsContentProtection.Setup();
        return destination;
    }

    internal static void RegisterLyrics(AddressableAssetSettings settings)
    {
        if (!AssetDatabase.IsValidFolder(SongContentKeys.LyricsFolder)) return;
        var catalog = Resources.Load<AppConfigSO>("AppConfig")?.songCatalog;
        if (catalog == null) return;

        var group = settings.FindGroup(SongContentKeys.GroupName);
        foreach (var song in catalog.songs)
        {
            if (song == null || string.IsNullOrEmpty(song.songId)) continue;
            string path = $"{SongContentKeys.LyricsFolder}/{song.songId}.json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) continue;
            SongLyrics.Parse(asset.text, song.songId);
            string guid = AssetDatabase.AssetPathToGUID(path);
            group ??= EnsureGroup(settings, null);
            var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, group);
            entry.address = SongContentKeys.LyricsAddress(song.songId);
            string label = SongContentKeys.ContentLabel(song.songId);
            settings.AddLabel(label);
            entry.SetLabel(label, true);
        }
    }

    public const AudioCompressionFormat PlaybackFormat = AudioCompressionFormat.Vorbis;
    public const float PlaybackQuality = 1f;
    public const AudioClipLoadType PlaybackLoadType = AudioClipLoadType.CompressedInMemory;

    /// <summary>Applies the playback import policy (default settings) to one master; true when it reimported.
    /// Platform overrides are left alone and reported by SongContentValidator when they change the samples.</summary>
    public static bool ApplyPlaybackImportPolicy(string assetPath)
    {
        if (!(AssetImporter.GetAtPath(assetPath) is AudioImporter importer)) return false;
        var s = importer.defaultSampleSettings;
        bool changed = s.compressionFormat != PlaybackFormat || !Mathf.Approximately(s.quality, PlaybackQuality)
                       || s.loadType != PlaybackLoadType || s.preloadAudioData || !importer.loadInBackground;
        if (!changed) return false;
        s.compressionFormat = PlaybackFormat;
        s.quality = PlaybackQuality;
        s.loadType = PlaybackLoadType;
        s.preloadAudioData = false; // data loaded by the readiness barrier (CatalogSongLoader), never as a side effect
        importer.defaultSampleSettings = s;
        importer.loadInBackground = true; // LoadAudioData never stalls the main thread; the barrier waits for Loaded
        importer.SaveAndReimport();
        return true;
    }

    public static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings, StringBuilder sb)
    {
        var group = settings.FindGroup(SongContentKeys.GroupName);
        if (group == null)
        {
            group = settings.FindGroup(LegacyGroupName);
            if (group != null) { group.Name = SongContentKeys.GroupName; sb?.AppendLine($"  PASS  migrated group '{LegacyGroupName}' → '{SongContentKeys.GroupName}'"); }
            else group = settings.CreateGroup(SongContentKeys.GroupName, false, false, true, null, typeof(ContentUpdateGroupSchema), typeof(BundledAssetGroupSchema));
        }
        var schema = group.GetSchema<BundledAssetGroupSchema>() ?? group.AddSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
        schema.IncludeInBuild = true;
        schema.UseAssetBundleCache = true;
        schema.UseAssetBundleCrc = true;
        EditorUtility.SetDirty(schema);
        var cu = group.GetSchema<ContentUpdateGroupSchema>() ?? group.AddSchema<ContentUpdateGroupSchema>();
        cu.StaticContent = false;
        EditorUtility.SetDirty(cu);
        sb?.AppendLine($"  PASS  {SongContentKeys.GroupName}: remote paths, PackSeparately, LZ4, updatable");
        return group;
    }
}
#endif
