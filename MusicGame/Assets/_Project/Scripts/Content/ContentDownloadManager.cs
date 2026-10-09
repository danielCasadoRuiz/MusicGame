using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>The four download categories (Addressables label "Category_&lt;name&gt;").</summary>
public enum ContentCategory
{
    /// <summary>Shipped in the player (local, static) — never needs a download.</summary>
    Core,
    Enemies,
    Styles,
    PlayerClothing,
}

/// <summary>Logical content group keys (Addressables labels) — see ContentDeliverySetup for how
/// they are assigned. One group may span several bundles; quality variants inside a group carry an
/// extra "Q_&lt;quality&gt;" label so only ONE quality is ever downloaded.</summary>
public static class ContentKeys
{
    public const string Core = "Core";

    /// <summary>Category-wide label carried by every item of that category ("Category_Enemies"…).</summary>
    public static string Category(ContentCategory category) => category == ContentCategory.Core ? Core : "Category_" + category;

    /// <summary>Category of a logical group label ("Enemy_monteverdi" → Enemies, "Style_x" → Styles,
    /// "PlayerClothing_x" → PlayerClothing, "Core…" → Core). Unknown prefixes → PlayerClothing.</summary>
    public static ContentCategory CategoryOf(string groupLabel)
    {
        if (string.IsNullOrEmpty(groupLabel)) return ContentCategory.PlayerClothing;
        if (groupLabel.StartsWith("Enemy_")) return ContentCategory.Enemies;
        if (groupLabel.StartsWith("Style_")) return ContentCategory.Styles;
        if (groupLabel.StartsWith(Core)) return ContentCategory.Core;
        return ContentCategory.PlayerClothing;
    }

    /// <summary>Address of a quality variant: "&lt;asset id&gt;@&lt;quality&gt;" — the part before '@'
    /// groups the variants of ONE conceptual asset, so a category download falls back PER ASSET.</summary>
    public static string VariantAddress(string assetId, string quality) => assetId + "@" + quality;
    public static string VariantAssetId(string address)
    {
        int at = address != null ? address.LastIndexOf('@') : -1;
        return at > 0 ? address.Substring(0, at) : address;
    }
    public static string Style(string styleId) => "Style_" + styleId;
    public static string Enemy(string opponentId) => "Enemy_" + opponentId;
    public static string PlayerClothing(string itemId) => "PlayerClothing_" + itemId;
}

/// <summary>
/// The ONE place content is downloaded/cached (Addressables underneath, so local and remote content
/// behave identically for callers). Downloading is separate from loading/instantiating: gameplay
/// code keeps instantiating through its own loaders (AvatarFactory, ThemeAssetLoader…), which hit
/// the cache once content is here.
///
///   GetDownloadSizeAsync(key)        bytes still to download (0 = cached / local)
///   EnsureContentAvailableAsync(key) download what is missing (progress, retries, cancellation,
///                                    one shared task per key for concurrent callers)
///   PreloadContentAsync(key)         the same, fire-and-forget friendly (e.g. the next rival)
///
/// A key is a logical group label (ContentKeys), an Addressable address/label, an AssetReference, or
/// a QualityAssetCollection. QUALITY: locations of the group that carry a "Q_*" label are filtered to
/// the active Unity quality level (same fallback as QualityAssetResolver); untagged content (UI,
/// fonts, shared materials) is always included. Download handles are always released.
/// </summary>
public class ContentDownloadManager : MonoBehaviour, IAppModule
{
    public static ContentDownloadManager Instance { get; private set; }

    public bool IsInitialized { get; private set; }
    public float LastProgress { get; private set; }

    private ContentDeliveryConfigSO _config;
    private readonly Dictionary<string, Task<bool>> _inFlight = new();
    private Task _initTask;

    public void Configure(ContentDeliveryConfigSO config) => _config = config;

    void IAppModule.Initialize(AppContext context)
    {
        Instance = this;
        // Remote device rules never block startup (timeout; cached for the next launch if late).
        if (_config != null && !string.IsNullOrWhiteSpace(_config.remoteDeviceProfilesUrl))
            StartCoroutine(DeviceQualityResolver.RefreshRemoteRules(_config.remoteDeviceProfilesUrl, _config.remoteProfilesTimeoutSeconds));
        _initTask = InitializeAddressablesAsync();
    }

    void IAppModule.Shutdown() { if (Instance == this) Instance = null; }

