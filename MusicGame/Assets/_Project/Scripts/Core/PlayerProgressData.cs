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
    public const int CurrentVersion = 7; // 2: song history; 3: pendingRun; 4: stable songId; 5: campaign + song cycles + records; 6: song use per lap; 7: played history

    public int version = CurrentVersion;

    /// <summary>LEGACY (saves ≤ v5 written before the XP system was retired): old XP total. Never read or
    /// written any more — the campaign (CampaignProgressData) is THE progression. Kept so old files
    /// round-trip unchanged; a stored value is simply ignored.</summary>
    public int xp;
    /// <summary>LEGACY: old XP-derived level. Ignored (see xp).</summary>
    public int playerLevel = 1;
    /// <summary>Authoritative extra-life inventory: LIFE pickups add, Fight Again consumes. Their ONE
    /// role: a retry of a lost fight (Fight Again) without replaying the song.</summary>
    public int extraLives;
    /// <summary>The opponent-difficulty tier progression (completed songs) — the existing
    /// GameProgression rules, now kept across restarts.</summary>
    public GameProgressionState tierProgress = new();
    /// <summary>Every rival VERSION ever defeated, independently (non-linear collection: Lv4 does
    /// not imply Lv1–3).</summary>
    public List<DefeatedOpponentRecord> defeatedOpponents = new();
    /// <summary>Total fights won (plain statistic; repairs pre-campaign saves — no rank or level is derived from it).</summary>
    public int fightsWon;

    // ── Song history (v2) — song ids are the STABLE PlayableSongCatalogSO songId ("song_007") since
    //    v4 (v2/v3 saves stored Addressable addresses; PlayerProgressStore.Migrate maps them) ──────
    /// <summary>Playable catalog songs completed (Runner → Fight WON), in completion order. Never
    /// repeated in normal progression. Ids no longer in the catalog are kept and simply ignored.</summary>
    public List<string> completedSongIds = new();
    /// <summary>Playable catalog songs PLAYED at least once (the Runner finished, whatever the fight did), in first-play
    /// order (v7). All-time history for the Song Selection check only — never decides availability (that is
    /// songCycle, per lap) and never cleared by a new lap.</summary>
    public List<string> playedSongIds = new();
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

    // ── Campaign (v5) — see CampaignProgressData / CampaignService ──────────────────────────
    /// <summary>Lap, current level + position, per-position best records, final rival progress, seal.</summary>
    public CampaignProgressData campaign = new();
    /// <summary>Song variety cycle (independent of laps). completedSongIds above stays the all-time history.</summary>
    public SongCycleData songCycle = new();
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

    // v5: WHERE this assignment belongs in the campaign (or which final-rival stage).
    /// <summary>Campaign lap the assignment was made in.</summary>
    public int lap;
    /// <summary>CampaignLevelDefinition.id + 1-based match number = the scorable position ("L2#3").</summary>
    public string levelId = "";
    public int matchNumber;
    /// <summary>≥ 0: a final-rival stage (no song / no runner); -1: a normal campaign match.</summary>
    public int bossStage = -1;
    /// <summary>LIFE pickups already turned into extra lives by runs of THIS assignment (the best attempt so
    /// far). A replay / restart / resumed run of the same match only grants lives beyond it, so repeating
    /// a match never farms lives (RunRewardRules.GrantsLife). Missing in older files → 0 (safe).</summary>
    public int livesGranted;
    /// <summary>Seed of THIS match's Runner pickup distribution (RunnerPickupLayout): drawn when the
    /// assignment is created, reused by every resume / replay / restart of it. 0 = none yet (older
    /// files) → one is drawn and saved the first time the run is restored.</summary>
    public int pickupSeed;
    /// <summary>RunnerPickupLayout.GeneratorVersion the seed was last generated with (0 = unknown).</summary>
    public int pickupLayoutVersion;

    public bool IsSet => !string.IsNullOrEmpty(songId) || bossStage >= 0;
    public bool IsBoss => bossStage >= 0;
    public string PositionId => string.IsNullOrEmpty(levelId) || matchNumber <= 0 ? "" : CampaignConfigSO.PositionId(levelId, matchNumber);
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
