using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Where similarity vectors come from, in priority order:
///   0. published index — SongCatalogIndexSO entries (remote content, SongCatalogService.RegisterPublished):
///                       the vectors of the songs actually published, including songs added by a content
///                       update after the player shipped;
///   1. baked catalog  — AppConfigSO.songSimilarity (SongSimilarityCatalogSO, Editor-precomputed for
///                       EVERY built-in playable song): the normal source, no play history needed;
///   2. runtime cache  — persistentDataPath/SongSimilarity/vectors.json, written after a FULL analysis
///                       of a catalog song that has NO baked vector (dev content / future content);
///   3. lazy fallback  — the local binary analysis cache entry of that song (no audio, no ML: only the
///                       derived features are recomputed), then stored into (2). Not the normal case.
/// Never runs a full audio analysis just to answer "what comes next?". Small (ids + ~66 floats each),
/// kept apart from PlayerProgress (which stores ids only).
/// </summary>
public static class SongSimilarityCache
{
    [System.Serializable]
    private class FileData { public List<SongSimilarityVector> vectors = new(); }

    private static Dictionary<string, SongSimilarityVector> s_runtime;
    private static Dictionary<string, SongSimilarityVector> s_baked;
    private static readonly Dictionary<string, SongSimilarityVector> s_published = new();
    private static readonly HashSet<string> s_lazyMisses = new();

    private static string FilePath => Path.Combine(Application.persistentDataPath, "SongSimilarity", "vectors.json");

    public static bool TryGet(string songId, out SongSimilarityVector vector)
    {
        EnsureLoaded();
        if (!string.IsNullOrEmpty(songId) &&
            ((s_published.TryGetValue(songId, out vector) && vector.IsValid) ||
             (s_baked.TryGetValue(songId, out vector) && vector.IsValid) ||
             (s_runtime.TryGetValue(songId, out vector) && vector.IsValid)))
            return true;
        vector = null;
        return false;
    }

    /// <summary>TryGet, then the lazy analysis-cache fallback (attempted once per id per session).</summary>
    public static bool TryGetOrLazyBuild(string songId, out SongSimilarityVector vector)
    {
        if (TryGet(songId, out vector)) return true;
        if (string.IsNullOrEmpty(songId) || !s_lazyMisses.Add(songId)) return false;
        var profile = LoadCachedProfile(songId);
        if (profile == null) return false;
        Store(songId, profile);
        return TryGet(songId, out vector);
    }

    /// <summary>Caches the vector of a FULL profile for a playable catalog song without a baked vector
    /// (no-op otherwise).</summary>
    public static void Store(string songId, SongProfile profile)
    {
        var v = SongSimilarityVector.Build(songId, profile);
        if (v == null) return;
        EnsureLoaded();
        if (s_published.TryGetValue(songId, out var pub) && pub.IsValid) return; // published vector is authoritative
        if (s_baked.TryGetValue(songId, out var baked) && baked.IsValid) return; // shipped vector is authoritative
        if (s_runtime.TryGetValue(songId, out var old) && SameValues(old, v)) return;
        s_runtime[songId] = v;
        SaveRuntime();
        Debug.Log($"[SongSimilarity] Vector cached for '{songId}'.");
    }

    /// <summary>The published index's vectors (replaces the previous set: a content update may change them).</summary>
    public static void RegisterPublished(IEnumerable<SongSimilarityVector> vectors)
    {
        s_published.Clear();
        if (vectors != null)
            foreach (var v in vectors)
                if (v != null && !string.IsNullOrEmpty(v.songId) && v.IsValid) s_published[v.songId] = v;
        s_lazyMisses.Clear();
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

    /// <summary>The local binary analysis cache entry of a catalog song (SongProfileCache, keyed by songId),
    /// derived features recomputed. It is only checked against the CURRENT analysis params/pipeline: the
    /// audio revision cannot be verified without the audio, which is acceptable for an approximate
    /// similarity vector (never for gameplay). Null when absent or stale.</summary>
    public static SongProfile LoadCachedProfile(string songId)
    {
        if (string.IsNullOrEmpty(songId)) return null;
        try
        {
            var app = Resources.Load<AppConfigSO>("AppConfig");
            if (app == null || app.audioAnalysis == null) return null;
            SongProfileCache.InitOnMainThread();
            string file = SongProfileCache.FindAnyFor(songId);
            if (file == null) return null;
            byte[] bytes = File.ReadAllBytes(file);
            var header = MusicGame.Audio.Analysis.ProfileBinary.ReadHeader(bytes, out _);
            var current = MusicGame.Audio.Analysis.AnalysisParams.Create(SongAnalysisSnapshot.ToSettings(app.audioAnalysis));
            if (header.pipelineVersion != MusicGame.Audio.Analysis.AnalysisVersions.Pipeline || header.paramsHash != current.Hash) return null;
            return SongAnalysisPipeline.LoadProfile(bytes, new DerivedAnalysisSettings(app.audioAnalysis), out _);
        }
        catch (System.Exception e) { Debug.LogWarning($"[SongSimilarity] Cached profile read failed for '{songId}': {e.Message}"); }
        return null;
    }

    /// <summary>Tests / tools: forget the in-memory state (next access reloads from disk/assets).</summary>
    public static void ResetForTests() { s_runtime = null; s_baked = null; s_lazyMisses.Clear(); }
}
