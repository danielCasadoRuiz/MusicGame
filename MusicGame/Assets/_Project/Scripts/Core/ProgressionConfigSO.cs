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

    /// <summary>1-based tier for a completed-song count.</summary>
    public int TierForCompletedSongs(int completedSongs) => Mathf.Max(0, completedSongs) / Mathf.Max(1, songsPerTier) + 1;

    /// <summary>Songs needed to complete `tier` (constant today — the single place escalating
    /// requirements would be implemented later).</summary>
    public int SongsRequiredForTier(int tier) => Mathf.Max(1, songsPerTier);
}
