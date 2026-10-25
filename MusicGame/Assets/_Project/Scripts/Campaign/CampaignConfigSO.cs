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

    [Header("Runner difficulty (continuous 0..1, see RunnerDifficultyModel)")]
    [Tooltip("Easiest and hardest runner difficulty of the whole campaign. A level without its own " +
             "difficultyFirstMatch/LastMatch gets an equal slice of this range, in level order, and every " +
             "match of the level steps up inside its slice.")]
    [Range(0f, 1f)] public float campaignDifficultyStart = 0.15f;
    [Range(0f, 1f)] public float campaignDifficultyEnd = 0.85f;
    [Tooltip("Added per lap after the first (clamped to 1). 0 = later laps play at the same difficulty, so " +
             "they reuse the same stored pickup layouts (the lap is then not part of the layout key).")]
    [Range(0f, 0.5f)] public float lapDifficultyStep = 0f;

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

    /// <summary>Runner difficulty (0..1) of match `matchNumber` (1-based) of `levelId` in `lap`: the level's
    /// first→last match range (its own, or its slice of the campaign range), plus the lap step. −1 when the
    /// level is unknown.</summary>
    public float RunnerDifficultyAt(string levelId, int matchNumber, int lap)
    {
        int index = IndexOf(levelId);
        var level = Level(index);
        if (level == null) return -1f;
        // Default: each level owns an equal slice; its matches step through it and the next level's first
        // match is one more step up (never the same difficulty as the previous level's last match).
        int count = Mathf.Max(1, level.matchCount);
        float slice = (campaignDifficultyEnd - campaignDifficultyStart) / Mathf.Max(1, LevelCount);
        float first = level.difficultyFirstMatch >= 0f ? level.difficultyFirstMatch : campaignDifficultyStart + slice * index;
        float last  = level.difficultyLastMatch  >= 0f ? level.difficultyLastMatch  : campaignDifficultyStart + slice * index + slice * (count - 1f) / count;
        float t = count > 1 ? Mathf.Clamp01((matchNumber - 1f) / (count - 1f)) : 0f;
        float d = Mathf.Lerp(first, last, t) + lapDifficultyStep * Mathf.Max(0, lap - 1);
        return Mathf.Clamp01(d);
    }

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
    [Tooltip("Legacy label (Normal/Hard) kept for older tools; the runner now uses the continuous difficulty below.")]
    public RunnerDifficulty runnerDifficulty = RunnerDifficulty.Normal;
    [Tooltip("Runner difficulty (0..1) of this level's FIRST match; −1 = its slice of the campaign range.")]
    [Range(-1f, 1f)] public float difficultyFirstMatch = -1f;
    [Tooltip("Runner difficulty (0..1) of this level's LAST match (matches in between step up evenly); −1 = its slice of the campaign range.")]
    [Range(-1f, 1f)] public float difficultyLastMatch = -1f;
}
