#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.ResourceManagement.Util;

/// <summary>Protects the original playable catalog and its native background representation.
/// Original WAVs remain editor working sources. Standard AudioClip payloads are preserved inside
/// authenticated encrypted bundles, avoiding a lossy re-encode or changing analysis/time alignment.</summary>
[InitializeOnLoad]
public sealed class TpsContentProtection : IPreprocessBuildWithReport
{
    public const string KeyringPath = "Assets/_Project/Resources/TpsKeyring.bytes";
    public int callbackOrder => -900;
    static TpsContentProtection()
    {
        EditorApplication.delayCall += Hook;
    }
    private static void Hook()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
        {
            settings.OnDataBuilderComplete -= ProtectBuild;
            settings.OnDataBuilderComplete += ProtectBuild;
        }
    }
    public void OnPreprocessBuild(BuildReport report) { Setup(); AuditPlayerDependencies(); }
    public static bool ProtectedGroup(AddressableAssetGroup group) => group != null &&
        (group.Name == SongContentKeys.GroupName || group.Name == "Remote_JukeboxAudio" ||
         group.entries.Any(e => e.address.StartsWith(SongContentKeys.LyricsPrefix, StringComparison.Ordinal) || e.address.StartsWith("SongAudio/", StringComparison.Ordinal) || e.address.StartsWith("JukeboxAudio/", StringComparison.Ordinal)));

    [MenuItem("Tools/MusicGame/Content/Songs/Setup TPS Encryption")]
    public static void Setup()
    {
        EnsureRoot(); Hook();
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new InvalidOperationException("Addressables settings missing.");
        RemoteSongContentSetup.RegisterLyrics(settings);
        foreach (var group in settings.groups.Where(ProtectedGroup))
        {
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            schema.AssetBundleProviderType = new SerializedType { Value = typeof(TpsBundleProvider) };
            EditorUtility.SetDirty(schema);
        }
        EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
    }
    public static byte[] EnsureRoot()
    {
        if (!File.Exists(KeyringPath))
        {
            if ((Directory.Exists("Assets/_Project/Content/JukeboxAudio") && Directory.EnumerateFiles("Assets/_Project/Content/JukeboxAudio", "*.tps.bytes").Any()) ||
                (Directory.Exists("ServerData") && Directory.EnumerateFiles("ServerData", "*.tps", SearchOption.AllDirectories).Any()))
                throw new CryptographicException("TPS content already exists but its offline root is missing. Restore the existing keyring; do not generate replacement keys.");
            var data = new TpsKeyring.Data { root = Convert.ToBase64String(TpsCrypto.RandomBytes(32)) };
            File.WriteAllText(KeyringPath, JsonUtility.ToJson(data, true));
            AssetDatabase.ImportAsset(KeyringPath); TpsKeyring.Reset();
        }
        var stored = JsonUtility.FromJson<TpsKeyring.Data>(File.ReadAllText(KeyringPath));
        var root = Convert.FromBase64String(stored.root);
        if (root.Length != 32) throw new CryptographicException("Invalid TPS root. Do not regenerate it for an existing release.");
        return root;
    }
    public static byte[] Seal(byte[] bytes, byte kind, string identity)
    {
        byte[] root = EnsureRoot();
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
        byte[] id = new byte[16]; Buffer.BlockCopy(digest, 0, id, 0, id.Length);
        byte[] key = TpsKeyring.Derive(root, TpsCrypto.Hex(id));
        try
        {
            byte[] encrypted = TpsCrypto.Encrypt(bytes, kind, id, key);
            byte[] check = TpsCrypto.Decrypt(encrypted, key, kind);
            try { if (!bytes.SequenceEqual(check)) throw new CryptographicException("TPS encryption verification failed."); }
            finally { Array.Clear(check, 0, check.Length); }
            return encrypted;
        }
        finally { Array.Clear(root, 0, root.Length); Array.Clear(key, 0, key.Length); }
    }
    private static void ProtectBuild(AddressableAssetSettings settings, IDataBuilder builder, IDataBuilderResult result)
    {
        if (!(result is AddressablesPlayerBuildResult built) || !string.IsNullOrEmpty(built.Error)) return;
        try
        {
            foreach (var bundle in built.AssetBundleBuildResults.Where(b => ProtectedGroup(b.SourceAssetGroup)))
            {
                if (bundle.SourceAssetGroup.GetSchema<BundledAssetGroupSchema>().AssetBundleProviderType.Value != typeof(TpsBundleProvider))
                    throw new BuildFailedException("Music group uses an unprotected provider. Run TPS setup before rebuilding.");
                string source = Path.GetFullPath(bundle.FilePath);
                string output = TpsBundleProvider.TpsPath(source);
                if (!File.Exists(source))
                {
                    if (File.Exists(output)) continue;
                    throw new FileNotFoundException("Protected build output missing.", source);
                }
                byte[] original = File.ReadAllBytes(source);
                try
                {
                    byte[] encrypted = Seal(original, TpsCrypto.Bundle, "bundle/" + bundle.InternalBundleName);
                    File.WriteAllBytes(output + ".tmp", encrypted);
                    if (File.Exists(output)) File.Delete(output);
                    File.Move(output + ".tmp", output);
                    // Only the exact generated output just verified is removed; never a source asset.
                    File.Delete(source);
                    built.FileRegistry?.RemoveFile(bundle.FilePath); built.FileRegistry?.AddFile(output);
                    bundle.FilePath = output;
                }
                finally { Array.Clear(original, 0, original.Length); }
            }
            // Old unencrypted builds must not accompany a fresh upload of ServerData.
            foreach (string folder in built.AssetBundleBuildResults.Where(b => ProtectedGroup(b.SourceAssetGroup)).Select(b => Path.GetDirectoryName(Path.GetFullPath(b.FilePath))).Distinct())
                QuarantineLegacy(folder);
            Debug.Log("[TPS] Protected music build complete. Upload .tps files and the matching catalog, never old .bundle outputs.");
        }
        catch (Exception e) { built.Error = "TPS protection failed: " + e.Message; Debug.LogError(built.Error); }
    }
    private static void QuarantineLegacy(string folder)
    {
        // Only known generated music bundle names, under the exact output folders in this build result.
        string backup = Path.GetFullPath("Library/TpsLegacyBuildOutputs");
        foreach (string file in Directory.GetFiles(folder, "*.bundle"))
        {
            string name = Path.GetFileName(file);
            if (!name.StartsWith("remote_songs_", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("remote_jukeboxaudio_", StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(backup); File.Copy(file, Path.Combine(backup, name), true); File.Delete(file);
        }
    }
    public static void AuditPlayerDependencies()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        if (app?.songCatalog == null) throw new BuildFailedException("Missing playable catalog.");
        var originals = app.songCatalog.songs.Select(s => AssetDatabase.GUIDToAssetPath(s.editorGuid)).ToHashSet();
        var roots = AssetDatabase.GetAllAssetPaths().Where(p => p.Contains("/Resources/") && !p.Contains("/Editor/") && !AssetDatabase.IsValidFolder(p)).ToList();
        roots.AddRange(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path));
        foreach (string root in roots)
            foreach (string dependency in AssetDatabase.GetDependencies(root, true))
                if (originals.Contains(dependency) || dependency.StartsWith(SongContentKeys.LyricsFolder + "/", StringComparison.Ordinal))
                    throw new BuildFailedException("Music content bypasses TPS through a direct player dependency: " + root + " -> " + dependency);
    }
}
#endif
