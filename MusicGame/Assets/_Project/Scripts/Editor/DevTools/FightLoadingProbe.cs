#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play-mode check of the fight's LOADING presentation: boots Core.unity, starts a run (which assigns the rival),
/// preloads the fight (FightPreloader), enters the Fight and, every editor tick of the Versus intro, verifies:
///   • no fighter avatar renders while it is still being assembled (built hidden, revealed posed);
///   • while the stage is not ready the Versus veil is OPAQUE; it clears once ready;
///   • the rival's line shows on the card (bubble) during loading.
/// Saves a few frames + report. Batch (with graphics): -executeMethod FightLoadingProbe.RunFromCommandLine
/// -fightLoadOut "C:/dir". Boots the real AppBootstrap: back up the save first.
/// </summary>
[InitializeOnLoad]
public static class FightLoadingProbe
{
    private const string KeyActive = "FightLoadingProbe.Active", KeyOut = "FightLoadingProbe.Out", KeyPhase = "FightLoadingProbe.Phase",
                         KeyNext = "FightLoadingProbe.Next", KeyLog = "FightLoadingProbe.Log", KeyFails = "FightLoadingProbe.Fails";
    private static int s_frames, s_loadingFrames, s_opaqueLoadingFrames, s_visibleWhileBuilding, s_bubbleOnCardFrames, s_shots;
    private static double s_versusStart = -1, s_readyAt = -1;
    private static bool s_wasReady, s_reported;

