using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

public enum NextSongStatus { Found, NoSeed, CatalogComplete, EmptyCatalog }

/// <summary>No uncompleted PLAYABLE song remains. Hook for a future endgame (repeat / new cycle) —
/// today the flow simply returns to the Main Menu, which shows "catalog complete".</summary>
public struct SongCatalogCompletedEvent { }

/// <summary>
/// Automatic song progression over the PLAYABLE catalog ("Song" Addressables label = Music folder only).
///   available catalog − completedSongIds, ordered by the similarity route around seedSongId → first.
/// Persistent ids live in PlayerProgress (completedSongIds, seedSongId, songRoute); vectors live in
/// SongSimilarityCache; the comparison itself in SongSimilarityService.
///
/// The route is built ONCE per (seed, catalog, vector set) — its signature — from the seed + the
/// REMAINING songs, and then only walked. A manual choice (Song Selection) sets a new seed and rebuilds
/// around the remaining content. A catalog change (song added/removed) or a newly cached vector changes
/// the signature, so the next resolve rebuilds it — no save deletion ever needed. Unknown saved ids are
/// ignored.
/// </summary>
public static class SongProgression
{
    public const string SongLabel = "Song";

    /// <summary>Set by the last resolve: true when every playable song is completed.</summary>
    public static bool CatalogComplete { get; private set; }

    /// <summary>The playable catalog's locations (the "Song" label).</summary>
    public static IEnumerator LoadCatalog(System.Action<List<IResourceLocation>> onLoaded)
    {
        var list = new List<IResourceLocation>();
        AsyncOperationHandle<IList<IResourceLocation>> handle = Addressables.LoadResourceLocationsAsync(SongLabel, typeof(AudioClip));
        yield return handle;
        if (handle.Status == AsyncOperationStatus.Succeeded) list.AddRange(handle.Result);
        else Debug.LogWarning("[SongProgression] Failed to query the 'Song' Addressables label.");
        Addressables.Release(handle);
        onLoaded?.Invoke(list);
    }

