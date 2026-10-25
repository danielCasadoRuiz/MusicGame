#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Adds the per-round PERFECT! texts (under each health bar) to the existing FightHud prefab without
/// rebuilding it — the same elements FightScreenPrefabs builds for a fresh prefab. Idempotent.
/// Batch: -executeMethod FightHudPerfectSetup.RunFromCommandLine</summary>
public static class FightHudPerfectSetup
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/FightHud.prefab";

    [MenuItem("Tools/MusicGame/UI/Add PERFECT Texts To Fight HUD")]
    public static void Run() => Debug.Log(Apply());

    public static void RunFromCommandLine()
    {
        string r = Apply();
        System.IO.File.WriteAllText("Logs/FightHudPerfectSetup.txt", r);
        EditorApplication.Exit(r.StartsWith("FAIL") ? 1 : 0);
    }

    public static string Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var view = go.GetComponentInChildren<FightHudView>(true);
            if (view == null) return "FAIL no FightHudView";
            if (view.playerPerfectText != null && view.opponentPerfectText != null) return "kept (already has the PERFECT texts)";
            if (view.playerRoundPipsText == null) return "FAIL no round pips to anchor to";
            FightScreenPrefabs.BuildPerfectTexts((RectTransform)view.playerRoundPipsText.transform.parent, view);
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            return "added";
        }
        finally { PrefabUtility.UnloadPrefabContents(go); }
    }
}
#endif