    static FightLoadingProbe()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        EditorApplication.update += Tick;
    }

    public static void RunFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-fightLoadOut");
        string outDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("Logs/FightLoadingProbe");
        Directory.CreateDirectory(outDir);
        foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, outDir);
        SessionState.SetInt(KeyPhase, 0);
        SessionState.SetString(KeyNext, "0");
        SessionState.SetString(KeyLog, "");
        SessionState.SetInt(KeyFails, 0);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static void Log(string line) => SessionState.SetString(KeyLog, SessionState.GetString(KeyLog, "") + line + "\n");
    private static void Expect(bool ok, string what)
    {
        Log($"  [{(ok ? "ok" : "FAIL")}] {what}");
        if (!ok) SessionState.SetInt(KeyFails, SessionState.GetInt(KeyFails, 0) + 1);
    }

    private static T Find<T>() where T : Component =>
        UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

    private static void Wait(double now, float seconds, int nextPhase)
    {
        SessionState.SetInt(KeyPhase, nextPhase);
        SessionState.SetString(KeyNext, (now + seconds).ToString(CultureInfo.InvariantCulture));
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(KeyActive, false)) { EditorApplication.update -= Tick; return; }
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        int phase = SessionState.GetInt(KeyPhase, 0);
        if (phase == 4) { SampleVersus(now); return; }
        if (now < double.Parse(SessionState.GetString(KeyNext, "0"), CultureInfo.InvariantCulture)) return;
        try
        {
            switch (phase)
            {
                case 0: Wait(now, 12f, 1); break; // boot
                case 1: AppFlowController.Instance.RequestState(GameFlowState.SongSelection); Wait(now, 3f, 2); break;
                case 2:
                {
                    var row = UnityEngine.Object.FindObjectsByType<SongRowView>(FindObjectsSortMode.None).OrderBy(r => r.transform.GetSiblingIndex())
                        .First(r => r.completedBadge == null || !r.completedBadge.gameObject.activeSelf);
                    row.button.onClick.Invoke();
                    Find<SongSelectionView>().playButton.onClick.Invoke();
                    Wait(now, 20f, 3);
                    break;
                }
                case 3:
                {
                    var session = GameSession.Instance;
                    Log($"run started; assigned rival {(session?.AssignedOpponent != null ? session.AssignedOpponent.displayName : "none")}");
                    double t0 = now;
                    if (Environment.GetCommandLineArgs().Contains("-noPreload")) Log("preload SKIPPED (-noPreload): slow path, everything loads in the fight");
                    else
                    {
                        FightPreloader.PrepareAssignedFight(); // what GameEndedEvent does at the end of the song
                        Expect(FightPreloader.HeldAssets > 0, $"preload started during the run results ({FightPreloader.HeldAssets} asset(s))");
                    }
                    // Let the preload finish (bounded), then enter the fight.
                    SessionState.SetString("FightLoadingProbe.T0", t0.ToString(CultureInfo.InvariantCulture));
                    Wait(now, 0.2f, 31);
                    break;
                }
                case 31:
                {
                    double t0 = double.Parse(SessionState.GetString("FightLoadingProbe.T0", "0"), CultureInfo.InvariantCulture);
                    if (FightPreloader.PendingLoads > 0 && now - t0 < 20) { Wait(now, 0.2f, 31); break; }
                    Log($"preload finished in {now - t0:0.00} s ({FightPreloader.HeldAssets} asset(s) resident, {FightPreloader.PendingLoads} pending)");
                    AppFlowController.Instance.RequestState(GameFlowState.Fight);
                    Wait(now, 0f, 4);
                    break;
                }
                case 5: Finish(); break;
            }
        }
        catch (Exception e) { Expect(false, $"phase {phase} threw: {e}"); Finish(); }
    }

    /// <summary>Every tick from entering Fight until the countdown: the loading-card and hidden-build checks.</summary>
    private static void SampleVersus(double now)
    {
        var flow = FightFlowController.Instance;
        var boot = FightSceneBootstrap.Instance;
        if (flow == null || boot == null) return;
        if (flow.CurrentState != FightFlowState.VersusIntro)
        {
            if (s_versusStart < 0) return; // roulette still running
            // Arena review shots continue into the fight (combat framing, then third person without UI).
            if (s_shots < 5 && now - s_readyAt < 25.0)
            {
                if (s_shots == 3 && now - s_readyAt > 5.0)
                {
                    Shot("04_arena_combat_framing");
                    var cam = boot.CameraController;
                    if (cam != null && cam.ViewMode != CameraViewMode.ThirdPerson) cam.ToggleView();
                }
                else if (s_shots == 4 && now - s_readyAt > 8.0) Shot("05_arena_third_person_no_ui", withUi: false);
                if (s_shots < 5) return;
            }
            if (s_reported) return;
            s_reported = true;
            Log($"Versus intro: {s_frames} sampled frames, {s_loadingFrames} while loading ({s_opaqueLoadingFrames} opaque), stage ready after {(s_readyAt > 0 ? s_readyAt - s_versusStart : -1):0.00} s");
            Expect(s_loadingFrames == 0 || s_opaqueLoadingFrames == s_loadingFrames, "Versus card fully opaque on every frame the stage was not ready");
            Expect(s_visibleWhileBuilding == 0, $"no avatar part rendered while still being assembled ({s_visibleWhileBuilding} frame(s))");
            // Informational: with a fast load the line may still be loading when the stage is ready — it then
            // appears directly over the rival's head (by design), so this is not a failure.
            Log($"  [info] the rival's line shown on the loading card on {s_bubbleOnCardFrames} of {s_loadingFrames} loading frame(s)");
            SessionState.SetInt(KeyPhase, 5);
            SessionState.SetString(KeyNext, (now + 1).ToString(CultureInfo.InvariantCulture));
            return;
        }
        if (s_versusStart < 0) { s_versusStart = now; Log("Versus intro entered"); }
        s_frames++;
        var view = Find<VersusScreenView>();
        bool ready = boot.StageReady;
        if (!ready)
        {
            s_loadingFrames++;
            if (view != null && view.root.activeInHierarchy && view.backdrop.color.a >= 0.999f && view.cardGroup.alpha >= 0.999f) s_opaqueLoadingFrames++;
            var bubble = Find<ComposerSpeechBubble>();
            if (bubble != null && bubble.ScreenAnchorProvider != null && !string.IsNullOrEmpty(bubble.CurrentText)) s_bubbleOnCardFrames++;
            if (s_shots == 0 && s_loadingFrames > 2) Shot("01_versus_loading");
        }
        else if (!s_wasReady) { s_readyAt = now; s_wasReady = true; Shot("02_stage_ready"); }
        else if (s_shots == 2 && now - s_readyAt > 1.0) Shot("03_versus_revealed");
        else if (s_shots == 3 && now - s_readyAt > 5.0)
        {
            Shot("04_arena_combat_framing");
            var cam = boot.CameraController;
            if (cam != null && cam.ViewMode != CameraViewMode.ThirdPerson) cam.ToggleView(); // arena review from the combat framing
        }
        else if (s_shots == 4 && now - s_readyAt > 8.0) Shot("05_arena_third_person_no_ui", withUi: false);

        // Hidden build: any renderer under a fighter that renders before that fighter is ready = a visible build.
        foreach (var (actor, readyFlag) in new[] { (boot.PlayerActor, boot.PlayerVisualReady), (boot.OpponentActor, boot.OpponentVisualReady) })
        {
            if (actor == null || readyFlag || actor.VisualRoot == null) continue;
            if (actor.VisualRoot.GetComponentsInChildren<Renderer>().Any(r => r.enabled && !r.forceRenderingOff)) s_visibleWhileBuilding++;
        }
    }

    private static void Shot(string name, bool withUi = true)
    {
        s_shots++;
        string path = Path.Combine(SessionState.GetString(KeyOut, "."), name + ".png");
        var boot = FightSceneBootstrap.Instance;
        var cam = boot != null && boot.CameraController != null ? boot.CameraController.Camera : null;
        if (cam == null) { Log($"  shot {name}: no fight camera"); return; }
        // Overlay canvases don't reach a RenderTexture: project them onto the fight camera for this one render.
        var canvases = withUi ? UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray() : new Canvas[0];
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + 0.05f; }
        Canvas.ForceUpdateCanvases();
        var rt = new RenderTexture(1280, 720, 24);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = prevTarget;
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        Log($"  shot {name}");
    }

    private static void Finish()
    {
        SessionState.SetBool(KeyActive, false);
        EditorApplication.update -= Tick;
        int fails = SessionState.GetInt(KeyFails, 0);
        string report = $"[FightLoadingProbe] failures: {fails}\n{SessionState.GetString(KeyLog, "")}RESULT {(fails == 0 ? "PASS" : "FAIL")}\n";
        File.WriteAllText(Path.Combine(SessionState.GetString(KeyOut, "."), "report.txt"), report);
        Debug.Log(report);
        EditorApplication.isPlaying = false;
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }
}
#endif
