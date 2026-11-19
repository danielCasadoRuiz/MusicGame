#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

/// <summary>Real Unity bundle -> TPS -> actual Addressables provider -> AudioClip test.
/// Does not enter Play mode, change player progress, or use network services.</summary>
public static class TpsValidation
{
    private static readonly StringBuilder Report = new();
    private static string s_sourceFolder, s_buildFolder;
    private static ResourceManager s_manager;
    private static AsyncOperationHandle<IAssetBundleResource> s_load;
    private static IResourceLocation s_location, s_remoteLocation;
    private static string s_remoteCache;
    private static float[] s_expected;
    private static int s_stage;
    private static double s_deadline;

    [MenuItem("Tools/MusicGame/Content/Songs/Verify TPS Encryption")]
    public static void Run()
    {
        try
        {
            Report.Clear(); s_stage = 0;
            TpsContentProtection.Setup(); TpsKeyring.Reset();
            TpsContentProtection.AuditPlayerDependencies();
            Check(true, "Playable originals do not leak through Resources or build scenes");
            var native = Resources.Load<JukeboxNativeCatalogSO>("JukeboxNativeCatalog");
            foreach (var entry in native.songs)
            {
                string path = "Assets/_Project/Content/JukeboxAudio/" + entry.songId + ".tps.bytes";
                byte[] bytes = File.ReadAllBytes(path);
                byte[] clear = TpsKeyring.Open(bytes, TpsCrypto.Aac);
                try { Check(clear.Length > 12 && Encoding.ASCII.GetString(clear, 4, 4) == "ftyp", "Protected native AAC authenticates: " + entry.songId); }
                finally { Array.Clear(clear, 0, clear.Length); }
                var keyTask = (System.Threading.Tasks.Task<string>)typeof(NativeMusicPlayer).GetMethod("ReadProtectedKeyAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { path });
                byte[] nativeKey = Convert.FromBase64String(keyTask.GetAwaiter().GetResult());
                Check(nativeKey.Length == 64, "Native queue authenticates its encrypted source on a worker: " + entry.songId);
                Array.Clear(nativeKey, 0, nativeKey.Length);
                Check(!File.Exists(path.Replace(".tps.bytes", ".m4a.bytes")), "No generated clear M4A remains: " + entry.songId);
            }
            byte[] keys = TpsCrypto.RandomBytes(64), id = TpsCrypto.RandomBytes(16), payload = TpsCrypto.RandomBytes(4001);
            byte[] encrypted = TpsCrypto.Encrypt(payload, TpsCrypto.Bundle, id, keys);
            Check(TpsCrypto.Decrypt(encrypted, keys, TpsCrypto.Bundle).SequenceEqual(payload), "Unity runtime crypto backend round trip");
            encrypted[48] ^= 1;
            bool rejected = false;
            try { TpsCrypto.Decrypt(encrypted, keys, TpsCrypto.Bundle); } catch (System.Security.Cryptography.CryptographicException) { rejected = true; }
            Check(rejected, "Unity runtime rejects tampered ciphertext");
            Array.Clear(keys, 0, keys.Length);

            string token = Guid.NewGuid().ToString("N");
            s_sourceFolder = "Assets/_Project/TpsValidation_" + token;
            s_buildFolder = Path.GetFullPath("Temp/TpsValidation_" + token);
            Directory.CreateDirectory(s_sourceFolder); Directory.CreateDirectory(s_buildFolder);
            string wave = s_sourceFolder + "/fixture.wav";
            const int frequency = 22050, samples = 4410;
            using (var writer = new BinaryWriter(File.Create(wave)))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(samples * 2 + 36);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(frequency); writer.Write(frequency * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                for (int i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 220 / frequency) * 1000));
            }
            AssetDatabase.ImportAsset(wave);
            var importer = (AudioImporter)AssetImporter.GetAtPath(wave);
            var audioSettings = importer.defaultSampleSettings;
            audioSettings.loadType = AudioClipLoadType.DecompressOnLoad; audioSettings.compressionFormat = AudioCompressionFormat.PCM; audioSettings.preloadAudioData = true;
            importer.defaultSampleSettings = audioSettings; importer.loadInBackground = false; importer.SaveAndReimport();
            var original = AssetDatabase.LoadAssetAtPath<AudioClip>(wave); original.LoadAudioData();
            s_expected = new float[original.samples * original.channels];
            Check(original.GetData(s_expected, 0), "Reference audio fixture decoded");
            var manifest = BuildPipeline.BuildAssetBundles(s_buildFolder,
                new[] { new AssetBundleBuild { assetBundleName = "fixture.bundle", assetNames = new[] { wave } } },
                BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.ForceRebuildAssetBundle, EditorUserBuildSettings.activeBuildTarget);
            Check(manifest != null, "Actual Unity audio bundle built");
            string plainBundle = Path.Combine(s_buildFolder, "fixture.bundle");
            BuildPipeline.GetCRCForAssetBundle(plainBundle, out uint crc);
            var originalBundle = File.ReadAllBytes(plainBundle);
            string protectedBundle = TpsBundleProvider.TpsPath(plainBundle);
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            var buildResult = new UnityEditor.AddressableAssets.Build.AddressablesPlayerBuildResult();
            buildResult.AssetBundleBuildResults.Add(new UnityEditor.AddressableAssets.Build.AddressablesPlayerBuildResult.BundleBuildResult
            { FilePath = plainBundle, InternalBundleName = "fixture", SourceAssetGroup = settings.FindGroup(SongContentKeys.GroupName), Crc = crc });
            typeof(TpsContentProtection).GetMethod("ProtectBuild", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { settings, null, buildResult });
            Check(string.IsNullOrEmpty(buildResult.Error) && File.Exists(protectedBundle) && !File.Exists(plainBundle), "Production build hook replaces plaintext bundle output with TPS");
            Array.Clear(originalBundle, 0, originalBundle.Length);
            s_manager = new ResourceManager();
            var provider = new TpsBundleProvider(); provider.Initialize(typeof(TpsBundleProvider).FullName, null); s_manager.ResourceProviders.Add(provider);
            var location = new ResourceLocationBase("TPS fixture", plainBundle, provider.ProviderId, typeof(IAssetBundleResource));
            location.Data = new AssetBundleRequestOptions { Crc = crc, BundleName = "fixture", Hash = Hash128.Compute(token).ToString(), BundleSize = new FileInfo(protectedBundle).Length };
            s_location = location;
            var remote = new ResourceLocationBase("Offline TPS fixture", "https://offline.invalid/fixture.bundle", provider.ProviderId, typeof(IAssetBundleResource));
            remote.Data = location.Data; s_remoteLocation = remote;
            s_remoteCache = TpsBundleProvider.CachePath(remote);
            Check(TpsBundleProvider.AdjustDownloadSize(((AssetBundleRequestOptions)remote.Data).BundleSize, new[] { remote }) > 0, "Missing protected cache reports a real download requirement");
            Directory.CreateDirectory(Path.GetDirectoryName(s_remoteCache));
            File.Copy(protectedBundle, s_remoteCache, true);
            Check(TpsBundleProvider.AdjustDownloadSize(((AssetBundleRequestOptions)remote.Data).BundleSize, new[] { remote }) == 0, "Encrypted local cache reports zero download size");
            s_load = s_manager.ProvideResource<IAssetBundleResource>(location);
            s_deadline = EditorApplication.timeSinceStartup + 45;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }
    private static void Tick()
    {
        try
        {
            typeof(ResourceManager).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(s_manager, new object[] { .016f });
            if (!s_load.IsDone)
            {
                if (EditorApplication.timeSinceStartup > s_deadline) throw new TimeoutException("Protected bundle load timed out");
                return;
            }
            if (s_stage == 0)
            {
                Check(s_load.Status == AsyncOperationStatus.Succeeded, "Addressables TPS provider loads protected bundle from memory");
                var bundle = s_load.Result.GetAssetBundle();
                var clip = bundle.LoadAllAssets<AudioClip>().Single(); clip.LoadAudioData();
                var actual = new float[clip.samples * clip.channels];
                Check(clip.GetData(actual, 0) && actual.SequenceEqual(s_expected), "Decrypted bundle preserves exact PCM samples and timing");
                s_manager.Release(s_load); s_load = default;
                s_stage = 1; s_load = s_manager.ProvideResource<IAssetBundleResource>(s_remoteLocation); s_deadline = EditorApplication.timeSinceStartup + 30;
            }
            else if (s_stage == 1)
            {
                Check(s_load.Status == AsyncOperationStatus.Succeeded && s_load.Result.GetAssetBundle() != null, "Protected bundle plays offline from encrypted cache with an unreachable remote URL");
                s_manager.Release(s_load); s_load = default;
                byte[] broken = File.ReadAllBytes(s_remoteCache); broken[broken.Length - 1] ^= 1; File.WriteAllBytes(s_remoteCache, broken);
                s_stage = 2; s_load = s_manager.ProvideResource<IAssetBundleResource>(s_remoteLocation); s_deadline = EditorApplication.timeSinceStartup + 30;
            }
            else
            {
                Check(s_load.Status == AsyncOperationStatus.Failed, "Addressables TPS provider rejects tampering without opening audio");
                Check(!File.Exists(s_remoteCache), "Invalid encrypted cache is removed so the next retry can recover");
                Check(!File.Exists(s_location.InternalId), "No decrypted bundle was written back to disk");
                Finish(true, null);
            }
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Report.AppendLine("PASS " + message);
    }
    private static void Finish(bool success, string error)
    {
        EditorApplication.update -= Tick;
        if (s_load.IsValid()) s_manager?.Release(s_load); s_load = default;
        s_manager?.Dispose(); s_manager = null;
        if (!string.IsNullOrEmpty(s_remoteCache) && s_remoteCache.StartsWith(Path.Combine(Application.persistentDataPath, "TpsBundleCache") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(s_remoteCache)) File.Delete(s_remoteCache);
        if (!string.IsNullOrEmpty(s_sourceFolder) && s_sourceFolder.StartsWith("Assets/_Project/TpsValidation_", StringComparison.Ordinal)) AssetDatabase.DeleteAsset(s_sourceFolder);
        if (!string.IsNullOrEmpty(s_buildFolder) && s_buildFolder.StartsWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(s_buildFolder)) Directory.Delete(s_buildFolder, true);
        if (!success) Report.AppendLine("FAIL " + error);
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/TpsValidation.txt", Report.ToString());
        if (success) Debug.Log(Report); else Debug.LogError(Report);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
    }
}
#endif
