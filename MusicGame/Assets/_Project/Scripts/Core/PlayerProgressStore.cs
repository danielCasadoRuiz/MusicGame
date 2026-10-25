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

    /// <summary>Format version of the file last loaded (before migration) — 0 if none.</summary>
    public static int LoadedVersion { get; private set; }

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
            LoadedVersion = loadedVersion;
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

    private static void SeedPlayedHistory(PlayerProgressData data)
    {
        void Add(string id) { if (!string.IsNullOrEmpty(id) && !data.playedSongIds.Contains(id)) data.playedSongIds.Add(id); }
        foreach (var id in data.completedSongIds) Add(id);
        foreach (var id in data.songCycle.consumedSongIds) Add(id);
        foreach (var p in data.campaign.positions)
            if (p != null)
                foreach (var rec in new[] { p.bestRunner, p.lastRunner, p.bestCombat, p.lastCombat })
                    if (rec != null && rec.has) Add(rec.songId);
    }

    /// <summary>In-memory migration of a loaded save (also used by the batch rule tests).</summary>
    public static void Migrate(PlayerProgressData data)
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
        // v4 → v5: campaign + song cycles. The song cycle starts as "every song won so far is consumed
        // in cycle 1" (exactly the old exclusion rule; it renews itself once all are consumed). The
        // campaign POSITION placement needs the CampaignConfigSO, so it happens in PlayerProgressService
        // .Configure (campaign.initialized = false → CampaignModel.MigrateLegacy with the won-match count).
        data.campaign ??= new CampaignProgressData();
        data.campaign.positions ??= new System.Collections.Generic.List<PositionRecordData>();
        data.campaign.currentLevelRivals ??= new System.Collections.Generic.List<string>();
        data.campaign.boss ??= new BossProgressData();
        data.campaign.boss.stages ??= new System.Collections.Generic.List<BossStageData>();
        data.campaign.levelId ??= "";
        if (data.songCycle == null || data.version < 5)
        {
            data.songCycle ??= new SongCycleData();
            if (data.version < 5) { data.songCycle.cycle = 1; data.songCycle.consumedSongIds = new System.Collections.Generic.List<string>(data.completedSongIds); }
        }
        data.songCycle.consumedSongIds ??= new System.Collections.Generic.List<string>();
        data.pendingRun.levelId ??= "";
        if (data.version < 5) data.pendingRun.bossStage = -1; // JsonUtility leaves missing ints at 0 = boss stage 0
        // v6 → v7: the played history did not exist — rebuilt from what the save already proves was played: songs won
        // (completedSongIds), songs used this lap, and every runner / combat record's song (any lap).
        data.playedSongIds ??= new System.Collections.Generic.List<string>();
        if (data.version < 7) SeedPlayedHistory(data);
        data.playedSongIds.RemoveAll(string.IsNullOrEmpty);
        // v7 → v8: the player profile starts empty (no build was ever saved before).
        data.profile ??= new PlayerProfileData();
        data.profile.stylePicks ??= new System.Collections.Generic.List<StylePickData>();
    }
}
