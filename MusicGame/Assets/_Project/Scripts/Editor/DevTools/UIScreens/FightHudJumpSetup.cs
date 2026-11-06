#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Adds the JUMP touch button (the fight's third button, above Punch / Kick) to the existing FightHud
/// prefab without rebuilding it — the same element FightScreenPrefabs builds for a fresh prefab. Idempotent.
/// Batch: -executeMethod FightHudJumpSetup.RunFromCommandLine  (Logs/FightHudJumpSetup.txt)</summary>
public static class FightHudJumpSetup
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/FightHud.prefab";

    [MenuItem("Tools/MusicGame/UI/Add Jump Button To Fight HUD")]
    public static void Run() => Debug.Log(Apply());

    public static void RunFromCommandLine()
    {
        string r = Apply();
        System.IO.File.WriteAllText("Logs/FightHudJumpSetup.txt", r);
        EditorApplication.Exit(r.StartsWith("FAIL") ? 1 : 0);
    }

    public static string Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var view = go.GetComponentInChildren<FightHudView>(true);
            if (view == null) return "FAIL no FightHudView";
            if (view.jumpAction != null) return "kept (already has the jump button)";
            if (view.mobileControlsRoot == null) return "FAIL no MobileControls root";
            FightScreenPrefabs.BuildJumpButton((RectTransform)view.mobileControlsRoot.transform, view);
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            return "added";
        }
        finally { PrefabUtility.UnloadPrefabContents(go); }
    }
}
#endif
