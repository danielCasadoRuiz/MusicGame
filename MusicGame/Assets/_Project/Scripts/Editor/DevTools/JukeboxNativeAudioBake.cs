#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>Native players cannot read Unity AudioClip bundles. Publish independent AAC files on demand,
/// one small remote bundle per song, leaving the runner's audio/profile representation untouched.</summary>
public static class JukeboxNativeAudioBake
{
    public const string CatalogPath = "Assets/_Project/Resources/JukeboxNativeCatalog.asset";
    private const string Folder = "Assets/_Project/Content/JukeboxAudio";
    [MenuItem("Tools/MusicGame/Content/Songs/Bake Jukebox Background Audio")]
    public static void Bake()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (app?.songCatalog == null || settings == null) throw new Exception("Missing song catalog / Addressables settings");
        TpsContentProtection.Setup();
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var catalog = AssetDatabase.LoadAssetAtPath<JukeboxNativeCatalogSO>(CatalogPath);
        if (catalog == null) { catalog = ScriptableObject.CreateInstance<JukeboxNativeCatalogSO>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
        var group = settings.FindGroup("Remote_JukeboxAudio") ?? settings.CreateGroup("Remote_JukeboxAudio", false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        var schema = group.GetSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.Uncompressed;
        group.GetSchema<ContentUpdateGroupSchema>().StaticContent = false;
        var log = new StringBuilder();
        foreach (var song in app.songCatalog.songs.Where(s => s != null && !string.IsNullOrEmpty(s.songId)))
        {
            string source = AssetDatabase.GUIDToAssetPath(song.editorGuid);
            if (!File.Exists(source)) throw new FileNotFoundException(source);
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(source);
            string revision = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").Substring(0, 20).ToLowerInvariant() + "_aac160_tps1";
            string output = Folder + "/" + song.songId + ".tps.bytes";
            var entry = catalog.Find(song.songId);
            if (entry == null || entry.revision != revision || !File.Exists(output))
            {
                string temp = Path.GetFullPath(output + ".tmp.m4a");
                var start = new ProcessStartInfo("ffmpeg", "-hide_banner -loglevel error -y -i \"" + Path.GetFullPath(source) + "\" -vn -c:a aac -b:a 160k -movflags +faststart \"" + temp + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                using var process = Process.Start(start);
                string error = process.StandardError.ReadToEnd(); process.WaitForExit();
                if (process.ExitCode != 0) throw new Exception("Native audio encode failed: " + error);
                try
                {
                    byte[] clear = File.ReadAllBytes(temp);
                    try { File.WriteAllBytes(output, TpsContentProtection.Seal(clear, TpsCrypto.Aac, "native/" + song.songId)); }
                    finally { Array.Clear(clear, 0, clear.Length); }
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                string legacy = Folder + "/" + song.songId + ".m4a.bytes";
                if (File.Exists(legacy))
                {
                    // Preserve the original Addressables GUID while replacing the generated payload.
                    File.Delete(legacy);
                    if (File.Exists(legacy + ".meta") && !File.Exists(output + ".meta")) File.Move(legacy + ".meta", output + ".meta");
                }
                AssetDatabase.ImportAsset(output);
            }
            if (entry == null) { entry = new JukeboxNativeCatalogSO.Entry { songId = song.songId }; catalog.songs.Add(entry); }
            entry.address = "JukeboxAudio/" + song.songId; entry.revision = revision;
            var asset = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(output), group);
            asset.address = entry.address;
            var index = AssetDatabase.LoadAssetAtPath<SongCatalogIndexSO>(SongContentKeys.IndexAssetPath);
            var published = index?.songs.Find(e => e.songId == song.songId);
            if (published != null) { published.nativeAudioAddress = entry.address; published.nativeAudioRevision = entry.revision; EditorUtility.SetDirty(index); }
            log.AppendLine(song.songId + " " + revision);
        }
        EditorUtility.SetDirty(catalog); EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/JukeboxNativeAudioBake.txt", log.ToString());
        UnityEngine.Debug.Log("[Jukebox] Native audio prepared for " + catalog.songs.Count + " songs. Publish Addressables content before device use.");
    }
}
#endif
