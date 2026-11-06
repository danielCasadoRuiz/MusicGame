#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Adds the RIVAL's panel (its build bars, Special / x3 / x4 charges and its level pips — 2026-11-05) to the
/// existing FightHud prefab without rebuilding anything else — the same element HelpScreenPrefabs.PatchFightHud builds
/// on a full UI build. Idempotent (re-applies the layout). Batch: -executeMethod FightHudRivalPanelSetup.RunFromCommandLine
/// (Logs/FightHudRivalPanelSetup.txt)</summary>
public static class FightHudRivalPanelSetup
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/FightHud.prefab";

    [MenuItem("Tools/MusicGame/UI/Add Rival Panel To Fight HUD")]
    public static void Run() => Debug.Log(Apply());

    public static void RunFromCommandLine()
    {
        string r = Apply();
        System.IO.File.WriteAllText("Logs/FightHudRivalPanelSetup.txt", r);
        EditorApplication.Exit(r.StartsWith("FAIL") ? 1 : 0);
    }

    public static string Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (!HelpScreenPrefabs.PatchRivalPanel(go)) return "FAIL no FightHudView";
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            return "rival panel applied";
        }
        finally { PrefabUtility.UnloadPrefabContents(go); }
    }
}
#endif