    /// <summary>Distinct stable ids (Addressable addresses), catalog order.</summary>
    public static List<string> Ids(IEnumerable<IResourceLocation> locations)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>();
        if (locations != null)
            foreach (var l in locations)
                if (l != null && !string.IsNullOrEmpty(l.PrimaryKey) && seen.Add(l.PrimaryKey)) ids.Add(l.PrimaryKey);
        return ids;
    }

    public static IResourceLocation Find(IEnumerable<IResourceLocation> locations, string songId)
    {
        if (locations != null) foreach (var l in locations) if (l != null && l.PrimaryKey == songId) return l;
        return null;
    }

    public static bool IsCompleted(string songId) =>
        PlayerProgressService.Instance != null && PlayerProgressService.Instance.IsSongCompleted(songId);

    /// <summary>A MANUAL catalog choice: becomes the new preference seed; the route is rebuilt now
    /// around the seed + the remaining (uncompleted) songs.</summary>
    public static void OnManualSelection(string songId, IReadOnlyList<string> catalogIds)
    {
        var progress = PlayerProgressService.Instance;
        if (progress == null || string.IsNullOrEmpty(songId)) return;
        if (progress.SeedSongId != songId) progress.SetSongSeed(songId);
        Rebuild(progress, songId, catalogIds, force: true);
    }

    /// <summary>CONTINUE / post-win: the first route entry that is in the catalog and not completed.</summary>
    public static NextSongStatus ResolveNext(IReadOnlyList<string> catalogIds, out string songId)
    {
        songId = null;
        var progress = PlayerProgressService.Instance;
        if (progress == null || catalogIds == null || catalogIds.Count == 0) return NextSongStatus.EmptyCatalog;

        var completed = new HashSet<string>(progress.CompletedSongIds);
        var catalog   = new HashSet<string>(catalogIds);
        bool anyLeft = false;
        foreach (var id in catalogIds) if (!completed.Contains(id)) { anyLeft = true; break; }
        CatalogComplete = !anyLeft;
        if (CatalogComplete)
        {
            Debug.Log("[SongProgression] Every playable song is completed — catalog complete.");
            EventBus.Publish(new SongCatalogCompletedEvent());
            return NextSongStatus.CatalogComplete;
        }

        // Seed: the manual preference; if it vanished from the catalog (or an old save has none),
        // the most recently completed song still in the catalog.
        string seed = catalog.Contains(progress.SeedSongId) ? progress.SeedSongId : null;
        if (seed == null)
            for (int i = progress.CompletedSongIds.Count - 1; i >= 0 && seed == null; i--)
                if (catalog.Contains(progress.CompletedSongIds[i])) seed = progress.CompletedSongIds[i];
        if (seed == null) return NextSongStatus.NoSeed;
        if (seed != progress.SeedSongId) progress.SetSongSeed(seed);

        Rebuild(progress, seed, catalogIds, force: false);
        songId = FirstRemaining(progress.SongRoute.songIds, catalog, completed);
        if (songId == null) // stale route (shouldn't happen: same signature ⇒ same catalog) — rebuild once
        {
            Rebuild(progress, seed, catalogIds, force: true);
            songId = FirstRemaining(progress.SongRoute.songIds, catalog, completed);
        }
        Debug.Log($"[SongProgression] Next song '{songId}' (seed '{seed}', {completed.Count} completed).");
        return songId != null ? NextSongStatus.Found : NextSongStatus.CatalogComplete;
    }

    public static string FirstRemaining(IReadOnlyList<string> route, HashSet<string> catalog, HashSet<string> completed)
    {
        if (route != null) foreach (var id in route) if (catalog.Contains(id) && !completed.Contains(id)) return id;
        return null;
    }

    /// <summary>Version + seed + every catalog id with whether it has a vector — any difference (song
    /// added/removed, new vector cached, seed changed, vector layout bumped) means rebuild.</summary>
    public static string Signature(string seed, IReadOnlyList<string> catalogIds, System.Func<string, bool> hasVector)
    {
        var ids = new List<string>(catalogIds);
        ids.Sort(string.CompareOrdinal);
        var sb = new System.Text.StringBuilder($"v{SongSimilarityVector.CurrentVersion}|{seed}|");
        foreach (var id in ids) sb.Append(id).Append(hasVector(id) ? ":1;" : ":0;");
        unchecked
        {
            ulong h = 14695981039346656037UL; // FNV-1a 64
            foreach (char c in sb.ToString()) { h ^= c; h *= 1099511628211UL; }
            return $"{ids.Count}-{h:x16}";
        }
    }

    private static void Rebuild(PlayerProgressService progress, string seed, IReadOnlyList<string> catalogIds, bool force)
    {
        if (catalogIds == null) return;
        var vectors = new Dictionary<string, SongSimilarityVector>();
        foreach (var id in catalogIds)
            if (SongSimilarityCache.TryGetOrLazyBuild(id, out var v)) vectors[id] = v;

        string signature = Signature(seed, catalogIds, vectors.ContainsKey);
        if (!force && progress.SongRoute.signature == signature && progress.SongRoute.songIds.Count > 0) return;

        var remaining = new List<string>();
        foreach (var id in catalogIds) if (!progress.IsSongCompleted(id)) remaining.Add(id);
        var route = SongSimilarityService.BuildRoute(seed, remaining, id => vectors.TryGetValue(id, out var x) ? x : null,
                                                     new List<SongSimilarityVector>(vectors.Values));
        progress.SetSongRoute(route, signature);
        Debug.Log($"[SongProgression] Route rebuilt around '{seed}' ({route.Count} remaining, {vectors.Count}/{catalogIds.Count} vectors): " +
                  string.Join(" → ", route.GetRange(0, Mathf.Min(5, route.Count))) + (route.Count > 5 ? " …" : ""));
    }
}
