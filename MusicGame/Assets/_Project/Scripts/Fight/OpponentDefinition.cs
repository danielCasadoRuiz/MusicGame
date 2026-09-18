using UnityEngine;

/// <summary>
/// A single Fight rival's IDENTITY — id/displayName never change — plus how its content evolves
/// across the future global Player Level system (see OpponentLevelConfig's own doc). Referenced
/// directly (not via Addressables) from OpponentRosterSO — see that class's own doc for why.
///
/// GetConfigForLevel is the ONLY thing callers (OpponentSelectionController, VersusScreenController,
/// eventually Fight's own arena/avatar spawning) need — none of them reason about levels[] itself.
/// </summary>
[CreateAssetMenu(fileName = "OpponentDefinition", menuName = "MusicGame/Fight/Opponent Definition")]
public class OpponentDefinition : ScriptableObject
{
    public string id;
    public string displayName;

    [Tooltip("One entry per Player Level this opponent has content for — exactly one per level " +
             "(no ranges, no inheritance between levels): whoever authors this asset adds one " +
             "entry per level the game actually has and sets its own portrait/avatar/songs/" +
             "difficulty explicitly. Extensible from the Inspector (add/remove freely) — " +
             "deliberately NOT hardcoded level1/level2/... fields. Resolved by GetConfigForLevel.")]
    public OpponentLevelConfig[] levels = System.Array.Empty<OpponentLevelConfig>();

    [Tooltip("Safety-net fallback used ONLY when playerLevel matches no entry in levels[] at all " +
             "(missing configuration, e.g. this opponent's asset simply hasn't been set up for " +
             "that level yet) — GetConfigForLevel logs a warning whenever this actually triggers, " +
             "so a missing level is easy to notice instead of silently showing empty content.")]
    public OpponentLevelConfig defaultConfig = new OpponentLevelConfig();

    /// <summary>
    /// Exact match: the entry whose OWN level equals playerLevel — never a range or a nearest/
    /// highest-tier guess. Falls back to defaultConfig (with a warning) if none match. Never null.
    /// playerLevel is 0 until the real global Player Level system exists (see LevelContext).
    /// </summary>
    public OpponentLevelConfig GetConfigForLevel(int playerLevel)
    {
        if (levels != null)
        {
            for (int i = 0; i < levels.Length; i++)
            {
                var candidate = levels[i];
                if (candidate != null && candidate.level == playerLevel) return candidate;
            }
        }

        Debug.LogWarning($"[OpponentDefinition] '{(string.IsNullOrEmpty(displayName) ? name : displayName)}' " +
                          $"has no levels[] entry for level {playerLevel} — falling back to defaultConfig. " +
                          "Add one in the Inspector.");
        return defaultConfig ?? new OpponentLevelConfig();
    }
}

/// <summary>
/// One opponent's content for exactly one Player Level — see OpponentDefinition.levels' own doc.
/// Difficulty is deliberately a REFERENCE to a separate AIDifficultyProfile asset (shareable across
/// levels/opponents — e.g. one "Easy" profile reused by several early rivals — see that class's
/// own doc) rather than a pile of inline fields here; nothing reads it yet (no AI exists this
/// phase), so it's completely safe to leave unassigned.
/// </summary>
[System.Serializable]
public class OpponentLevelConfig
{
    [Tooltip("Which Player Level this entry represents — GetConfigForLevel matches this exactly, no ranges.")]
    public int level;

    public Sprite portrait;

    [Tooltip("Avatar/visual variant for this level — unused until Fight actually renders a real fighter instead of a placeholder capsule.")]
    public GameObject fighterPrefab;

    [Tooltip("This level's available tracks — more than one is expected; one is picked at random " +
             "each time (a roulette snippet, and — once locked in — the match song itself).")]
    public AudioClip[] songs = System.Array.Empty<AudioClip>();

    [Tooltip("Not read by anything yet — no AI/combat system exists this phase. Safe to leave unassigned.")]
    public AIDifficultyProfile difficultyProfile;

    /// <summary>Null if songs is empty/all-null (expected for a freshly-created level entry) —
    /// callers must tolerate it, same as before.</summary>
    public AudioClip GetRandomSong()
    {
        if (songs == null || songs.Length == 0) return null;

        int count = 0;
        for (int i = 0; i < songs.Length; i++)
            if (songs[i] != null) count++;
        if (count == 0) return null;

        int pick = Random.Range(0, count);
        for (int i = 0; i < songs.Length; i++)
        {
            if (songs[i] == null) continue;
            if (pick == 0) return songs[i];
            pick--;
        }
        return null; // unreachable
    }
}