    private async Task InitializeAddressablesAsync()
    {
        try
        {
            var init = Addressables.InitializeAsync(false);
            await init.Task;
            Addressables.Release(init);

            if (_config == null || _config.checkCatalogUpdatesOnStart)
            {
                var check = Addressables.CheckForCatalogUpdates(false);
                await check.Task;
                var catalogs = check.Status == AsyncOperationStatus.Succeeded ? check.Result : null;
                Addressables.Release(check);
                if (catalogs != null && catalogs.Count > 0)
                {
                    var update = Addressables.UpdateCatalogs(catalogs, false);
                    await update.Task;
                    Debug.Log($"[ContentDownloadManager] Remote catalog updated ({catalogs.Count}).");
                    Addressables.Release(update);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ContentDownloadManager] Addressables init / catalog check failed — continuing with the local catalog: {e.Message}");
        }
        IsInitialized = true;
    }

    private Task EnsureInitialized() => _initTask ?? Task.CompletedTask;

    // ── Public API ──────────────────────────────────────────────────────────────

    public async Task<long> GetDownloadSizeAsync(object contentKey)
    {
        await EnsureInitialized();
        var locations = await ResolveLocationsAsync(contentKey);
        if (locations.Count == 0) return 0;
        var size = Addressables.GetDownloadSizeAsync(locations);
        await size.Task;
        long bytes = size.Status == AsyncOperationStatus.Succeeded ? size.Result : 0;
        Addressables.Release(size);
        return bytes;
    }

    /// <summary>Downloads everything `contentKey` needs at the active quality (nothing if cached /
    /// local). Concurrent calls for the same key share one task. True = available.</summary>
    public Task<bool> EnsureContentAvailableAsync(object contentKey, IProgress<float> progress = null, CancellationToken cancellation = default)
    {
        string id = KeyId(contentKey);
        if (id == null) return Task.FromResult(false);
        if (_inFlight.TryGetValue(id, out var running)) return running;
        var task = DownloadWithRetries(contentKey, id, progress, cancellation);
        _inFlight[id] = task;
        return task;
    }

    /// <summary>Download ahead of time (no instantiation). Safe to call repeatedly / without awaiting.</summary>
    public Task<bool> PreloadContentAsync(object contentKey) => EnsureContentAvailableAsync(contentKey);

    /// <summary>Everything in a category (all enemies / all styles / all player clothing) at the ACTIVE
    /// quality — each item is its own bundle, so only changed/missing bundles are fetched. Nothing is
    /// instantiated. Core is local: always available.</summary>
    public Task<bool> EnsureCategoryAvailableAsync(ContentCategory category, IProgress<float> progress = null, CancellationToken cancellation = default) =>
        category == ContentCategory.Core ? Task.FromResult(true) : EnsureContentAvailableAsync(ContentKeys.Category(category), progress, cancellation);

    public Task<long> GetCategoryDownloadSizeAsync(ContentCategory category) =>
        category == ContentCategory.Core ? Task.FromResult(0L) : GetDownloadSizeAsync(ContentKeys.Category(category));

    /// <summary>Removes the cached bundles of `contentKey` (e.g. to reclaim space).</summary>
    public async Task ClearCacheAsync(object contentKey)
    {
        var locations = await ResolveLocationsAsync(contentKey);
        if (locations.Count == 0) return;
        var clear = Addressables.ClearDependencyCacheAsync(locations, false);
        await clear.Task;
        Addressables.Release(clear);
    }

    // ── Internals ───────────────────────────────────────────────────────────────

    private async Task<bool> DownloadWithRetries(object contentKey, string id, IProgress<float> progress, CancellationToken cancellation)
    {
        try
        {
            await EnsureInitialized();
            var locations = await ResolveLocationsAsync(contentKey);
            if (locations.Count == 0) { progress?.Report(1f); return true; } // nothing addressable under this key: nothing to fetch

            int attempts = 1 + (_config != null ? _config.downloadRetries : 2);
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (cancellation.IsCancellationRequested) return false;
                var sizeOp = Addressables.GetDownloadSizeAsync(locations);
                await sizeOp.Task;
                long bytes = sizeOp.Status == AsyncOperationStatus.Succeeded ? sizeOp.Result : -1;
                Addressables.Release(sizeOp);
                if (bytes == 0) { progress?.Report(1f); return true; } // already cached / local

                var download = Addressables.DownloadDependenciesAsync(locations, false);
                while (!download.IsDone)
                {
                    if (cancellation.IsCancellationRequested) break; // the bundle request finishes in the background; we stop waiting
                    LastProgress = download.GetDownloadStatus().Percent;
                    progress?.Report(LastProgress);
                    await Task.Yield();
                }
                bool ok = download.IsDone && download.Status == AsyncOperationStatus.Succeeded;
                string error = download.OperationException?.Message;
                Addressables.Release(download);
                if (ok) { progress?.Report(1f); return true; }
                if (cancellation.IsCancellationRequested) return false;

                Debug.LogWarning($"[ContentDownloadManager] Download of '{id}' failed (attempt {attempt}/{attempts}): {error}");
                if (attempt < attempts) await Task.Delay(TimeSpan.FromSeconds(_config != null ? _config.retryDelaySeconds : 1.5f));
            }
            Debug.LogError($"[ContentDownloadManager] '{id}' could not be downloaded.");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ContentDownloadManager] '{id}': {e.Message}");
            return false;
        }
        finally { _inFlight.Remove(id); }
    }

    private static string KeyId(object key) => key switch
    {
        null => null,
        string s => s,
        QualityAssetCollection q => q.Resolve()?.RuntimeKey?.ToString(),
        AssetReference r => r.RuntimeKeyIsValid() ? r.RuntimeKey.ToString() : null,
        _ => key.ToString(),
    };

    /// <summary>All locations behind `key`, with quality-tagged ones filtered to ONE quality.</summary>
    private async Task<List<IResourceLocation>> ResolveLocationsAsync(object key)
    {
        object runtimeKey = key switch
        {
            QualityAssetCollection q => q.Resolve()?.RuntimeKey,
            AssetReference r => r.RuntimeKeyIsValid() ? r.RuntimeKey : null,
            _ => key,
        };
        var result = new List<IResourceLocation>();
        if (runtimeKey == null) return result;

        var all = await Locations(runtimeKey);
        if (all.Count == 0 || key is QualityAssetCollection || key is AssetReference) { result.AddRange(all); return result; }

        // Which locations are quality variants (and of which level).
        var names = QualitySettings.names;
        var tagged = new Dictionary<string, int>();             // location id → quality level
        for (int level = 0; level < names.Length; level++)
            foreach (var loc in await Locations(QualityAssetResolver.LabelFor(names[level])))
                tagged[Id(loc)] = level;

        result.AddRange(FilterToQuality(all, loc => tagged.TryGetValue(Id(loc), out int lv) ? lv : -1,
                                        loc => ContentKeys.VariantAssetId(loc.PrimaryKey), QualitySettings.GetQualityLevel()));
        return result;
    }

    /// <summary>Keeps every untagged location (quality-independent content) and, PER conceptual asset
    /// (variants grouped by assetIdOf), only the variant of the level QualityAssetResolver would pick
    /// (exact, else highest below, else lowest above). Pure — also used by the checks.</summary>
    public static List<T> FilterToQuality<T>(IEnumerable<T> all, Func<T, int> levelOf, Func<T, string> assetIdOf, int requestedLevel)
    {
        var levelsByAsset = new Dictionary<string, HashSet<int>>();
        foreach (var loc in all)
        {
            int lv = levelOf(loc);
            if (lv < 0) continue;
            string asset = assetIdOf(loc) ?? "";
            if (!levelsByAsset.TryGetValue(asset, out var set)) levelsByAsset[asset] = set = new HashSet<int>();
            set.Add(lv);
        }
        var chosen = new Dictionary<string, int>();
        foreach (var kv in levelsByAsset) chosen[kv.Key] = QualityAssetResolver.ChooseLevel(kv.Value, requestedLevel);

        var result = new List<T>();
        foreach (var loc in all)
        {
            int lv = levelOf(loc);
            if (lv < 0 || chosen[assetIdOf(loc) ?? ""] == lv) result.Add(loc);
        }
        return result;
    }

    private static async Task<IList<IResourceLocation>> Locations(object key)
    {
        var op = Addressables.LoadResourceLocationsAsync(key);
        await op.Task;
        var list = op.Status == AsyncOperationStatus.Succeeded && op.Result != null
            ? new List<IResourceLocation>(op.Result) : new List<IResourceLocation>();
        Addressables.Release(op);
        return list;
    }

    private static string Id(IResourceLocation loc) => loc.InternalId + "|" + loc.ResourceType;
}
