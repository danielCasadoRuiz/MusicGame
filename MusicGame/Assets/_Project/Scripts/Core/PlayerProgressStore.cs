using System.IO;
using UnityEngine;

/// <summary>
/// Local save file for PlayerProgressData: JSON at persistentDataPath/player_progress.json.
/// Writes are atomic (temp file → replace, previous kept as .bak) so closing the game mid-write never
/// corrupts progress; a missing or unreadable file loads as fresh defaults (and an unreadable one is
/// set aside as .corrupt for inspection instead of being overwritten silently).
/// Delete() exists for a future "Reset Progress" option — nothing calls it yet.
/// </summary>
public static class PlayerProgressStore
{
    private const string FileName = "player_progress.json";

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    public static bool Exists => File.Exists(FilePath);

    /// <summary>Loads the save, or null when none exists / it cannot be read.</summary>
    public static PlayerProgressData Load()
    {
        string path = FilePath;
        if (!File.Exists(path)) return null;
        try
        {
            var data = JsonUtility.FromJson<PlayerProgressData>(File.ReadAllText(path));
            if (data == null) throw new IOException("empty save");
            int loadedVersion = data.version;
            Migrate(data);
            // Persist the migrated format once (not if the song-id mapping could not run: no catalog).
            if (loadedVersion < PlayerProgressData.CurrentVersion && SongCatalog.IsAvailable) Save(data);
            return data;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerProgressStore] Could not read '{path}' ({e.Message}) — keeping it as .corrupt and starting fresh.");
            try { File.Copy(path, path + ".corrupt", true); } catch { /* best effort */ }
            return null;
        }
    }

    public static void Save(PlayerProgressData data)
    {
        if (data == null) return;
        string path = FilePath;
        try
        {
            data.version = PlayerProgressData.CurrentVersion;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PlayerProgressStore] Save failed ({e.Message}) — progress kept in memory only.");
        }
    }

    /// <summary>For a future "Reset Progress" option.</summary>
    public static void Delete()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    /// <summary>Upgrades older formats in place. Fields added later with an initializer need no code
    /// here (JsonUtility keeps their default); only conversions do. Also repairs nulls.</summary>
    private static void MigrateSongIdsToStable(PlayerProgressData data)
    {
        if (!SongCatalog.IsAvailable)
        {
            Debug.LogError("[PlayerProgressStore] No playable song catalog — cannot migrate song ids to stable ids.");
            return;
        }
        string Map(string id) => string.IsNullOrEmpty(id) || SongCatalog.IsKnownId(id) ? id : (SongCatalog.IdForAddress(id) ?? id);
        int mapped = 0;
        for (int i = 0; i < data.completedSongIds.Count; i++)
        {
            string m = Map(data.completedSongIds[i]);
            if (m != data.completedSongIds[i]) { data.completedSongIds[i] = m; mapped++; }
        }
        var unique = new System.Collections.Generic.List<string>();
        foreach (var id in data.completedSongIds) if (!unique.Contains(id)) unique.Add(id);
        data.completedSongIds = unique;
        data.seedSongId = Map(data.seedSongId);
        data.songRoute.songIds = data.songRoute.songIds.ConvertAll(Map);
        data.songRoute.signature = "";
        data.pendingRun.songId = Map(data.pendingRun.songId);
        Debug.Log($"[PlayerProgressStore] Migrated save v{data.version} → v4 stable song ids ({mapped} completed id(s) remapped).");
    }

    private static void Migrate(PlayerProgressData data)
    {
        data.tierProgress ??= new GameProgressionState();
        data.defeatedOpponents ??= new System.Collections.Generic.List<DefeatedOpponentRecord>();
        data.defeatedOpponents.RemoveAll(r => r == null || string.IsNullOrEmpty(r.opponentId));
        foreach (var r in data.defeatedOpponents) r.levels ??= new System.Collections.Generic.List<int>();
        data.xp = Mathf.Max(0, data.xp);
        data.extraLives = Mathf.Max(0, data.extraLives);
        // v1 → v2: song history did not exist — empty history, no seed, no route (rebuilt on demand).
        data.completedSongIds ??= new System.Collections.Generic.List<string>();
        data.completedSongIds.RemoveAll(string.IsNullOrEmpty);
        data.seedSongId ??= "";
        data.songRoute ??= new SongRouteData();
        data.songRoute.songIds ??= new System.Collections.Generic.List<string>();
        data.songRoute.signature ??= "";
        // v2 → v3: no pending assignment (content validity is checked on CONTINUE — RunAssignment).
        data.pendingRun ??= new PendingRunData();
        data.pendingRun.songId ??= "";
        data.pendingRun.opponentId ??= "";
        // v3 → v4: song ids were Addressable addresses — map each to its stable songId through the
        // playable catalog (deterministic). An id already stable or unknown is kept as-is (ignored by
        // readers if it no longer exists). The route is re-keyed and its signature cleared (rebuilt).
        if (data.version < 4) MigrateSongIdsToStable(data);
    }
}
