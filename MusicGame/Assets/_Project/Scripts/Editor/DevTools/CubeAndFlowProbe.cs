#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Focused Play Mode probe of two fixes (2026-10-23):
///   CUBES — a musical cube is small (ghost) before its musical moment whatever the player's proximity, full
///   size (1.0, never above: no overshoot) from its moment on, shrinks away when passed without collecting,
///   disappears for good when collected (also before its moment), and carries no visual state from the pool
///   across pause, fall-respawn and restart. Checked every editor tick on every active musical cube.
///   FLOW — Continue from the Main Menu while the previous selection is still recorded (the stuck-preparation
///   bug), Continue cancelled during the preparation then Continue again, and a manual selection: each must
///   reach the countdown (GameStartedEvent), never stay on the preparation screen.
/// Boots Core.unity (the real AppBootstrap: writes the save). Content in the editor's Addressables mode.
/// Batch: -executeMethod CubeAndFlowProbe.RunFromCommandLine [-cubeProbeOut FILE]   Exit 0 = pass.
/// </summary>
[InitializeOnLoad]
public static class CubeAndFlowProbe
{
    private const string KeyActive = "CubeAndFlowProbe.Active";
    private const string KeyOut    = "CubeAndFlowProbe.Out";
    private const float  StepTimeout = 120f;

    private static readonly StringBuilder s_log = new();
    private static int s_pass, s_fail, s_errors, s_step = -1;
    private static double s_stepStart;
    private static bool s_hooked, s_monitor;

    // Cube invariants.
    private static int s_ghostViolations, s_fullViolations, s_overshoot, s_hitsSeen, s_missStarted, s_missShrinking, s_missRecycled, s_samples;
    private static readonly List<string> s_examples = new();
    private static readonly Dictionary<RingController, float> s_missScale = new();
    private static float s_collectedEventTime = -1f;
    private static int s_reappeared;

    // Flow.
    private static int s_started;
    private static Action<GameStartedEvent> s_onStarted;

