using UnityEngine;

/// <summary>
/// A single Fight rival's IDENTITY — id (stable, used by selection history/saves) and displayName
/// never change — plus one content configuration per PROGRESSION TIER (levels[]; the historical
/// name "level" now means ProgressionTier — see ProgressionConfigSO). Every opponent is eligible at
/// every tier: the tier only chooses WHICH configuration of the opponent is used. Referenced
/// directly (not via Addressables) from OpponentRosterSO — see that class's own doc for why.
///
/// GetConfigForTier is the ONLY thing callers (OpponentSelectionController, eventually Fight's own
/// arena/avatar spawning) need — none of them reason about levels[] itself.
/// </summary>
[CreateAssetMenu(fileName = "OpponentDefinition", menuName = "MusicGame/Fight/Opponent Definition")]
public class OpponentDefinition : ScriptableObject
{
    public string id;
    public string displayName;

    // ── Composer identity — the SAME for every version/tier ─────────────────────────────────────
    [Header("Composer identity (same for every version / tier)")]
    [Tooltip("Real-world standing height in metres. Avatars are built at the canonical 1.75 m and " +
             "uniformly scaled to this (visual only — hurtboxes / hit ranges stay identical for fairness).")]
    [Range(1.4f, 2.1f)] public float heightMeters = 1.70f;
    [Tooltip("True when no reliable historical record exists and heightMeters is an approximation.")]
    public bool heightIsEstimate = true;
    [Tooltip("Localization key of the short COLLECTION biography. Empty = \"Composer.<id>.Bio\".")]
    public string biographyKey;

    /// <summary>Localization key of this composer's biography (UIText table).</summary>
    public string BiographyKey => string.IsNullOrEmpty(biographyKey) ? $"Composer.{id}.Bio" : biographyKey;

    [Tooltip("One entry per PROGRESSION TIER this opponent has content for (entry.level = tier, " +
             "1-based). Entries may share the same assets. Extensible from the Inspector — " +
             "deliberately NOT hardcoded tier1/tier2/... fields. Resolved by GetConfigForTier: the " +
             "highest configured tier <= the requested one (so a tier above authored content reuses " +
             "the highest available).")]
    public OpponentLevelConfig[] levels = System.Array.Empty<OpponentLevelConfig>();

    [Tooltip("Safety-net fallback used ONLY when levels[] has no usable entry at all — " +
             "GetConfigForTier logs a warning whenever this actually triggers.")]
    public OpponentLevelConfig defaultConfig = new OpponentLevelConfig();

    /// <summary>
    /// Deterministic tier resolution — never null:
    ///   1. the entry whose tier == requested tier;
    ///   2. else the HIGHEST configured tier below it (player tier above authored content);
    ///   3. else the LOWEST configured tier (requested tier below every entry);
    ///   4. else defaultConfig (with a warning — levels[] is empty).
    /// resolvedTier reports which tier's entry was used (0 for defaultConfig).
    /// </summary>
    public OpponentLevelConfig GetConfigForTier(int tier, out int resolvedTier)
    {
        OpponentLevelConfig below = null, lowest = null;
        if (levels != null)
            foreach (var candidate in levels)
            {
                if (candidate == null) continue;
                if (candidate.level == tier) { resolvedTier = tier; return candidate; }
                if (candidate.level < tier && (below == null || candidate.level > below.level)) below = candidate;
                if (lowest == null || candidate.level < lowest.level) lowest = candidate;
            }

        var chosen = below ?? lowest;
        if (chosen != null) { resolvedTier = chosen.level; return chosen; }

        Debug.LogWarning($"[OpponentDefinition] '{(string.IsNullOrEmpty(displayName) ? name : displayName)}' " +
                          "has no levels[] entries — falling back to defaultConfig. Add tier entries in the Inspector.");
        resolvedTier = 0;
        return defaultConfig ?? new OpponentLevelConfig();
    }

    public OpponentLevelConfig GetConfigForTier(int tier) => GetConfigForTier(tier, out _);
}

