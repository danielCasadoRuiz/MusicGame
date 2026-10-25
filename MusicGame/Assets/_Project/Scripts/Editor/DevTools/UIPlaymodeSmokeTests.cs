#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play Mode smoke test of the prefab-based UI: boots Core.unity like the game, walks Intro → Main Menu →
/// Settings → Song Selection → Fight, then shows every remaining screen prefab (overlays, results, runner HUD,
/// pause), saving a PNG of the UI for each step and collecting every error/exception logged meanwhile.
/// Batch (with graphics, NOT -nographics):
///   -executeMethod UIPlaymodeSmokeTests.RunFromCommandLine -uiSmokeOut "C:/path/dir"
/// Writes &lt;dir&gt;/report.txt and &lt;dir&gt;/NN_step.png. It boots the real AppBootstrap, so it reads/writes the
/// real save file: back it up before running.
/// </summary>
[InitializeOnLoad]
public static class UIPlaymodeSmokeTests
{
    private const string KeyActive = "UIPlaymodeSmoke.Active";
    private const string KeyOut = "UIPlaymodeSmoke.Out";
    private const string KeyStep = "UIPlaymodeSmoke.Step";
    private const string KeyNext = "UIPlaymodeSmoke.Next";

    private static readonly StringBuilder s_log = new();
    private static int s_errors;
    private static bool s_hooked;

    // (seconds to wait BEFORE running the step, name, action)
    private static readonly (float wait, string name, Action act)[] Steps =
    {
        (12f, "01_intro", null),
        (0f, "02_mainmenu", () => AppFlowController.Instance.RequestState(GameFlowState.MainMenu)),
        (3f, null, null),
        (0f, "03_settings", () => Find<MainMenuView>().settingsButton.onClick.Invoke()),
        (1f, null, null),
        (0f, null, () => Find<MainMenuView>().closeButton.onClick.Invoke()),
        (0f, "04_collection", () => RivalCollectionController.Instance.Open()),
        (1f, null, null),
        (0f, null, () => RivalCollectionController.Instance.Close()),
        (0f, "05_songselection", () => AppFlowController.Instance.RequestState(GameFlowState.SongSelection)),
        (4f, null, null),
        (0f, null, () => AppFlowController.Instance.RequestState(GameFlowState.Fight)),
        (4f, "06_fight_a", null),
        (5f, "07_fight_b", null),
        (6f, "08_fight_c", null),
        (8f, "09_fight_d", null),
        (8f, "10_fight_e", null),
        (0f, null, () => AppFlowController.Instance.RequestState(GameFlowState.MainMenu)),
        (2f, null, () => AppFlowController.Instance.RequestState(GameFlowState.SongSelection)),
        (3f, null, () => FirstPlayableRow().button.onClick.Invoke()),
        (1f, null, () => Find<SongSelectionView>().playButton.onClick.Invoke()),
        (30f, "11_runner", null),
        (0f, "12_runner_pause", () => Find<PauseController>().Pause()),
        (1f, null, () => Find<PauseController>().Resume()),
        (12f, "13_runner_later", null),
        (0f, null, () => AppFlowController.Instance.RequestState(GameFlowState.MainMenu)),
        (4f, null, null),
    };

    private static SongRowView FirstPlayableRow() =>
        UnityEngine.Object.FindObjectsByType<SongRowView>(FindObjectsSortMode.None)
            .OrderBy(r => r.transform.GetSiblingIndex())
            .First(r => r.button.interactable && !r.Played);

    private static readonly string[] Overlays =
    {
        nameof(AnalyzingScreenView), nameof(CountdownScreenView), nameof(FinishBannerView), nameof(NextSongTransitionView),
        nameof(RoundIntroView), nameof(RoundEndView), nameof(MatchResultView), nameof(MobileControlsView),
        nameof(OpponentSelectionView), nameof(VersusScreenView), nameof(FightHudView), nameof(SongSelectionView), nameof(MainMenuView),
    };

