#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Adds the PERFECT! banner to the existing (hand-edited) MatchResult prefab without rebuilding it —
/// the same element ResultsScreenPrefabs builds for a fresh prefab. Idempotent.
/// Batch: -executeMethod MatchResultPerfectSetup.RunFromCommandLine</summary>
public static class MatchResultPerfectSetup
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/MatchResult.prefab";

    [MenuItem("Tools/MusicGame/UI/Add PERFECT Banner To Match Result")]
    public static void Run() => Debug.Log(Apply());

    public static void RunFromCommandLine()
    {
        string r = Apply();
        System.IO.File.WriteAllText("Logs/MatchResultPerfectSetup.txt", r);
        EditorApplication.Exit(r.StartsWith("FAIL") ? 1 : 0);
    }

    public static string Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var view = go.GetComponent<MatchResultView>();
            if (view == null) return "FAIL no MatchResultView";
            if (view.perfectBanner != null) return "kept (already has a PERFECT banner)";
            view.perfectBanner = ResultsScreenPrefabs.BuildPerfectBanner((RectTransform)go.transform);
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            return "added PERFECT banner to " + PrefabPath;
        }
        finally { PrefabUtility.UnloadPrefabContents(go); }
    }
}
#endif
