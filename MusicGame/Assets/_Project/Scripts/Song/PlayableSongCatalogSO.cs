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

/// <summary>Runtime resolution songId ⇄ Addressable address (cached from AppConfig.songCatalog).</summary>
public static class SongCatalog
{
    private static Dictionary<string, string> s_idToAddress, s_addressToId;

    private static void EnsureLoaded()
    {
        if (s_idToAddress != null) return;
        s_idToAddress = new Dictionary<string, string>();
        s_addressToId = new Dictionary<string, string>();
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var catalog = app != null ? app.songCatalog : null;
        if (catalog == null) { Debug.LogWarning("[SongCatalog] No AppConfig.songCatalog — run Tools > MusicGame > Setup Song Addressables."); return; }
        foreach (var e in catalog.songs)
        {
            if (e == null || string.IsNullOrEmpty(e.songId) || string.IsNullOrEmpty(e.address)) continue;
            s_idToAddress[e.songId] = e.address;
            s_addressToId[e.address] = e.songId;
        }
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

    public static bool IsAvailable { get { EnsureLoaded(); return s_idToAddress.Count > 0; } }

    public static bool IsKnownId(string songId) { EnsureLoaded(); return songId != null && s_idToAddress.ContainsKey(songId); }

    /// <summary>Tools/tests: drop the cache (next access re-reads the asset).</summary>
    public static void Reset() { s_idToAddress = null; s_addressToId = null; }
}