    static CubeAndFlowProbe()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        EditorApplication.update += Tick;
    }

    public static void RunFromCommandLine()
    {
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, ContentUpdateTools.Arg("-cubeProbeOut") ?? Path.GetFullPath("Logs/CubeAndFlowProbe.txt"));
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static MusicClock Clock => MusicClock.Instance;
    private static GameplayManager Manager => UnityEngine.Object.FindFirstObjectByType<GameplayManager>();
    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;

    private static int s_startedAtStep;
    private static bool StartedSince() => s_started > s_startedAtStep;
    private static void ExpectStart() => s_startedAtStep = s_started;

    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (8f, "boot", () => Flow != GameFlowState.Boot, () =>
        {
            s_onStarted = _ => s_started++;
            EventBus.Subscribe(s_onStarted);
            AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongSelection);
        }),
        (3f, "manual selection → play", () => Rows().Count > 0, () =>
        {
            ExpectStart();
            Rows()[0].button.onClick.Invoke();
            Find<SongSelectionView>().playButton.onClick.Invoke();
        }),
        (0f, "manual selection reached the countdown", StartedSince, () =>
        {
            Check("manual selection → countdown", true);
            s_monitor = true;
        }),
        (6f, "collect a cube BEFORE its musical moment", () => Clock != null && Clock.State == MusicClockState.Playing, () =>
        {
            var rc = ActiveMusical().Where(r => !r.IsHit && r.SourceEventTime > Clock.SongTime + 0.4f).OrderBy(r => r.SourceEventTime).FirstOrDefault();
            if (rc == null) { Check("a cube ahead of its moment exists to collect", false); return; }
            s_collectedEventTime = rc.SourceEventTime;
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            typeof(RingController).GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(rc, new object[] { player.GetComponent<Collider>() });
            Check("collected before its moment: gone at once (inactive)", !rc.gameObject.activeSelf || !rc.GetComponentInChildren<Renderer>().enabled,
                  $"event {s_collectedEventTime:F3} at songTime {Clock.SongTime:F3}");
        }),
        (2f, "pause", null, () =>
        {
            Check("the collected cube never reappeared for its pulse", s_reappeared == 0, $"{s_reappeared} reappearance(s)");
            Find<PauseController>().Pause();
        }),
        (1.5f, "resume", null, () => Find<PauseController>().Resume()),
        (2f, "fall → respawn", null, () => FallRespawnSystem.Instance.TriggerFall()),
        (0f, "respawned", () => Clock.State == MusicClockState.Playing, () => { }),
        (3f, "restart", null, () => Manager.RequestRestartSong()),
        (0f, "restart playing", () => Clock.HasPlaybackStarted && Clock.State == MusicClockState.Playing, () => { }),
        (4f, "cube report → Main Menu", null, () =>
        {
            s_monitor = false;
            Check("cubes stay SMALL before their musical moment (no growth by proximity)", s_ghostViolations == 0, $"{s_ghostViolations} violation(s) over {s_samples} samples");
            Check("cubes are FULL size from their moment while collectable", s_fullViolations == 0, $"{s_fullViolations} violation(s), {s_hitsSeen} hits observed");
            Check("no overshoot (never above full size)", s_overshoot == 0, $"{s_overshoot}");
            Check("passed cubes shrink smoothly and are recycled", s_missStarted > 0 && s_missShrinking > 0 && s_missRecycled > 0,
                  $"{s_missStarted} started, {s_missShrinking} shrinking frames, {s_missRecycled} recycled");
            Check("the cube collected before its moment never came back", s_reappeared == 0, $"{s_reappeared}");
            if (s_examples.Count > 0) Log("   examples: " + string.Join(" | ", s_examples.Take(6)));
            AppBootstrap.Context.AppFlow.RequestState(GameFlowState.MainMenu);
        }),
        (3f, "CONTINUE with the previous selection still recorded", () => Flow == GameFlowState.MainMenu && Find<MainMenuView>() != null, () =>
        {
            Check("previous selection still recorded when Continue is pressed", GameSession.Instance.SelectedSong.HasValue,
                  GameSession.Instance.SelectedSong?.SongId);
            ExpectStart();
            Find<MainMenuView>().playButton.onClick.Invoke();
        }),
        (0f, "continue reached the countdown", StartedSince, () => Check("Continue → countdown (no stuck preparation)", true)),
        (3f, "Continue, cancelled during the preparation", null, () => AppBootstrap.Context.AppFlow.RequestState(GameFlowState.MainMenu)),
        (3f, "continue (to be cancelled)", () => Flow == GameFlowState.MainMenu, () =>
        {
            ExpectStart();
            Find<MainMenuView>().playButton.onClick.Invoke();
        }),
        (0f, "cancel while preparing", () => Flow == GameFlowState.SongAnalysis && SongPreparation.Current != SongPreparationStage.None, () =>
        {
            Log($"   cancelling during {SongPreparation.Current}");
            AppBootstrap.Context.AppFlow.RequestState(GameFlowState.MainMenu);
        }),
        (2f, "continue again", () => Flow == GameFlowState.MainMenu, () =>
        {
            Check("the cancelled preparation never started the run", !StartedSince(), $"started {s_started - s_startedAtStep}");
            ExpectStart();
            Find<MainMenuView>().playButton.onClick.Invoke();
        }),
        (0f, "continue after a cancel reached the countdown", StartedSince, () => Check("Continue after a cancelled one → countdown", true)),
    };

    // ── Cube invariants, every tick while monitoring ─────────────────────────────

    private static IEnumerable<RingController> ActiveMusical() =>
        UnityEngine.Object.FindObjectsByType<RingController>(FindObjectsSortMode.None).Where(r => r.gameObject.activeInHierarchy && r.IsMusical);

    private static void Monitor()
    {
        var clock = Clock;
        if (clock == null) return;
        bool playing = clock.State == MusicClockState.Playing || clock.State == MusicClockState.Warmup || clock.State == MusicClockState.Paused || clock.State == MusicClockState.Held;
        if (!playing) return;
        float t = clock.SongTime;
        var seen = new HashSet<RingController>();
        foreach (var r in ActiveMusical())
        {
            seen.Add(r);
            if (s_collectedEventTime >= 0f && Mathf.Abs(r.SourceEventTime - s_collectedEventTime) < 1e-4f && !r.IsCollected) s_reappeared++;
            float f = r.VisualScaleFactor;
            if (f > 1f + 1e-3f) { s_overshoot++; Example($"overshoot {f:F2} event {r.SourceEventTime:F2}"); }
            if (r.IsMissing)
            {
                if (s_missScale.TryGetValue(r, out float prev) && f < prev - 1e-4f) s_missShrinking++;
                if (!s_missScale.ContainsKey(r)) s_missStarted++;
                s_missScale[r] = f;
                continue;
            }
            if (r.IsCollected) continue;
            s_samples++;
            if (t < r.SourceEventTime - 0.02f && Mathf.Abs(f - r.GhostScale) > 1e-3f)
            { s_ghostViolations++; Example($"grew early: {f:F2} at {t:F3} < event {r.SourceEventTime:F3}"); }
            if (t >= r.SourceEventTime + 0.05f && clock.State == MusicClockState.Playing)
            {
                if (Mathf.Abs(f - 1f) > 1e-3f) { s_fullViolations++; Example($"not full {f:F2} at {t:F3} ≥ event {r.SourceEventTime:F3}"); }
                else if (r.IsHit) s_hitsSeen++;
            }
        }
        foreach (var k in s_missScale.Keys.ToList())
            if (!seen.Contains(k) || !k.IsMissing) { s_missRecycled++; s_missScale.Remove(k); }
    }

    private static void Example(string e) { if (s_examples.Count < 20) s_examples.Add(e); }

    // ── Driver ───────────────────────────────────────────────────────────────────

    private static List<SongRowView> Rows() =>
        UnityEngine.Object.FindObjectsByType<SongRowView>(FindObjectsSortMode.None)
            .Where(r => r.isActiveAndEnabled && !r.Played)
            .OrderBy(r => r.transform.GetSiblingIndex()).ToList();

    private static T Find<T>() where T : Component =>
        UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

    private static void Tick()
    {
        if (!SessionState.GetBool(KeyActive, false)) { EditorApplication.update -= Tick; return; }
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        try
        {
            if (!s_hooked)
            {
                s_hooked = true;
                Application.logMessageReceived += (msg, stack, type) =>
                {
                    if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) { s_errors++; s_log.AppendLine($"  [{type}] {msg}"); }
                };
            }
            if (s_step < 0) { s_step = 0; s_stepStart = now; }
            if (s_monitor) Monitor();
            if (s_step >= Steps.Length) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = Steps[s_step];
            if (now - s_stepStart > StepTimeout + step.wait) { Log($"TIMEOUT at step {s_step} ({step.name}) flow={Flow} prep={SongPreparation.Current}"); Finish(2); return; }
            if (now - s_stepStart < step.wait) return;
            if (step.until != null && !step.until()) return;
            Log($"-- {step.name} (flow {Flow}, songTime {(Clock != null ? Clock.SongTime : 0f):F2})");
            step.act?.Invoke();
            s_step++;
            s_stepStart = now;
        }
        catch (Exception ex) { Log("EXCEPTION " + ex); Finish(2); }
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    private static void Log(string msg) { s_log.AppendLine(msg); Debug.Log("[CubeAndFlowProbe] " + msg); }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        if (s_onStarted != null) EventBus.Unsubscribe(s_onStarted);
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors logged (exit {code})");
        try { File.WriteAllText(SessionState.GetString(KeyOut, Path.GetFullPath("Logs/CubeAndFlowProbe.txt")), s_log.ToString()); }
        catch (Exception ex) { Debug.LogError("[CubeAndFlowProbe] report: " + ex.Message); }
        EditorApplication.Exit(code);
    }
}
#endif
