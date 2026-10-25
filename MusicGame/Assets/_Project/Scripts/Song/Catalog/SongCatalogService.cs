using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Runtime owner of the published song index (SongCatalogIndexSO). Loaded once, AFTER ContentDownloadManager
/// finished Addressables init + the startup catalog check (so a remote update is never raced), and re-read
/// after a later catalog update (ContentCatalogUpdatedEvent). On load it feeds:
///   SongCatalog           — songId ⇄ address (a song added by content gets its identity + progression);
///   SongSimilarityCache   — the published similarity vectors (routes for new songs too).
/// When the index cannot be loaded (offline before anything was ever downloaded) the player-shipped
/// PlayableSongCatalogSO keeps naming the songs, but catalog songs cannot be PREPARED without their
/// published entry (CatalogSongLoader reports IndexUnavailable) — never a silent analysis instead.
/// The index handle is held for the app lifetime (a few KB) and released when superseded.
/// </summary>
public static class SongCatalogService
{
    private static SongCatalogIndexSO s_index;
    private static AsyncOperationHandle<SongCatalogIndexSO> s_handle;
    private static readonly Dictionary<string, SongCatalogIndexEntry> s_entries = new();
    private static bool s_loading;
    private static bool s_stale = true;
    private static System.Action<ContentCatalogUpdatedEvent> s_onCatalogUpdated;

    public static bool IndexAvailable => s_index != null;
    public static SongCatalogIndexSO Index => s_index;
    /// <summary>Last load failure (null when the index is loaded).</summary>
    public static string LastError { get; private set; }

    /// <summary>Coroutine: the index is loaded (or definitively unavailable for now). Concurrent callers share
    /// one load. Never throws; never blocks a frame.</summary>
    public static IEnumerator EnsureReady()
    {
        HookCatalogUpdates();
        var cdm = ContentDownloadManager.Instance;
        if (cdm != null) { var init = cdm.Initialization; while (!init.IsCompleted) yield return null; }

        while (s_loading) yield return null;
        if (!s_stale && s_index != null) yield break;

        s_loading = true;
        try
        {
            var handle = Addressables.LoadAssetAsync<SongCatalogIndexSO>(SongContentKeys.IndexAddress);
            yield return handle;
            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
            {
                var previous = s_handle;
                Apply(handle.Result);
                s_handle = handle;
                if (previous.IsValid()) Addressables.Release(previous); // new index applied first, then the old one released
                s_stale = false;
                LastError = null;
                Debug.Log($"[SongCatalogService] Published index: {s_index.songs.Count} songs (revision {s_index.contentRevision}).");
            }
            else
            {
                LastError = handle.OperationException != null ? handle.OperationException.Message : "index not found";
                if (handle.IsValid()) Addressables.Release(handle);
                Debug.LogWarning($"[SongCatalogService] Published song index unavailable ({LastError}) — " +
                                 (s_index != null ? "keeping the previous one." : "catalog songs cannot be prepared until it loads."));
            }
        }
        finally { s_loading = false; }
    }

    public static bool TryGetEntry(string songId, out SongCatalogIndexEntry entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(songId) || !s_entries.TryGetValue(songId, out var e)) return false;
        entry = e.Clone(); // callers keep their own copy: a later index swap never changes an in-flight request
        return true;
    }

    /// <summary>songId → address pairs of the published index (SongCatalog merges them over the shipped table).</summary>
    public static IEnumerable<SongCatalogIndexEntry> Entries => s_entries.Values;

    private static void Apply(SongCatalogIndexSO index)
    {
        s_index = index;
        s_entries.Clear();
        var vectors = new List<SongSimilarityVector>();
        foreach (var e in index.songs)
        {
            if (e == null || string.IsNullOrEmpty(e.songId)) continue;
            s_entries[e.songId] = e;
            if (e.similarity != null && e.similarity.IsValid) vectors.Add(e.similarity);
        }
        SongCatalog.Reset();                         // re-merge ids/addresses on next access
        SongSimilarityCache.RegisterPublished(vectors);
    }

    private static void HookCatalogUpdates()
    {
        if (s_onCatalogUpdated != null) return;
        s_onCatalogUpdated = _ => s_stale = true; // re-read on the next EnsureReady (never mid-request)
        EventBus.Subscribe(s_onCatalogUpdated);
    }

    /// <summary>Tests/probes: replace one published entry IN MEMORY (e.g. a tampered revision or a missing
    /// profile address) to exercise the incompatible-content path. Never persisted; ResetForTests or the
    /// next index load restores the published entries.</summary>
    public static void OverrideEntryForTests(SongCatalogIndexEntry entry)
    {
        if (entry != null && !string.IsNullOrEmpty(entry.songId)) s_entries[entry.songId] = entry;
    }

    /// <summary>Tests/probes: forget the loaded index (releases its handle).</summary>
    public static void ResetForTests()
    {
        if (s_handle.IsValid()) Addressables.Release(s_handle);
        s_handle = default;
        s_index = null;
        s_entries.Clear();
        s_stale = true;
        SongCatalog.Reset();
    }
}

/// <summary>ContentDownloadManager applied a newer remote catalog (content update).</summary>
public struct ContentCatalogUpdatedEvent { public int CatalogCount; }
