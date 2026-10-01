using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Combo-string content (idempotent): registers the Punch/Kick finishers as ordinary combo
/// definitions (P P P / K K K — empty moveId = the last step's normal, attackBonus = finisher) and
/// audits the whole FightComboSetSO for definitions that are genuinely indistinguishable (identical
/// steps). Prefix overlap is allowed by design and only reported as information.
/// Menu: Tools > MusicGame > Combat > Setup Combo Strings. Batch: FightComboStringSetup.SetupFromCommandLine.
/// </summary>
public static class FightComboStringSetup
{
    [MenuItem("Tools/MusicGame/Combat/Setup Combo Strings")]
    public static void SetupMenu() => Debug.Log(Setup());

    public static void SetupFromCommandLine()
    {
        string report = Setup();
        File.WriteAllText("Logs/FightComboStringSetup.txt", report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Setup()
    {
        var sb = new StringBuilder("[FightComboStringSetup]\n");
        var set = Resources.Load<AppConfigSO>("AppConfig")?.fightFlow?.comboSet;
        if (set == null) return sb.AppendLine("  ERROR  no combo set").ToString();

        var list = set.combos.ToList();
        AddIfMissing(list, "punch_finisher", "Punch Finisher", FightButton.Punch, FightComboBonus.PunchFinisher, sb);
        AddIfMissing(list, "kick_finisher", "Kick Finisher", FightButton.Kick, FightComboBonus.KickFinisher, sb);
        set.combos = list.ToArray();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        sb.AppendLine("  registered combos:");
        foreach (var c in set.combos)
            sb.AppendLine($"    {c.id,-16} {c.StepsText,-34} gap ≤{c.maxTimeBetweenInputs:0.00}s{(c.maxTotalDuration > 0f ? $" total ≤{c.maxTotalDuration:0.00}s" : "")}  → {(string.IsNullOrEmpty(c.moveId) ? "(last step's normal)" : c.moveId)}{(c.attackBonus != FightComboBonus.None ? $" + {c.attackBonus}" : "")}");

        bool Same(FightComboDefinition a, FightComboDefinition b) =>
            a.steps.Length == b.steps.Length && a.steps.Select(s => s.ToString()).SequenceEqual(b.steps.Select(s => s.ToString()));
        bool Prefix(FightComboDefinition a, FightComboDefinition b) =>
            a.steps.Length < b.steps.Length && a.steps.Select(s => s.ToString()).SequenceEqual(b.steps.Take(a.steps.Length).Select(s => s.ToString()));

        int conflicts = 0;
        for (int i = 0; i < set.combos.Length; i++)
            for (int j = i + 1; j < set.combos.Length; j++)
            {
                var a = set.combos[i]; var b = set.combos[j];
                if (Same(a, b))
                {
                    conflicts++;
                    sb.AppendLine($"  CONFLICT  '{a.id}' and '{b.id}' have identical steps ({a.StepsText}) — only priority can separate them.");
                }
                else if (Prefix(a, b)) sb.AppendLine($"  prefix    '{a.id}' ⊂ '{b.id}' (allowed — '{b.id}' extends and replaces it)");
                else if (Prefix(b, a)) sb.AppendLine($"  prefix    '{b.id}' ⊂ '{a.id}' (allowed — '{a.id}' extends and replaces it)");
            }
        sb.AppendLine($"  {conflicts} genuinely indistinguishable pair(s)");
        return sb.ToString();
    }

    private static void AddIfMissing(System.Collections.Generic.List<FightComboDefinition> list, string id, string name,
                                     FightButton button, FightComboBonus bonus, StringBuilder sb)
    {
        if (list.Any(c => c != null && c.id == id)) return;
        var step = new FightComboDefinition.Step { button = button, horizontal = FightHorizontalDirection.Neutral, vertical = FightVerticalDirection.Neutral };
        list.Add(new FightComboDefinition
        {
            id = id, debugName = name, steps = new[] { step, step, step },
            maxTimeBetweenInputs = 0.6f, // the previous repeat-chain window
            moveId = "", attackBonus = bonus,
        });
        sb.AppendLine($"  added '{id}' ({button} ×3, {bonus})");
    }
}
