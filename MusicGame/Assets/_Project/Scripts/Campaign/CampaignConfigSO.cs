using System;
using UnityEngine;

/// <summary>
/// The CAMPAIGN: an ordered, finite list of levels, each with its own number of matches (duration)
/// and its own difficulty (rival version, runner difficulty) — the two are tuned independently.
/// AppConfigSO.campaign. Changing counts/order never needs code changes; level ids must stay stable
/// because per-position records are keyed by them ("L2#3").
/// </summary>
[CreateAssetMenu(fileName = "CampaignConfig", menuName = "MusicGame/Campaign/Campaign Config")]
public class CampaignConfigSO : ScriptableObject
{
    public CampaignLevelDefinition[] levels =
    {
        new() { id = "L1", matchCount = 5, rivalLevel = 1, runnerDifficulty = RunnerDifficulty.Normal },
        new() { id = "L2", matchCount = 5, rivalLevel = 2, runnerDifficulty = RunnerDifficulty.Normal },
        new() { id = "L3", matchCount = 6, rivalLevel = 3, runnerDifficulty = RunnerDifficulty.Hard },
        new() { id = "L4", matchCount = 6, rivalLevel = 4, runnerDifficulty = RunnerDifficulty.Hard },
    };

    [Header("Rival selection")]
    [Tooltip("Within a level, composers whose card for that level's rival version is still missing are picked " +
             "first (guarantees the normal collection can be completed over laps). Off = plain shuffle bag.")]
    public bool prioritizeMissingCards = true;
    [Tooltip("A composer is not repeated inside the same level of the same lap while others are available.")]
    public bool noRepeatWithinLevel = true;

    [Header("Final rival")]
    public FinalBossConfigSO finalBoss;

    public int LevelCount => levels != null ? levels.Length : 0;

    public int TotalMatches
    {
        get { int n = 0; if (levels != null) foreach (var l in levels) if (l != null) n += Mathf.Max(1, l.matchCount); return n; }
    }

    public CampaignLevelDefinition Level(int index) =>
        levels != null && index >= 0 && index < levels.Length ? levels[index] : null;

    public int IndexOf(string levelId)
    {
        if (levels == null || string.IsNullOrEmpty(levelId)) return -1;
        for (int i = 0; i < levels.Length; i++) if (levels[i] != null && levels[i].id == levelId) return i;
        return -1;
    }

    public static string PositionId(string levelId, int matchNumber) => $"{levelId}#{matchNumber}";

    private void OnValidate()
    {
        if (levels == null) return;
        foreach (var l in levels) if (l != null) { l.matchCount = Mathf.Max(1, l.matchCount); l.rivalLevel = Mathf.Max(1, l.rivalLevel); }
    }
}

[Serializable]
public class CampaignLevelDefinition
{
    [Tooltip("STABLE id (records are keyed by it). Never rename a released level.")]
    public string id = "L1";
    [Tooltip("Optional localization key for the level name. Empty = \"Campaign.Level\" with its number.")]
    public string nameKey = "";
    [Tooltip("Matches required to complete the level (its duration).")]
    [Min(1)] public int matchCount = 5;
    [Tooltip("Rival VERSION fought in this level (OpponentLevelConfig.level): stats, AI, combat profile, card.")]
    [Min(1)] public int rivalLevel = 1;
    [Tooltip("Runner difficulty for every match of this level (pickup heights / off-track / patterns).")]
    public RunnerDifficulty runnerDifficulty = RunnerDifficulty.Normal;
}