/// <summary>
/// One opponent's content for exactly one PROGRESSION TIER — see OpponentDefinition.levels' own doc.
/// `songs` is the opponent's OWN fight music (roulette snippet + match song), independent of the
/// Runner song the player chose — an opponent is never tied to a Runner song.
/// Difficulty is deliberately a REFERENCE to a separate AIDifficultyProfile asset (shareable across
/// levels/opponents — e.g. one "Easy" profile reused by several early rivals — see that class's
/// own doc) rather than a pile of inline fields here; nothing reads it yet (no AI exists this
/// phase), so it's completely safe to leave unassigned.
/// </summary>
[System.Serializable]
public class OpponentLevelConfig
{
    [Tooltip("Which PROGRESSION TIER (1-based) this entry represents — see OpponentDefinition.GetConfigForTier.")]
    public int level;

    public Sprite portrait;

    [Tooltip("Avatar/visual variant for this level — used only as a fallback when avatarRecipe below " +
             "is unassigned (see FightSceneBootstrap's own doc on the avatarRecipe-first, " +
             "fighterPrefab/capsule-fallback order).")]
    public GameObject fighterPrefab;

    [Tooltip("This level's fully-assembled avatar (body/face/hair/outfit) — when assigned, " +
             "FightSceneBootstrap builds it via AvatarFactory and swaps it onto the Opponent's " +
             "FighterActor.VisualRoot once ready, instead of fighterPrefab/the debug capsule. Null is " +
             "tolerated (falls back to fighterPrefab/capsule exactly as before — task's own explicit " +
             "'no OpponentDefinitions separats per level' note: a level's avatar simply evolves via " +
             "its OWN AvatarRecipeSO, e.g. Rex Level 1/2/3 can each reference a different recipe built " +
             "off the SAME Identity_Rex).")]
    public AvatarRecipeSO avatarRecipe;

    [Tooltip("This level's available tracks — more than one is expected; one is picked at random " +
             "each time (a roulette snippet, and — once locked in — the match song itself).")]
    public AudioClip[] songs = System.Array.Empty<AudioClip>();

    [Tooltip("Not read by anything yet — no AI/combat system exists this phase. Safe to leave unassigned.")]
    public AIDifficultyProfile difficultyProfile;

    [Tooltip("This level's physical combat capability (Strength/Speed/Defense/etc.) — deliberately " +
             "SEPARATE from difficultyProfile above: a rival's raw stats and its AI skill are " +
             "independent (a strong fighter can have weak AI, or the reverse). Null is tolerated — " +
             "FightSceneBootstrap falls back to a flat 100-everywhere FighterStats.")]
    public FighterStatsProfileSO combatStats;

    [Header("Combat (how this tier FIGHTS — never on the avatar)")]
    [Tooltip("Moves + animation style for this tier. Tiers may share one profile. Null = " +
             "FightFlowConfig.defaultOpponentCombatProfile.")]
    public FighterCombatProfileSO combatProfile;
    [Tooltip("Resources this tier's opponent can spend in a match (its own rule — the Player's come from the Runner).")]
    [Min(0)] public int specials = 1;
    [Min(0)] public int tripleCombos;
    [Min(0)] public int quadCombos;

    /// <summary>Null if songs is empty/all-null (expected for a freshly-created level entry) —
    /// callers must tolerate it, same as before. `avoid` is skipped whenever another song exists.</summary>
    public AudioClip GetRandomSong(AudioClip avoid = null)
    {
        if (songs == null || songs.Length == 0) return null;

        int count = 0, avoidable = 0;
        for (int i = 0; i < songs.Length; i++)
            if (songs[i] != null) { count++; if (songs[i] == avoid) avoidable++; }
        if (count == 0) return null;
        if (avoidable == count) avoid = null; // nothing else to pick

        int pick = Random.Range(0, count - (avoid != null ? avoidable : 0));
        for (int i = 0; i < songs.Length; i++)
        {
            if (songs[i] == null || (avoid != null && songs[i] == avoid)) continue;
            if (pick == 0) return songs[i];
            pick--;
        }
        return null; // unreachable
    }
}
