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
/// Focused Play Mode probe of two fixes, inside the REAL flow (boots Core.unity like the game):
///   SELECTION — a selection cleared while its preparation runs must recover (back to Song Selection, bounded);
///     manual Play records the selection BEFORE entering SongAnalysis (one generation, already present); a
///     selection replaced right after entering SongAnalysis (the old CONTINUE race: the previous song present,
///     the new one recorded a frame later) must prepare and start the NEW song, never publish the old one.
///   MUSICAL CUBES — every frame: before its eventTime a cube stays at ghost scale (no proximity growth); from
///     eventTime it is full size (exactly 1, no overshoot), never earlier; a cube collected before / after its
///     beat disappears at once and never comes back; a passed cube shrinks monotonically and is recycled;
///     no size change while paused; no inherited state after a fall respawn or a restart.
/// Ends before the song finishes (nothing completed). It boots the real AppBootstrap (selection writes the
/// save): back the save up before running.
/// Batch: -executeMethod CubeBeatSelectionProbe.RunFromCommandLine [-cubeProbeOut FILE]
/// Exit code 0 = all checks passed, 1 = a check failed, 2 = timeout/exception.
/// </summary>
[InitializeOnLoad]
public static class CubeBeatSelectionProbe
{
    private const string KeyActive = "CubeBeatSelectionProbe.Active";
    private const string KeyOut    = "CubeBeatSelectionProbe.Out";

    private static readonly StringBuilder s_log = new();
    private static int    s_pass, s_fail, s_errors, s_expectedErrors;
    private static int    s_step = -1;
    private static double s_stepStart, s_hardDeadline;
    private static bool   s_hooked;

    private static MusicClock      Clock   => MusicClock.Instance;
    private static GameplayManager Manager => UnityEngine.Object.FindFirstObjectByType<GameplayManager>();
    private static int Gen => GameSession.Instance != null ? GameSession.Instance.SelectionGeneration : -1;
    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;

    // Selection bookkeeping.
    private static readonly List<int> s_profileGens = new();
    private static readonly List<int> s_startedGens = new();
    private static Action<SongProfileReadyEvent> s_onProfile;
    private static Action<GameStartedEvent>      s_onStarted;
    private static Action<RingCollectedEvent>    s_onCollected;
    private static int    s_genBefore, s_genA, s_genB;
    private static string s_songA, s_songB;

    // Cube monitor.
    private static bool  s_monitor;
    private static int   s_frames, s_ghostSeen, s_hitSeen, s_grewEarly, s_hitEarly, s_overshoot, s_lateHit, s_changedWhilePaused;
    private static int   s_missStarted, s_missRecycled, s_missGrew, s_reappeared;
    private static readonly List<string> s_samples = new();
    private static readonly Dictionary<RingController, float> s_missScale = new();
    private static readonly HashSet<(RingType, float)> s_collectedKeys = new();
    private static readonly Dictionary<RingController, float> s_pausedScale = new();
    private static int   s_collectedBefore, s_collectedAfter;

    // Spatial relations at the hit (musical front / playhead line / trailing player), camera, surge, timing.
    private static readonly HashSet<(RingType, float)> s_hitKeys = new();
    private static int   s_relHits, s_relLineBad, s_relPlayerBad, s_cameraBad, s_cameraSamples;
    private static float s_worstLine, s_worstGap, s_minGap = float.MaxValue, s_maxGap;
    private static bool  s_autopilot, s_forcing;
    private static readonly List<(float err, float errNoTrail)> s_natural = new();
    private static float s_surgeOffset;
    private static PlayerController Player => UnityEngine.Object.FindFirstObjectByType<PlayerController>();
    private static readonly FieldInfo ForwardOffsetField = typeof(PlayerController).GetField("_forwardOffset", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo LateralOffsetField = typeof(PlayerController).GetField("_lateralOffset", BindingFlags.Instance | BindingFlags.NonPublic);

    static CubeBeatSelectionProbe()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        EditorApplication.update += Tick;
    }

