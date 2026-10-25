using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Identity of every built-in PLAYABLE song (Assets/_Project/Audio/Music/**):
///   songId  — the persistent, immutable identity ("song_007"): used by PlayerProgress
///             (completedSongIds, seedSongId, songRoute, pendingRun) and the similarity catalog;
///   address — HOW the clip is loaded (its Addressable address, label "Song") — may change.
/// Maintained ONLY by Tools > MusicGame > Setup Song Addressables, which reconnects a renamed/moved
/// clip through its Unity GUID (editorGuid, Editor-only bookkeeping — never persisted in saves),
/// preserves existing ids and gives a new id only to a genuinely new song. Ids are never reused:
/// nextIdNumber only grows. Composer/opponent music is never listed here.
/// Referenced by AppConfigSO.songCatalog; read at runtime through SongCatalog.
/// </summary>
[CreateAssetMenu(fileName = "PlayableSongCatalog", menuName = "MusicGame/Song/Playable Song Catalog")]
public class PlayableSongCatalogSO : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string songId;
        public string address;
        [Tooltip("Editor-only: the AudioClip GUID used to reconnect a moved/renamed asset to its songId.")]
        public string editorGuid;
    }

    public List<Entry> songs = new();
    [Tooltip("Next number for a NEW song id (song_NNN). Only ever grows, so ids are never reused.")]
    public int nextIdNumber = 1;

    public static string FormatId(int n) => $"song_{n:000}";
}

/// <summary>
/// Runtime resolution songId ⇄ Addressable address. Source of truth at runtime = the PUBLISHED remote index
/// (SongCatalogService, loaded after the catalog update) — so a song added by a content update has an
/// identity, progression and similarity without a new player. The player-shipped AppConfig.songCatalog is
/// only the fallback naming while the index is not available (offline before the first download), and
/// stays the editor's id allocator (SongAddressablesSetup).
/// </summary>
public static class SongCatalog
{
    private static Dictionary<string, string> s_idToAddress, s_addressToId, s_displayNames;

    private static void EnsureLoaded()
    {
        if (s_idToAddress != null) return;
        s_idToAddress = new Dictionary<string, string>();
        s_addressToId = new Dictionary<string, string>();
        s_displayNames = new Dictionary<string, string>();
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var catalog = app != null ? app.songCatalog : null;
        if (catalog != null)
            foreach (var e in catalog.songs)
            {
                if (e == null || string.IsNullOrEmpty(e.songId)) continue;
                // Loaded by the ASCII address derived from the id; the authored address is the display name.
                string address = SongContentKeys.AudioAddress(e.songId);
                s_idToAddress[e.songId] = address;
                s_addressToId[address] = e.songId;
                if (!string.IsNullOrEmpty(e.address)) s_displayNames[e.songId] = e.address;
            }
        // The published index wins (it is the content actually being played; it may add or rename songs).
        int published = 0;
        foreach (var e in SongCatalogService.Entries)
        {
            if (e == null || string.IsNullOrEmpty(e.songId) || string.IsNullOrEmpty(e.audioAddress)) continue;
            if (s_idToAddress.TryGetValue(e.songId, out var oldAddress)) s_addressToId.Remove(oldAddress);
            s_idToAddress[e.songId] = e.audioAddress;
            s_addressToId[e.audioAddress] = e.songId;
            if (!string.IsNullOrEmpty(e.displayName)) s_displayNames[e.songId] = e.displayName;
            published++;
        }
        if (catalog == null && published == 0)
            Debug.LogWarning("[SongCatalog] No published song index and no AppConfig.songCatalog — run Tools > MusicGame > Setup Song Addressables.");
    }

    /// <summary>Stable id of a loaded catalog location (its address), or null if not a catalog song.</summary>
    public static string IdForAddress(string address)
    {
        EnsureLoaded();
        return address != null && s_addressToId.TryGetValue(address, out var id) ? id : null;
    }

    public static string AddressForId(string songId)
    {
        EnsureLoaded();
        return songId != null && s_idToAddress.TryGetValue(songId, out var a) ? a : null;
    }

    /// <summary>Published display name (falls back to the address).</summary>
    public static string DisplayNameForAddress(string address) => DisplayNameFor(IdForAddress(address)) ?? address;

    public static string DisplayNameFor(string songId)
    {
        EnsureLoaded();
        if (songId == null) return null;
        return s_displayNames.TryGetValue(songId, out var n) ? n : AddressForId(songId);
    }

    public static bool IsAvailable { get { EnsureLoaded(); return s_idToAddress.Count > 0; } }

    public static bool IsKnownId(string songId) { EnsureLoaded(); return songId != null && s_idToAddress.ContainsKey(songId); }

    /// <summary>Tools/tests and index reloads: drop the cache (next access re-reads the shipped table + index).</summary>
    public static void Reset() { s_idToAddress = null; s_addressToId = null; s_displayNames = null; }
}
