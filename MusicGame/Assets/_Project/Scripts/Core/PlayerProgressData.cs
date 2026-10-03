using System.Collections.Generic;

/// <summary>
/// The PERSISTENT player progression, exactly as written to disk (JsonUtility) by PlayerProgressStore.
/// Plain serializable data only — ids and numbers, never Unity object references — so it survives
/// content changes: an unknown opponent id or level simply stays stored and is ignored by readers.
///
/// Versioning: `version` is the format version this file was written with. New fields are added
/// with a field initializer — JsonUtility leaves fields missing from an older file at that default —
/// and, if a field ever needs converting, PlayerProgressStore.Migrate upgrades old versions in place.
///
/// NOT stored here (run/session state, see RunSession): run score, SPECIAL, temporary bonuses,
/// current song/opponent, run statistics.
/// </summary>
[System.Serializable]
public class PlayerProgressData
{
    public const int CurrentVersion = 4; // 2: song history; 3: pendingRun; 4: song ids = stable songId (was Addressable address)

    public int version = CurrentVersion;

    /// <summary>Persistent progression points (never the run score — see ProgressionConfigSO XP).</summary>
    public int xp;
    /// <summary>Cached level for the stored xp (recomputed from xp + config on load).</summary>
    public int playerLevel = 1;
    /// <summary>Authoritative extra-life inventory: LIFE pickups add, Fight Again consumes.</summary>
    public int extraLives;
    /// <summary>The opponent-difficulty tier progression (completed songs) — the existing
    /// GameProgression rules, now kept across restarts.</summary>
    public GameProgressionState tierProgress = new();
    /// <summary>Every rival VERSION ever defeated, independently (non-linear collection: Lv4 does
    /// not imply Lv1–3).</summary>
    public List<DefeatedOpponentRecord> defeatedOpponents = new();
    /// <summary>Total fights won (informational / future unlocks).</summary>
    public int fightsWon;

    // ── Song history (v2) — song ids are the STABLE PlayableSongCatalogSO songId ("song_007") since
    //    v4 (v2/v3 saves stored Addressable addresses; PlayerProgressStore.Migrate maps them) ──────
    /// <summary>Playable catalog songs completed (Runner → Fight WON), in completion order. Never
    /// repeated in normal progression. Ids no longer in the catalog are kept and simply ignored.</summary>
    public List<string> completedSongIds = new();
    /// <summary>The player's current preference reference: the last MANUALLY chosen catalog song.</summary>
    public string seedSongId = "";
    /// <summary>Cached similarity route around seedSongId (remaining songs at build time) — see SongProgression.</summary>
    public SongRouteData songRoute = new();

    // ── Pending assignment (v3) ──────────────────────────────────────────────────────────────
    /// <summary>The song the player is currently trying to DEFEND + the exact rival assigned to
    /// challenge it, persisted the moment it is assigned (before analysis/Runner). Survives quitting,
    /// a lost fight and Fight Again; cleared only by the victory (or replaced by a confirmed manual
    /// SELECT SONG). Ids only — no song/fight state (a resumed run restarts the song).</summary>
    public PendingRunData pendingRun = new();
}

[System.Serializable]
public class PendingRunData
{
    /// <summary>Stable playable song id (songId). Empty = no pending assignment.</summary>
    public string songId = "";
    /// <summary>OpponentDefinition.id of the assigned rival.</summary>
    public string opponentId = "";
    /// <summary>OpponentLevelConfig.level — the exact rival VERSION.</summary>
    public int opponentLevel;
    /// <summary>Tier the assignment was made at (fake roulette rivals use the same tier).</summary>
    public int opponentTier;

    public bool IsSet => !string.IsNullOrEmpty(songId);
}

/// <summary>Ordered song ids (most similar to the seed first) + the signature of the catalog /
/// vector set it was built from; a different signature means "rebuild".</summary>
[System.Serializable]
public class SongRouteData
{
    public List<string> songIds = new();
    public string signature = "";
}

/// <summary>One rival's defeated versions: stable OpponentDefinition.id + the exact
/// OpponentLevelConfig.level values beaten (unordered set semantics, sorted for readability).</summary>
[System.Serializable]
public class DefeatedOpponentRecord
{
    public string opponentId;
    public List<int> levels = new();
}
