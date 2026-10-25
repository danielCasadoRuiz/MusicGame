#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One combat IDENTITY per rival (2026-10-25): a FighterStatsProfileSO with the PROPORTIONS of its four attributes
/// (Configs/Fight/Opponents/Profiles/StatsProfile_&lt;id&gt;.asset), assigned to every version of that rival. The size of
/// the build comes from the match difficulty (campaign level + match inside the level) × each version's
/// OpponentLevelConfig.difficultyScale — so every rival keeps its character while getting stronger.
/// Idempotent and conservative: a profile asset that already exists is never overwritten (hand edits stay), and a
/// version is only re-pointed when it still uses the shared default profile (or none).
/// </summary>
public static class RivalProfileSetup
{
    private const string Folder = "Assets/_Project/Configs/Fight/Opponents/Profiles";
    private const string DefaultProfilePath = "Assets/_Project/Configs/Fight/FighterStatsProfile_Default.asset";

    // Punch, Kick, Resistance, Agility — proportions only (any total; RivalBuild rescales them).
    private static readonly (string id, float punch, float kick, float resistance, float agility, string note)[] Identities =
    {
        ("bach",        20, 18, 26, 16, "solid, hard to hurt"),
        ("beethoven",   28, 18, 22, 12, "heavy punches"),
        ("brahms",      18, 20, 28, 14, "the wall"),
        ("chopin",      18, 14, 14, 34, "very agile, fragile"),
        ("handel",      26, 16, 24, 14, "punches and holds"),
        ("haydn",       20, 20, 20, 20, "all-rounder"),
        ("monteverdi",  14, 28, 16, 22, "kicks on the move"),
        ("mozart",      20, 16, 14, 30, "quick"),
        ("pachelbel",   18, 18, 22, 22, "steady"),
        ("tchaikovsky", 16, 30, 18, 16, "big kicks"),
        ("vivaldi",     16, 22, 12, 30, "fast kicker"),
        ("wagner",      26, 22, 26,  8, "brute force, slow"),
        ("music_being", 25, 25, 25, 25, "the final rival: no weak spot"),
    };

    public static void Apply(StringBuilder log)
    {
        EnsureFolder(Folder);
        var shared = AssetDatabase.LoadAssetAtPath<FighterStatsProfileSO>(DefaultProfilePath);
        foreach (var guid in AssetDatabase.FindAssets("t:OpponentDefinition"))
        {
            var opponent = AssetDatabase.LoadAssetAtPath<OpponentDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (opponent == null || string.IsNullOrEmpty(opponent.id)) continue;
            int i = System.Array.FindIndex(Identities, x => x.id == opponent.id);
            if (i < 0) { log.AppendLine($"  skip  rival '{opponent.id}': no identity defined (keeps its profile)"); continue; }
            var profile = EnsureProfile(Identities[i], log);
            bool changed = false;
            if (opponent.levels != null)
                foreach (var level in opponent.levels)
                    if (level != null && (level.combatStats == null || level.combatStats == shared)) { level.combatStats = profile; changed = true; }
            if (opponent.defaultConfig != null && (opponent.defaultConfig.combatStats == null || opponent.defaultConfig.combatStats == shared))
            { opponent.defaultConfig.combatStats = profile; changed = true; }
            if (changed) { EditorUtility.SetDirty(opponent); log.AppendLine($"  set   {opponent.id}: every version uses {profile.name} ({Identities[i].note})"); }
        }
        AssetDatabase.SaveAssets();
    }

    private static FighterStatsProfileSO EnsureProfile((string id, float punch, float kick, float resistance, float agility, string note) x, StringBuilder log)
    {
        string path = $"{Folder}/StatsProfile_{x.id}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<FighterStatsProfileSO>(path);
        if (existing != null) return existing;
        var p = ScriptableObject.CreateInstance<FighterStatsProfileSO>();
        p.buildStats = new FighterBuildStats { punchPower = x.punch, kickPower = x.kick, resistance = x.resistance, agility = x.agility };
        AssetDatabase.CreateAsset(p, path);
        log.AppendLine($"  new   {path} — {x.note} (P {x.punch} / K {x.kick} / R {x.resistance} / A {x.agility})");
        return p;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
#endif
