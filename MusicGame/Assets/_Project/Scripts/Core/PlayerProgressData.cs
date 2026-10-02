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
    public const int CurrentVersion = 1;

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
}

/// <summary>One rival's defeated versions: stable OpponentDefinition.id + the exact
/// OpponentLevelConfig.level values beaten (unordered set semantics, sorted for readability).</summary>
[System.Serializable]
public class DefeatedOpponentRecord
{
    public string opponentId;
    public List<int> levels = new();
}