    public static void RunFromCommandLine()
    {
        string outPath = Path.GetFullPath("Logs/CubeBeatSelectionProbe.txt");
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-cubeProbeOut") outPath = args[i + 1];
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, outPath);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    // (seconds to wait BEFORE, name, condition to wait for (null = none), action)
    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (12f, "boot → song selection", null, () =>
        {
            Subscribe();
            AppFlowController.Instance.RequestState(GameFlowState.SongSelection);
        }),
        (4f,  "select first playable", null, () => FirstPlayableRow().button.onClick.Invoke()),

        // 1) Cancellation during the load: the selection is cleared while the preparation runs.
        (1f,  "play", null, ClickPlay),
        (0f,  "SongAnalysis entered → clear the selection while loading", () => Flow == GameFlowState.SongAnalysis, () =>
        {
            CheckRecordedBeforeEntering();
            s_genA  = Gen;
            GameSession.Instance.SelectedSong = null;
            s_expectedErrors++; // the bounded wait for a selection fails on purpose → Song Selection
        }),
        (0f,  "wait for the recovery to Song Selection", () => Flow == GameFlowState.SongSelection, () =>
        {
            Check("cleared selection: never published, screen recovered to Song Selection",
                  !s_profileGens.Contains(s_genA) && s_startedGens.Count == 0, $"profiles={string.Join(",", s_profileGens)}");
        }),

        // 2) Selection replaced right after entering SongAnalysis (the CONTINUE race).
        (2f,  "select first playable again", null, () => FirstPlayableRow().button.onClick.Invoke()),
        (1f,  "play", null, ClickPlay),
        (0f,  "SongAnalysis entered", () => Flow == GameFlowState.SongAnalysis, () =>
        {
            CheckRecordedBeforeEntering();
            s_genA = Gen;
        }),
        (0.1f, "replace the selection during the preparation", null, () =>
        {
            s_songB = OtherSongId(s_songA);
            Log($"preparing {s_songA} (#{s_genA}) at stage {SongPreparation.Current} → replaced by {s_songB}");
            GameSession.Instance.SelectedSong = new SelectedSongInfo { SongId = s_songB, DisplayName = SongCatalog.DisplayNameFor(s_songB) };
            s_genB = Gen;
        }),
        (0f,  "wait for warmup", () => Clock != null && Manager != null && Manager.IsRunning && Clock.State == MusicClockState.Warmup, () =>
        {
            Check("the CURRENT selection was prepared and started", GameSession.Instance.SelectedSong?.SongId == s_songB
                  && s_profileGens.Contains(s_genB) && s_startedGens.Contains(s_genB),
                  $"song={GameSession.Instance.SelectedSong?.SongId} profiles={string.Join(",", s_profileGens)} started={string.Join(",", s_startedGens)}");
            Check("the superseded selection never published", !s_profileGens.Contains(s_genA) && !s_startedGens.Contains(s_genA));
            RunnerHazardSystem.Instance?.ClearGeneratedForTest(); // cube timing only: no obstacle in the way (HazardNavigationProbe covers them)
            s_monitor = true;
        }),

