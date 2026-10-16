using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// The final rival — "Ésser de la música" (Boss.Name) — and its three consecutive fights.
/// Reuses the normal rival pipeline: `opponent` is an ordinary OpponentDefinition whose levels[] hold
/// the three versions (stats, AI difficulty, combat profile, avatar recipe, portrait = card), and that
/// definition is deliberately NOT in the normal OpponentRoster (it never appears in the campaign).
///
/// Per stage: stable id, version, special song, arena environment. All three stages may share one
/// arena prefab or use three different ones; null = the default arena / placeholder floor.
/// </summary>
[CreateAssetMenu(fileName = "FinalBossConfig", menuName = "MusicGame/Campaign/Final Boss Config")]
public class FinalBossConfigSO : ScriptableObject
{
    [Tooltip("The final rival (NOT listed in the normal OpponentRoster). Its levels[] must contain each stage's opponentLevel.")]
    public OpponentDefinition opponent;
    [Tooltip("Localization key of the rival's name.")]
    public string nameKey = "Boss.Name";
    [Tooltip("Silhouette shown in the reveal popup and the hidden collection slots (null = the stage card tinted black).")]
    public Sprite silhouette;

    public FinalBossStage[] stages =
    {
        new() { stageId = "boss_1", opponentLevel = 1 },
        new() { stageId = "boss_2", opponentLevel = 2 },
        new() { stageId = "boss_3", opponentLevel = 3 },
    };

    [Tooltip("Extension point for the boss's special powers (FighterCombatProfileSO / move sets per stage are already " +
             "configured on each OpponentLevelConfig). Reserved for future ability assets.")]
    public ScriptableObject[] specialAbilities = Array.Empty<ScriptableObject>();

    public int StageCount => stages != null ? stages.Length : 0;
    public FinalBossStage Stage(int i) => stages != null && i >= 0 && i < stages.Length ? stages[i] : null;

    public OpponentLevelConfig LevelConfig(int stageIndex)
    {
        var s = Stage(stageIndex);
        if (s == null || opponent == null || opponent.levels == null) return null;
        foreach (var l in opponent.levels) if (l != null && l.level == s.opponentLevel) return l;
        return null;
    }

    /// <summary>Human-readable problems (null/empty = ready). Missing songs/arenas are warnings: the
    /// fight falls back to the version's own songs / the default arena.</summary>
    public string Validate()
    {
        var sb = new System.Text.StringBuilder();
        if (opponent == null) sb.AppendLine("no opponent assigned");
        if (StageCount != 3) sb.AppendLine($"{StageCount} stages (expected 3)");
        for (int i = 0; i < StageCount; i++)
        {
            var s = stages[i];
            if (s == null) { sb.AppendLine($"stage {i + 1}: missing"); continue; }
            if (string.IsNullOrEmpty(s.stageId)) sb.AppendLine($"stage {i + 1}: empty stageId");
            if (LevelConfig(i) == null) sb.AppendLine($"stage {i + 1}: opponent has no version {s.opponentLevel}");
            if (s.song == null) sb.AppendLine($"stage {i + 1}: special song not assigned yet (uses the version's songs)");
            if (s.arena == null || !s.arena.RuntimeKeyIsValid()) sb.AppendLine($"stage {i + 1}: arena not assigned (default arena)");
        }
        return sb.ToString();
    }
}

[Serializable]
public class FinalBossStage
{
    [Tooltip("STABLE id (records are keyed by it).")]
    public string stageId = "boss_1";
    [Tooltip("Version (OpponentLevelConfig.level) of FinalBossConfigSO.opponent fought in this stage — its card.")]
    [Min(1)] public int opponentLevel = 1;
    [Tooltip("Special fight song. Empty = the version's own songs[].")]
    public AudioClip song;
    [Tooltip("Arena of this stage (Addressable prefab — same loader as composer arenas). Several stages may share one. " +
             "Empty = FightArenaConfig.defaultArena / placeholder floor.")]
    public AssetReferenceGameObject arena;
}
