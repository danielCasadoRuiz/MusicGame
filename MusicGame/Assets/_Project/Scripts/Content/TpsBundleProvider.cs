using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.Scripting;

/// <summary>Addressables provider for encrypted catalog music bundles. Disk/cache holds TPS only.
/// The authenticated Unity bundle is opened from memory; AudioClip representation and profile binding
/// remain unchanged. No fallback to an old unencrypted bundle or Unity's plaintext bundle cache.</summary>
[Preserve]
public sealed class TpsBundleProvider : ResourceProviderBase
{
    public override Type GetDefaultType(IResourceLocation location) => typeof(IAssetBundleResource);
    public override void Provide(ProvideHandle handle) => new Resource().Run(handle);
    public override void Release(IResourceLocation location, object asset) { if (asset is Resource resource) resource.Release(); }

    public static string TpsPath(string path)
    {
        int query = path.IndexOf('?');
        string suffix = query < 0 ? "" : path.Substring(query);
        string file = query < 0 ? path : path.Substring(0, query);
        return (file.EndsWith(".tps", StringComparison.OrdinalIgnoreCase) ? file : Path.ChangeExtension(file, ".tps")) + suffix;
    }
    public static string CachePath(IResourceLocation location)
    {
        var options = location.Data as AssetBundleRequestOptions;
        string identity = (options?.BundleName ?? location.InternalId) + "|" + options?.Hash;
        using var sha = SHA256.Create();
        string file = TpsCrypto.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))) + ".tps";
        return Path.Combine(Application.persistentDataPath, "TpsBundleCache", file);
    }
    public static bool IsProtected(IResourceLocation location) => location.ProviderId != null && location.ProviderId.Contains(nameof(TpsBundleProvider));
    private static IEnumerable<IResourceLocation> Bundles(IEnumerable<IResourceLocation> locations)
    {
        var seen = new HashSet<string>();
        var pending = new Stack<IResourceLocation>(locations);
        while (pending.Count > 0)
        {
            var location = pending.Pop();
            if (location == null || !seen.Add(location.InternalId + "|" + location.ProviderId)) continue;
            if (IsProtected(location)) yield return location;
            if (location.HasDependencies) foreach (var dependency in location.Dependencies) pending.Push(dependency);
        }
    }
    public static long AdjustDownloadSize(long original, IEnumerable<IResourceLocation> locations)
    {
        long result = original;
        foreach (var location in Bundles(locations))
        {
            var options = location.Data as AssetBundleRequestOptions;
            if (options == null) continue;
            string path = Addressables.ResourceManager.TransformInternalId(location);
            bool remote = path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            long missing = remote && !File.Exists(CachePath(location)) ? options.BundleSize + TpsCrypto.HeaderSize + TpsCrypto.TagSize + 16 : 0;
            result += missing - options.ComputeSize(location, Addressables.ResourceManager);
        }
        return Math.Max(0, result);
    }
    public static void ClearEncryptedCache(IEnumerable<IResourceLocation> locations)
    {
        foreach (var location in Bundles(locations))
        {
            string path = CachePath(location); if (File.Exists(path)) File.Delete(path);
        }
    }
    private sealed class Resource : IAssetBundleResource
    {
        private AssetBundle _bundle;
        private long _downloaded, _total;
        private float _progress;
        public AssetBundle GetAssetBundle() => _bundle;
        public void Release() { if (_bundle != null) _bundle.Unload(true); _bundle = null; }
        public async void Run(ProvideHandle handle)
        {
            byte[] plain = null;
            try
            {
                handle.SetProgressCallback(() => _progress);
                handle.SetDownloadProgressCallbacks(() => new DownloadStatus { DownloadedBytes = _downloaded, TotalBytes = _total, IsDone = _progress >= 1 });
                string source = TpsPath(handle.ResourceManager.TransformInternalId(handle.Location));
                var options = handle.Location.Data as AssetBundleRequestOptions;
                string cache = CachePath(handle.Location);
                bool local = !source.Contains("://");
                string file = local ? source : cache;
                byte[] envelope;
                if (File.Exists(file)) envelope = await Task.Run(() => File.ReadAllBytes(file));
                else
                {
                    if (local) throw new FileNotFoundException("Protected music bundle missing. Rebuild protected content.", source);
                    using var request = UnityWebRequest.Get(source);
                    request.timeout = options?.Timeout > 0 ? options.Timeout : 60;
                    _total = (options?.BundleSize ?? 0) + 96;
                    var operation = request.SendWebRequest();
                    while (!operation.isDone) { _downloaded = (long)request.downloadedBytes; _progress = request.downloadProgress * .7f; await Task.Yield(); }
                    if (request.result != UnityWebRequest.Result.Success) throw new IOException("Protected music load failed: " + request.error);
                    envelope = request.downloadHandler.data;
                }
                byte[] keys = TpsKeyring.Resolve(TpsCrypto.KeyId(envelope));
                try { plain = await Task.Run(() => TpsCrypto.Decrypt(envelope, keys, TpsCrypto.Bundle)); }
                finally { Array.Clear(keys, 0, keys.Length); }
                // Never cache bytes before their authentication succeeds. Cache is always encrypted.
                if (!local && !File.Exists(cache))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cache));
                    string temp = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        await Task.Run(() => File.WriteAllBytes(temp, envelope));
                        if (!File.Exists(cache)) File.Move(temp, cache);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                _downloaded = _total; _progress = .8f;
                // Addressables uses this internal wrapper for prefetch-only requests. No bundle is
                // opened until an asset is actually loaded (same contract as its stock provider).
                if (handle.Location.GetType().Name != "DownloadOnlyLocation")
                {
                    var load = AssetBundle.LoadFromMemoryAsync(plain, options?.Crc ?? 0);
                    while (!load.isDone) { _progress = .8f + load.progress * .2f; await Task.Yield(); }
                    _bundle = load.assetBundle;
                    if (_bundle == null) throw new IOException("Protected music bundle could not be opened.");
                }
                _progress = 1;
                handle.Complete<IAssetBundleResource>(this, true, null);
            }
            catch (Exception e)
            {
                // A damaged cache must not cause an endless offline failure on subsequent retries.
                string cache = CachePath(handle.Location);
                if (e is CryptographicException && File.Exists(cache))
                {
                    try { File.Delete(cache); } catch (IOException cleanup) { Debug.LogWarning("[TPS] Cache cleanup failed: " + cleanup.Message); }
                }
                Release(); handle.Complete<IAssetBundleResource>(null, false, e);
            }
            finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
        }
    }
}
