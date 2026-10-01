using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wires the Signature Move content (idempotent): every FightMoveSetSO gets
///   signatureSpecial = the existing projectile move (Move_SpecialProjectile, combo "special_fireball":
///                      Down + Forward + Punch), now costing 1 Special;
///   signatureBasic   = the existing Heavy Punch move (no projectile).
/// Balance multipliers live in FightCombatBalanceConfig (Signature Move section).
/// Menu: Tools > MusicGame > Combat > Setup Signature Move. Batch: FightSignatureSetup.SetupFromCommandLine.
/// </summary>
public static class FightSignatureSetup
{
    private const string SpecialMovePath = "Assets/_Project/Configs/Fight/Move_SpecialProjectile.asset";
    private const string BasicMovePath   = "Assets/_Project/Configs/Fight/Combat/Moves/Move_HeavyPunch.asset";

    [MenuItem("Tools/MusicGame/Combat/Setup Signature Move")]
    public static void SetupMenu() => Debug.Log(Setup());

    public static void SetupFromCommandLine()
    {
        string report = Setup();
        File.WriteAllText("Logs/FightSignatureSetup.txt", report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Setup()
    {
        var sb = new StringBuilder("[FightSignatureSetup]\n");
        var special = AssetDatabase.LoadAssetAtPath<FightMoveDefinition>(SpecialMovePath);
        var basic   = AssetDatabase.LoadAssetAtPath<FightMoveDefinition>(BasicMovePath);
        if (special == null || basic == null) return sb.AppendLine($"  ERROR  missing move(s): special {special != null}, basic {basic != null}").ToString();

        special.resourceCost = CombatResourceType.Special;
        special.resourceAmount = 1;
        EditorUtility.SetDirty(special);
        sb.AppendLine($"  {special.id}: costs 1 Special, delivery {special.attackDelivery}, projectile hit {special.projectile?.hitDefinition?.baseDamage} dmg");

        foreach (var guid in AssetDatabase.FindAssets("t:FightMoveSetSO"))
        {
            var set = AssetDatabase.LoadAssetAtPath<FightMoveSetSO>(AssetDatabase.GUIDToAssetPath(guid));
            set.signatureSpecial = special;
            set.signatureBasic = basic;
            if (!set.moves.Contains(special)) sb.AppendLine($"  WARN  {set.name}: signature special move not in moves[] (the combo resolves it via the slot anyway)");
            EditorUtility.SetDirty(set);
            sb.AppendLine($"  PASS  {set.name}: signatureSpecial = {special.id}, signatureBasic = {basic.id}");
        }

        var combos = Resources.Load<AppConfigSO>("AppConfig")?.fightFlow?.comboSet;
        var sig = combos != null ? combos.combos.FirstOrDefault(c => c != null && c.moveId == special.id) : null;
        sb.AppendLine(sig != null
            ? $"  PASS  Signature input = combo '{sig.id}': {string.Join(" ", sig.steps.Select(s => s.ToString()))}"
            : "  ERROR  no combo triggers the signature move");
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }
}
