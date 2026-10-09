using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Resolves a QualityAssetCollection to ONE Addressable reference using Unity's active quality level
/// (QualitySettings.GetQualityLevel / QualitySettings.names — the single source of truth; there is no
/// separate asset-quality state). Levels are always matched BY NAME against the levels configured
/// for the running platform, never by a hard-coded index.
///
/// Fallback when the requested level has no variant:
///   1. the highest available level below the requested one,
///   2. otherwise the lowest available level above it,
///   3. otherwise null + a clear error.
/// e.g. requested High, available Low / Mid / Ultra → Mid.
/// </summary>
public static class QualityAssetResolver
{
    /// <summary>Addressables label carried by every quality variant (set by ContentDeliverySetup):
    /// "Q_Low", "Q_Mid", … — lets downloads intersect a content group with ONE quality.</summary>
    public static string LabelFor(string qualityName) => "Q_" + qualityName;

    public static string CurrentQualityName
    {
        get
        {
            var names = QualitySettings.names;
            int level = QualitySettings.GetQualityLevel();
            return level >= 0 && level < names.Length ? names[level] : null;
        }
    }

    /// <summary>Raised the first time a quality-dependent asset is resolved: from then on the active
    /// quality stays stable for the session (DeviceQualityResolver only caches later rule updates).</summary>
    public static bool QualityInUse { get; private set; }

    public static AssetReferenceGameObject Resolve(QualityAssetCollection collection, out string resolvedQuality)
    {
        QualityInUse = true;
        return Resolve(collection, QualitySettings.names, QualitySettings.GetQualityLevel(), out resolvedQuality, logErrors: true);
    }

    /// <summary>Pure resolution against an explicit level list (tests / editor preview).</summary>
    public static AssetReferenceGameObject Resolve(QualityAssetCollection collection, IReadOnlyList<string> levelNames, int requestedLevel,
                                                   out string resolvedQuality, bool logErrors = false)
    {
        resolvedQuality = null;
        if (collection?.variants == null || levelNames == null || levelNames.Count == 0) return null;

        // Available variants indexed by the level they belong to (unknown names are ignored).
        var byLevel = new Dictionary<int, QualityAssetReference>();
        foreach (var v in collection.variants)
        {
            if (v == null || !v.IsAssigned) continue;
            int idx = IndexOf(levelNames, v.qualityName);
            if (idx >= 0 && !byLevel.ContainsKey(idx)) byLevel[idx] = v;
        }

        int chosen = ChooseLevel(byLevel.Keys, requestedLevel);
        if (chosen < 0)
        {
            if (logErrors) Debug.LogError("[QualityAssetResolver] No valid quality variant assigned (none of the entries matches a configured quality level with a valid Addressable).");
            return null;
        }
        resolvedQuality = levelNames[chosen];
        return byLevel[chosen].prefab;
    }

    /// <summary>The fallback rule on level indices (pure): exact, else highest below, else lowest above, else -1.</summary>
    public static int ChooseLevel(IEnumerable<int> available, int requested)
    {
        int below = -1, above = int.MaxValue;
        foreach (int a in available)
        {
            if (a == requested) return a;
            if (a < requested && a > below) below = a;
            if (a > requested && a < above) above = a;
        }
        if (below >= 0) return below;
        return above != int.MaxValue ? above : -1;
    }

    public static int IndexOf(IReadOnlyList<string> names, string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        for (int i = 0; i < names.Count; i++)
            if (string.Equals(names[i], name, System.StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