        // 3) Musical cubes (the monitor runs every frame from here on).
        (0f,  "wait for playback", () => Clock.HasPlaybackStarted, null),
        (3f,  "collect one cube BEFORE its beat", null, () => ForceCollect(beforeBeat: true)),
        (2f,  "collect one cube AFTER its beat", null, () => ForceCollect(beforeBeat: false)),
        (0.5f, "surge (offset at its maximum, then released)", null, () =>
        {
            var p = Player;
            ForwardOffsetField.SetValue(p, Manager.Config.core.maxSurge);
            s_surgeOffset = p.SongOffset;
            Check("surge: the player moves ahead of its reference, the reference (camera/schedule) does not", p.ActualDistance - p.CanonicalDistance > 0.5f
                  && Mathf.Abs(p.MusicFrontDistance - p.CanonicalDistance - p.TrailDistance) < 1e-3f,
                  $"offset={p.SongOffset:F2} trail={p.TrailDistance:F2}");
        }),
        (3.5f, "surge returned → autopilot over the cubes", null, () =>
        {
            Check("surge: auto-return to the reference position", Mathf.Abs(Player.SongOffset) < 0.05f, $"offset {s_surgeOffset:F2} → {Player.SongOffset:F3}");
            s_autopilot = true;
        }),
        (8f,  "autopilot off → pause", null, () =>
        {
            s_autopilot = false;
            Find<PauseController>().Pause();
            s_pausedScale.Clear();
            foreach (var rc in ActiveMusical()) s_pausedScale[rc] = rc.VisualScaleFactor;
        }),
        (1.5f, "resume", null, () =>
        {
            foreach (var kv in s_pausedScale)
                if (kv.Key != null && kv.Key.gameObject.activeInHierarchy && Mathf.Abs(kv.Key.VisualScaleFactor - kv.Value) > 1e-4f) s_changedWhilePaused++;
            Check("paused: no cube changed size", s_changedWhilePaused == 0, $"{s_pausedScale.Count} cubes, {s_changedWhilePaused} changed");
            Find<PauseController>().Resume();
        }),
        (3f,  "force a fall", null, () => FallRespawnSystem.Instance.TriggerFall()),
        (0f,  "wait for respawn", () => Clock.State == MusicClockState.Playing, null),
        (4f,  "after respawn → restart", null, () => { s_collectedKeys.Clear(); Manager.RequestRestartSong(); }), // a restart legitimately respawns every cube
        (0f,  "wait for restart playback", () => Clock.HasPlaybackStarted, null),
        (5f,  "report", null, () =>
        {
            s_monitor = false;
            Log($"cube monitor: {s_frames} frames, {s_ghostSeen} ghost samples, {s_hitSeen} full-size samples");
            Check("before its beat a cube is never grown (proximity or spawn)", s_grewEarly == 0, $"{s_grewEarly} samples");
            Check("never full size before its eventTime", s_hitEarly == 0, $"{s_hitEarly} samples");
            Check("full size is exactly 1 (no overshoot)", s_overshoot == 0, $"{s_overshoot} samples");
            Check("full size at its eventTime (≤ 1 frame + 20 ms late)", s_lateHit == 0, $"{s_lateHit} samples");
            Check("cubes really observed small and then full", s_ghostSeen > 0 && s_hitSeen > 0);
            Check("collected cubes (before + after the beat) never came back", s_collectedBefore > 0 && s_collectedAfter > 0 && s_reappeared == 0,
                  $"before={s_collectedBefore} after={s_collectedAfter} reappeared={s_reappeared}");
            Check("passed cubes shrink monotonically and are recycled", s_missStarted > 0 && s_missGrew == 0 && s_missRecycled > 0,
                  $"started={s_missStarted} recycled={s_missRecycled} grew={s_missGrew}");
            float trail = Player.TrailDistance;
            Check("at the hit the playhead line passes under the cube (|line − cube| < 0.25 m)", s_relHits > 0 && s_relLineBad == 0,
                  $"{s_relHits} hits, worst {s_worstLine:F3} m");
            Check($"at the hit the cube is AHEAD of the player by the trail ({trail:F2} m ± 0.25)", s_relHits > 0 && s_relPlayerBad == 0,
                  $"gap {s_minGap:F2}..{s_maxGap:F2} m, worst deviation {s_worstGap:F3} m");
            Check("camera stays behind the player, in range", s_cameraSamples > 0 && s_cameraBad == 0, $"{s_cameraSamples} samples, {s_cameraBad} bad");
            if (s_natural.Count > 0)
                Log($"natural pickups: {s_natural.Count}, mean |error| {s_natural.Average(n => n.err) * 1000f:F0} ms (vs the hit moment alone: {s_natural.Average(n => n.errNoTrail) * 1000f:F0} ms)");
            Check("normal pickups not penalised by the trail (mean |error| < 120 ms = flat full-score zone)",
                  s_natural.Count >= 3 && s_natural.Average(n => n.err) < 0.12f, $"{s_natural.Count} pickups");
            foreach (var s in s_samples.Take(10)) Log("   " + s);
        }),
    };

    // ── Cube helpers ─────────────────────────────────────────────────────────

    private static IEnumerable<RingController> ActiveMusical() =>
        UnityEngine.Object.FindObjectsByType<RingController>(FindObjectsSortMode.None)
            .Where(rc => rc.gameObject.activeInHierarchy && rc.IsMusical);

    private static void MonitorCubes()
    {
        if (Clock == null || Manager == null || !Manager.IsRunning) return;
        var state = Clock.State;
        if (state != MusicClockState.Playing && state != MusicClockState.Warmup) return;
        s_frames++;
        float t = Clock.SongTime;
        float frame = Mathf.Max(Time.deltaTime, 1f / 30f);
        foreach (var rc in ActiveMusical())
        {
            if (s_collectedKeys.Contains((rc.Type, rc.SourceEventTime)) && !rc.IsMissing) { s_reappeared++; Sample($"collected cube back: {rc.Type} @{rc.SourceEventTime:F3}"); }
            if (rc.IsMissing)
            {
                float scale = rc.VisualScaleFactor;
                if (s_missScale.TryGetValue(rc, out float prev)) { if (scale > prev + 1e-4f) { s_missGrew++; Sample($"missed cube grew {prev:F3}→{scale:F3}"); } }
                else s_missStarted++;
                s_missScale[rc] = scale;
                continue;
            }
            if (rc.IsCollected) continue;
            float dt = t - rc.SourceEventTime;
            float f  = rc.VisualScaleFactor;
            if (!rc.IsHit)
            {
                if (dt < 0f)
                {
                    s_ghostSeen++;
                    if (Mathf.Abs(f - rc.GhostScale) > 0.01f) { s_grewEarly++; Sample($"{rc.Type} @{rc.SourceEventTime:F3}: scale {f:F3} {-dt:F3} s before its beat"); }
                }
                else if (dt > frame + 0.02f) { s_lateHit++; Sample($"{rc.Type} @{rc.SourceEventTime:F3}: still small {dt:F3} s after its beat"); }
            }
            else
            {
                s_hitSeen++;
                if (dt < -1e-4f) { s_hitEarly++; Sample($"{rc.Type} @{rc.SourceEventTime:F3}: full {-dt:F3} s before its beat"); }
                if (Mathf.Abs(f - 1f) > 1e-3f) { s_overshoot++; Sample($"{rc.Type} @{rc.SourceEventTime:F3}: hit scale {f:F3}"); }
            }
        }
        MonitorRelations(t, frame);
        // Missing cubes no longer active = recycled.
        foreach (var rc in s_missScale.Keys.ToList())
            if (rc == null || !rc.gameObject.activeInHierarchy || !rc.IsMissing) { s_missRecycled++; s_missScale.Remove(rc); }
    }

    private static void MonitorRelations(float t, float frame)
    {
        var p = Player;
        if (p == null) return;
        float speed = Manager.Config.core.playerSpeed;
        float line  = Shader.GetGlobalFloat("_PlayheadMusicDistance");
        foreach (var rc in ActiveMusical())
        {
            if (!rc.IsHit || rc.IsCollected || rc.IsMissing || !s_hitKeys.Add((rc.Type, rc.SourceEventTime))) continue;
            float dt = t - rc.SourceEventTime;
            if (dt < 0f || dt > frame + 0.02f || Mathf.Abs(p.SongOffset) > 0.01f || p.IsFalling) continue; // a fresh hit, no surge
            float cube        = rc.SourceEventTime * speed;   // its eventDistance
            float lineAtHit   = line - dt * speed;            // back to the hit instant
            float playerAtHit = p.ActualDistance - dt * speed;
            float gap = cube - playerAtHit;
            s_relHits++;
            s_worstLine = Mathf.Max(s_worstLine, Mathf.Abs(lineAtHit - cube));
            s_worstGap  = Mathf.Max(s_worstGap, Mathf.Abs(gap - p.TrailDistance));
            s_minGap = Mathf.Min(s_minGap, gap); s_maxGap = Mathf.Max(s_maxGap, gap);
            if (Mathf.Abs(lineAtHit - cube) > 0.25f) { s_relLineBad++; Sample($"line {lineAtHit:F2} vs cube {cube:F2}"); }
            if (Mathf.Abs(gap - p.TrailDistance) > 0.25f) { s_relPlayerBad++; Sample($"player gap {gap:F2} vs trail {p.TrailDistance:F2}"); }
        }
        // Camera: behind the player along the path, within a sane range (third person).
        var cam = CameraFollow.Instance != null ? CameraFollow.Instance.transform : null;
        if (cam != null && s_frames % 15 == 0 && !p.IsFalling)
        {
            Vector3 tangent  = MusicWorldManager.Instance.Path.GetSample(p.CanonicalDistance).tangent;
            Vector3 toPlayer = p.transform.position - cam.position;
            s_cameraSamples++;
            if (Vector3.Dot(toPlayer, tangent) < -0.5f || toPlayer.magnitude > 40f) { s_cameraBad++; Sample($"camera {toPlayer.magnitude:F1} m, along {Vector3.Dot(toPlayer, tangent):F1}"); }
        }
        // Autopilot: line up laterally with the next low cube so it is met by the normal (non-surge) run.
        if (s_autopilot && Manager.Timeline != null)
        {
            float d = p.ActualDistance;
            var next = Manager.Timeline.Events.Where(e => e.eventType == EventType.Ring && RingTypes.IsMusical(e.ringType) && e.eventDistance > d + 0.5f && e.verticalOffset < 1.2f)
                                              .OrderBy(e => e.eventDistance).FirstOrDefault();
            if (next.eventDistance > 0f) LateralOffsetField.SetValue(p, next.lateralOffset);
        }
    }

    private static void ForceCollect(bool beforeBeat)
    {
        float t = Clock.SongTime;
        var rc = ActiveMusical().Where(r => !r.IsCollected && !r.IsMissing && (beforeBeat ? r.SourceEventTime > t + 0.2f : r.IsHit))
                                .OrderBy(r => Mathf.Abs(r.SourceEventTime - t)).FirstOrDefault();
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var col = player != null ? player.GetComponent<Collider>() : null;
        if (rc == null || col == null) { Log($"no cube to collect ({(beforeBeat ? "before" : "after")} the beat) or no player collider"); return; }
        var key = (rc.Type, rc.SourceEventTime);
        bool wasHit = rc.IsHit;
        s_forcing = true;
        typeof(RingController).GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rc, new object[] { col });
        s_forcing = false;
        bool gone = !rc.gameObject.activeInHierarchy || rc.SourceEventTime != key.Item2;
        Check($"collected {(beforeBeat ? "before" : "after")} its beat → gone at once", gone && wasHit == !beforeBeat,
              $"{key.Item1} @{key.Item2:F3} t={t:F3} hit={wasHit} active={rc.gameObject.activeInHierarchy}");
        if (!gone) return;
        s_collectedKeys.Add(key);
        if (beforeBeat) s_collectedBefore++; else s_collectedAfter++;
    }

    private static void Sample(string s) { if (s_samples.Count < 40) s_samples.Add($"t={Clock.SongTime:F3} {s}"); }

    // ── Selection helpers ────────────────────────────────────────────────────

    // SelectSong records the selection a frame after the click (nested coroutine) and only then requests
    // SongAnalysis — so the flow is still Song Selection right after the click.
    private static void ClickPlay()
    {
        s_genBefore = Gen;
        Find<SongSelectionView>().playButton.onClick.Invoke();
        Check("Play: nothing entered before the selection is recorded", Flow == GameFlowState.SongSelection || Gen == s_genBefore + 1, $"flow={Flow} gen={Gen}");
    }

    // The very frame SongAnalysis is entered, the NEW selection is already there (one new generation).
    private static void CheckRecordedBeforeEntering()
    {
        s_songA = GameSession.Instance.SelectedSong?.SongId;
        Check("selection recorded before SongAnalysis (one new generation, song present)", Gen == s_genBefore + 1 && s_songA != null,
              $"gen {s_genBefore}→{Gen} selected={s_songA}");
    }

    private static string OtherSongId(string except)
    {
        for (int n = 1; n < 200; n++)
        {
            string id = PlayableSongCatalogSO.FormatId(n);
            if (id != except && SongCatalog.IsKnownId(id)) return id;
        }
        return except;
    }

    private static void Subscribe()
    {
        s_onProfile   = e => s_profileGens.Add(e.SelectionGeneration);
        s_onStarted   = _ => s_startedGens.Add(Gen);
        s_onCollected = e =>
        {
            if (s_forcing || !s_autopilot || !RingTypes.IsMusical(e.Type)) return;
            s_natural.Add((e.TimingError, Mathf.Abs(e.ActualTime - (e.ExpectedTime - Player.TrailSeconds))));
        };
        EventBus.Subscribe(s_onCollected);
        EventBus.Subscribe(s_onProfile);
        EventBus.Subscribe(s_onStarted);
    }

    private static SongRowView FirstPlayableRow() =>
        UnityEngine.Object.FindObjectsByType<SongRowView>(FindObjectsSortMode.None)
            .OrderBy(r => r.transform.GetSiblingIndex())
            .First(r => r.button.interactable && !r.Played);

    private static T Find<T>() where T : Component =>
        UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

    // ── Driver (same shape as RunnerSyncIntegrationProbe) ─────────────────────

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
                    if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                    {
                        s_errors++;
                        s_log.AppendLine($"  [{type}] {msg}");
                    }
                };
            }
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_hardDeadline = now + 300; }
            if (now > s_hardDeadline) { Log($"TIMEOUT at step {s_step} ({(s_step < Steps.Length ? Steps[s_step].name : "end")}) flow={Flow} stage={SongPreparation.Current}"); Finish(2); return; }

            if (s_monitor) MonitorCubes();

            if (s_step >= Steps.Length) { Finish(s_fail == 0 && s_errors == s_expectedErrors ? 0 : 1); return; }
            var step = Steps[s_step];
            if (now - s_stepStart < step.wait) return;
            if (step.until != null && !step.until()) return;
            Log($"-- {step.name} (flow={Flow}, gen={Gen}, songTime={(Clock != null ? Clock.SongTime : 0f):F3}, state={Clock?.State})");
            step.act?.Invoke();
            s_step++;
            s_stepStart = now;
        }
        catch (Exception ex)
        {
            Log("EXCEPTION " + ex);
            Finish(2);
        }
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    private static void Log(string msg)
    {
        s_log.AppendLine(msg);
        Debug.Log("[CubeBeatSelectionProbe] " + msg);
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        if (s_onProfile != null) EventBus.Unsubscribe(s_onProfile);
        if (s_onStarted != null) EventBus.Unsubscribe(s_onStarted);
        if (s_onCollected != null) EventBus.Unsubscribe(s_onCollected);
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors logged ({s_expectedErrors} expected) (exit {code})");
        try
        {
            string outPath = SessionState.GetString(KeyOut, Path.GetFullPath("Logs/CubeBeatSelectionProbe.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, s_log.ToString());
        }
        catch (Exception ex) { Debug.LogError("[CubeBeatSelectionProbe] could not write report: " + ex.Message); }
        EditorApplication.Exit(code);
    }
}
#endif
