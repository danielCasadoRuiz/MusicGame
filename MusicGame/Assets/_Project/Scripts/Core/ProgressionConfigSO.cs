using UnityEngine;

/// <summary>
/// Game progression balancing (AppConfigSO.progression). A completed SONG (one full Runner → Fight
/// cycle, counted when the match is WON — see GameSession.RegisterCompletedSong) is NOT a tier:
/// the player advances one ProgressionTier every songsPerTier completed songs.
///   tier (1-based) = completedSongs / songsPerTier + 1      (0–4 → 1, 5–9 → 2, 10–14 → 3, ...)
/// Nothing at runtime caps the tier by the current song catalog or authored content: an opponent
/// without content for a tier resolves to its highest configured tier ≤ it (OpponentDefinition).
/// </summary>
[CreateAssetMenu(fileName = "ProgressionConfig", menuName = "MusicGame/App/Progression Config")]
public class ProgressionConfigSO : ScriptableObject
{
    [Tooltip("Completed songs needed to advance one progression tier (constant for every tier for now).")]
    [Min(1)] public int songsPerTier = 5;

    [Tooltip("Opponent anti-repetition: the same opponent is not picked again until at least this many " +
             "OTHER opponents have appeared (also across shuffle-bag refills). 0 = only the bag rule.")]
    [Min(0)] public int recentOpponentCooldown = 2;

    [Tooltip("INFORMATIONAL — content tiers seeded for each opponent when the roster was set up " +
             "(ceil(song catalog size / songsPerTier)). Runtime never reads it to cap anything.")]
    [Min(1)] public int seededContentTiers = 4;

    // ── Player XP / Level (persistent — PlayerProgressService). XP is NOT the run score. ──────────
    [Header("Player XP / Level (persistent; separate from the run score)")]
    [Tooltip("XP for finishing a Runner run, scaled by its normalized score (0..1 of the song's max).")]
    [Min(0)] public int xpPerRun = 100;
    [Tooltip("Minimum XP for any finished run, however it went.")]
    [Min(0)] public int xpPerRunMinimum = 10;
    [Tooltip("XP for winning a fight.")]
    [Min(0)] public int xpPerFightWin = 50;
    [Tooltip("Extra XP the FIRST time a given rival version is defeated.")]
    [Min(0)] public int xpFirstRivalVersion = 75;
    [Tooltip("XP needed to go from level 1 to 2.")]
    [Min(1)] public int xpForLevel2 = 200;
    [Tooltip("Each further level needs this much more than the previous one (1 = flat).")]
    [Min(1f)] public float xpLevelGrowth = 1.3f;

    /// <summary>Total XP needed to REACH `level` (level 1 = 0).</summary>
    public int TotalXpForLevel(int level)
    {
        double total = 0, step = Mathf.Max(1, xpForLevel2);
        for (int l = 2; l <= level; l++) { total += step; step *= Mathf.Max(1f, xpLevelGrowth); }
        return (int)System.Math.Min(int.MaxValue, System.Math.Round(total));
    }

    /// <summary>1-based player level for a total XP amount.</summary>
    public int LevelForXp(int xp)
    {
        int level = 1;
        while (level < 999 && TotalXpForLevel(level + 1) <= xp) level++;
        return level;
    }

    /// <summary>XP a finished run grants (never the raw score — see xpPerRun).</summary>
    public int XpForRun(float normalizedScore) =>
        Mathf.Max(xpPerRunMinimum, Mathf.RoundToInt(xpPerRun * Mathf.Clamp01(normalizedScore)));

    /// <summary>1-based tier for a completed-song count.</summary>
    public int TierForCompletedSongs(int completedSongs) => Mathf.Max(0, completedSongs) / Mathf.Max(1, songsPerTier) + 1;

    /// <summary>Songs needed to complete `tier` (constant today — the single place escalating
    /// requirements would be implemented later).</summary>
    public int SongsRequiredForTier(int tier) => Mathf.Max(1, songsPerTier);
}
