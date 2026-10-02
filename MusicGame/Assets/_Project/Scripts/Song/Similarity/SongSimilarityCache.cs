using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Where similarity vectors come from, in priority order:
///   1. runtime cache  — persistentDataPath/SongSimilarity/vectors.json, written whenever a FULL
///                       SongProfile of a catalog song becomes available (GameSession, SongProfileReadyEvent);
///   2. baked catalog  — AppConfigSO.songSimilarity (SongSimilarityCatalogSO, Editor-precomputed);
///   3. lazy fallback  — an existing SongCache analysis JSON for that clip (no audio, no ML: only the
///                       derived features are recomputed), then stored into (1). Not the normal case.
/// Never runs a full audio analysis just to answer "what comes next?". Small (ids + ~66 floats each),
/// kept apart from PlayerProgress (which stores ids only).
/// </summary>
public static class SongSimilarityCache
{
    [System.Serializable]
    private class FileData { public List<SongSimilarityVector> vectors = new(); }

    private static Dictionary<string, SongSimilarityVector> s_runtime;
    private static Dictionary<string, SongSimilarityVector> s_baked;
    private static readonly HashSet<string> s_lazyMisses = new();

    private static string FilePath => Path.Combine(Application.persistentDataPath, "SongSimilarity", "vectors.json");

    public static bool TryGet(string songId, out SongSimilarityVector vector)
    {
        EnsureLoaded();
        if (!string.IsNullOrEmpty(songId) &&
            ((s_runtime.TryGetValue(songId, out vector) && vector.IsValid) ||
             (s_baked.TryGetValue(songId, out vector) && vector.IsValid)))
            return true;
        vector = null;
        return false;
    }

    /// <summary>TryGet, then the lazy SongCache fallback (attempted once per id per session).</summary>
    public static bool TryGetOrLazyBuild(string songId, out SongSimilarityVector vector)
    {
        if (TryGet(songId, out vector)) return true;
        if (string.IsNullOrEmpty(songId) || !s_lazyMisses.Add(songId)) return false;
        var profile = LoadCachedProfile(songId);
        if (profile == null) return false;
        Store(songId, profile);
        return TryGet(songId, out vector);
    }

    /// <summary>Caches the vector of a FULL profile for a playable catalog song (no-op otherwise).</summary>
    public static void Store(string songId, SongProfile profile)
    {
        var v = SongSimilarityVector.Build(songId, profile);
        if (v == null) return;
        EnsureLoaded();
        if (s_runtime.TryGetValue(songId, out var old) && SameValues(old, v)) return;
        s_runtime[songId] = v;
        SaveRuntime();
        Debug.Log($"[SongSimilarity] Vector cached for '{songId}'.");
    }

    // ── loading / saving ──────────────────────────────────────────────────────

    private static void EnsureLoaded()
    {
        if (s_runtime != null) return;
        s_runtime = new Dictionary<string, SongSimilarityVector>();
        s_baked   = new Dictionary<string, SongSimilarityVector>();

        var app = Resources.Load<AppConfigSO>("AppConfig");
        if (app != null && app.songSimilarity != null)
            foreach (var v in app.songSimilarity.vectors)
                if (v != null && !string.IsNullOrEmpty(v.songId)) s_baked[v.songId] = v;

        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath));
                if (data?.vectors != null)
                    foreach (var v in data.vectors)
                        if (v != null && !string.IsNullOrEmpty(v.songId) && v.IsValid) s_runtime[v.songId] = v;
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[SongSimilarity] Runtime vector cache unreadable, ignored: {e.Message}"); }
    }

    private static void SaveRuntime()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(new FileData { vectors = new List<SongSimilarityVector>(s_runtime.Values) }, true));
        }
        catch (System.Exception e) { Debug.LogWarning($"[SongSimilarity] Could not save vector cache: {e.Message}"); }
    }

    private static bool SameValues(SongSimilarityVector a, SongSimilarityVector b)
    {
        if (a == null || !a.IsValid) return false;
        for (int i = 0; i < a.features.Length; i++) if (!Mathf.Approximately(a.features[i], b.features[i])) return false;
        for (int i = 0; i < a.tags.Length; i++) if (!Mathf.Approximately(a.tags[i], b.tags[i])) return false;
        return true;
    }

    /// <summary>The SongCache JSON of a catalog song (SongCache names it "&lt;clip name&gt;_&lt;samples&gt;.json";
    /// a playable song's address is its clip name). Derived features recomputed. Null when absent.</summary>
    public static SongProfile LoadCachedProfile(string songId, string clipName = null)
    {
        clipName ??= songId;
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, "SongCache");
            if (!Directory.Exists(dir)) return null;
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                int us = name.LastIndexOf('_');
                if (us <= 0 || name.Substring(0, us) != clipName) continue;
                var data = JsonUtility.FromJson<SongProfileData>(File.ReadAllText(file));
                if (data == null || data.clipName != clipName) continue;
                var profile = data.ToProfile();
                var app = Resources.Load<AppConfigSO>("AppConfig");
                AudioPreAnalyzer.ComputeDerivedFeatures(profile, app != null ? app.audioAnalysis : null);
                return profile;
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[SongSimilarity] SongCache read failed for '{songId}': {e.Message}"); }
        return null;
    }

    /// <summary>Tests / tools: forget the in-memory state (next access reloads from disk/assets).</summary>
    public static void ResetForTests() { s_runtime = null; s_baked = null; s_lazyMisses.Clear(); }
}
