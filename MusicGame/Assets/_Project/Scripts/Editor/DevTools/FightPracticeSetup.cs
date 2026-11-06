using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The FIGHT PRACTICE scene (dev tool, 2026-11-05): Scenes/Debug/FightPractice.unity = one GameObject with
/// FightPracticeMode (it loads Fight.unity itself on Play). Open it and press Play — or the menu below.
/// Batch: -executeMethod FightPracticeSetup.CreateFromCommandLine -quit (create / refresh the scene)
///        -executeMethod FightPracticeSetup.SmokeFromCommandLine (play-mode check: ready, a demo move fires; exits itself)
/// </summary>
[InitializeOnLoad]
public static class FightPracticeSetup
{
    public const string ScenePath = "Assets/_Project/Scenes/Debug/FightPractice.unity";
    private const string SmokeKey = "FightPracticeSetup.Smoke";

    static FightPracticeSetup()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SmokeKey, false)) return;
            SessionState.SetBool(SmokeKey, false);
            _ = Smoke();
        };
    }

    [MenuItem("Tools/MusicGame/Combat/Fight Practice (open + play)")]
    public static void OpenAndPlay()
    {
        if (!File.Exists(ScenePath)) Create();
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/MusicGame/Combat/Create Fight Practice Scene")]
    public static void CreateMenu() => Debug.Log(Create());

    public static void CreateFromCommandLine()
    {
        string report = Create();
        File.WriteAllText("Logs/FightPracticeSetup.txt", report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("FightPractice");
        go.AddComponent<FightPracticeMode>();
        bool ok = EditorSceneManager.SaveScene(scene, ScenePath);
        return ok ? $"[FightPracticeSetup] scene saved: {ScenePath}" : $"[FightPracticeSetup] ERROR saving {ScenePath}";
    }

    // ── Play-mode smoke check ───────────────────────────────────────────────────────────────────────
    public static void SmokeFromCommandLine()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(SmokeKey, true);
        EditorApplication.isPlaying = true;
    }

    private static async Task Smoke()
    {
        var log = new StringBuilder("[FightPracticeSetup] smoke\n");
        int fails = 0;
        void Check(string n, bool ok, string d = "") { log.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {n}  {d}"); if (!ok) fails++; }
        try
        {
            FightPracticeMode mode = null;
            float end = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < end && (mode == null || !mode.IsReady)) { mode = Object.FindFirstObjectByType<FightPracticeMode>(); await Task.Yield(); }
            Check("practice ready (fight loaded, both fighters)", mode != null && mode.IsReady);
            if (mode != null && mode.IsReady)
            {
                var s = mode.Player.MoveController.MoveSet;
                foreach (var m in new[] { s.normalPunch, s.punchMidSide, s.kickMidCenter, s.spinSweep })
                {
                    float t = Time.realtimeSinceStartup + 1.6f;
                    mode.DemoForTest(m);
                    while (Time.realtimeSinceStartup < t) await Task.Yield();
                    Check($"demo '{m?.debugName}' runs and is reported", mode.LastMoveInfo.Contains(m != null ? m.debugName : "?"), $"{mode.LastMoveInfo} → {mode.LastResultInfo}");
                }
                var w = mode.Player.CombatResources;
                Check("infinite resources (x3, x4, Specials ≥ 3)", w != null && w.Get(CombatResourceType.TripleCombo) >= 3 && w.Get(CombatResourceType.QuadCombo) >= 3 && w.Get(CombatResourceType.Special) >= 3);
            }
        }
        catch (System.Exception e) { Check("exception", false, e.ToString()); }
        string report = log.Insert(0, fails == 0 ? "ALL PASSED\n" : $"{fails} FAILED\n").ToString();
        File.WriteAllText("Logs/FightPracticeSmoke.txt", report);
        Debug.Log(report);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(fails == 0 ? 0 : 1);
    }
}