    static UIPlaymodeSmokeTests()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        Hook();
        EditorApplication.update += Tick;
    }

    public static void RunFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-uiSmokeOut");
        string outDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("Logs/UIPlaymodeSmoke");
        Directory.CreateDirectory(outDir);
        foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, outDir);
        SessionState.SetInt(KeyStep, -1);
        File.WriteAllText(Path.Combine(outDir, "log.txt"), "");
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        if (s_hooked) return;
        s_hooked = true;
        Application.logMessageReceived += (msg, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                s_errors++;
                s_log.AppendLine($"  {type}: {msg}\n{Indent(stack)}");
            }
            File.AppendAllText(Path.Combine(SessionState.GetString(KeyOut, "."), "log.txt"), $"[{type}] {msg}\n");
        };
    }

    private static string Indent(string s) => string.Join("\n", (s ?? "").Split('\n').Take(6).Select(l => "      " + l));

    private static void Tick()
    {
        if (!SessionState.GetBool(KeyActive, false)) { EditorApplication.update -= Tick; return; }
        if (!EditorApplication.isPlaying) return;
        Hook();
        double now = EditorApplication.timeSinceStartup;
        int step = SessionState.GetInt(KeyStep, -1);
        if (step == -1) { Schedule(0, now); return; }
        if (now < double.Parse(SessionState.GetString(KeyNext, "0"))) return;

        try
        {
            if (step < Steps.Length)
            {
                var s = Steps[step];
                s.act?.Invoke();
                if (s.name != null) Capture(s.name);
            }
            else if (step == Steps.Length) ShowEachScreen();
            else { Finish(); return; }
        }
        catch (Exception e) { s_errors++; s_log.AppendLine($"  STEP {step} threw: {e}"); }
        Schedule(step + 1, now);
    }

    private static void Schedule(int step, double now)
    {
        float wait = step < Steps.Length ? Steps[step].wait : 1f;
        SessionState.SetInt(KeyStep, step);
        SessionState.SetString(KeyNext, (now + wait).ToString());
    }

    private static T Find<T>() where T : Component =>
        UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

    /// <summary>Each UI-scene screen alone (data left as the flow set it), then the three Runner prefabs.</summary>
    private static void ShowEachScreen()
    {
        var canvas = UIFactory.RootCanvas();
        var all = canvas.GetComponentsInChildren<MonoBehaviour>(true);
        var screens = canvas.Cast<Transform>().Select(t => t.gameObject).ToArray();
        var wasActive = screens.ToDictionary(g => g, g => g.activeSelf);
        int n = 20;
        foreach (string viewName in Overlays)
        {
            var view = all.FirstOrDefault(m => m != null && m.GetType().Name == viewName);
            if (view == null) { s_errors++; s_log.AppendLine($"  MISSING screen instance with {viewName} under the root canvas"); continue; }
            foreach (var g in screens) g.SetActive(false);
            var screenRoot = view.transform;
            while (screenRoot.parent != canvas) screenRoot = screenRoot.parent;
            screenRoot.gameObject.SetActive(true);
            foreach (Transform child in view.transform) if (child.name.EndsWith("Modal")) child.gameObject.SetActive(false);
            if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
            foreach (var g in screenRoot.GetComponentsInChildren<CanvasGroup>(true)) g.alpha = 1f; // faded-out cards
            Capture($"{n++}_{viewName}");
        }
        foreach (var g in screens) if (g != null) g.SetActive(false);
        foreach (string prefab in new[] { "LiveHud", "EndScreen", "PauseMenu" })
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/UI/{prefab}.prefab");
            var inst = UnityEngine.Object.Instantiate(asset, canvas, false);
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
            Capture($"{n++}_{prefab}");
            UnityEngine.Object.DestroyImmediate(inst);
        }
        foreach (var kv in wasActive) if (kv.Key != null) kv.Key.SetActive(kv.Value);
    }

    private static void Capture(string name)
    {
        var canvasRt = UIFactory.RootCanvas();
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        var camGo = new GameObject("[UISmokeCamera]");
        camGo.transform.position = new Vector3(0f, -5000f, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.2f, 0.24f);
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 10f; }
        Canvas.ForceUpdateCanvases();
        cam.Render();
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        File.WriteAllBytes(Path.Combine(SessionState.GetString(KeyOut, "."), name + ".png"), tex.EncodeToPNG());
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
        UnityEngine.Object.DestroyImmediate(tex);
        cam.targetTexture = null;
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(camGo);
        var visible = canvasRt.Cast<Transform>().Where(t => t.gameObject.activeSelf).Select(t => t.name);
        s_log.AppendLine($"  shot {name}  (active: {string.Join(", ", visible)})");
    }

    private static void Finish()
    {
        SessionState.SetBool(KeyActive, false);
        EditorApplication.update -= Tick;
        string report = $"[UIPlaymodeSmokeTests] errors/exceptions: {s_errors}\n{s_log}\nRESULT {(s_errors == 0 ? "PASS" : "FAIL")}\n";
        File.WriteAllText(Path.Combine(SessionState.GetString(KeyOut, "."), "report.txt"), report, Encoding.UTF8);
        Debug.Log(report);
        EditorApplication.isPlaying = false;
        EditorApplication.Exit(s_errors == 0 ? 0 : 1);
    }
}
#endif
