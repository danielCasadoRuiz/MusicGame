#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play Mode integration probe of the music sync layer inside the REAL Runner flow (phase P1):
/// boots Core.unity like the game, picks the first playable song in Song Selection, presses Play,
/// then checks on the real GameplayManager / FallRespawnSystem / PauseController / MusicClock:
///   warmup (capped, no musical pulses) → playback handoff → pause → a forced fall (clock Held, no
///   pulses while held, respawn at the fall moment, no pulse burst after it) → restart (Warmup again
///   at the play-range start) → play-range end → SongFinishedEvent → farewell → GameEndedEvent.
/// Every BeatPulseEvent is checked against the clock: it must arrive at its own musical moment
/// (eventTime − pulseLeadTime), never as a late burst of earlier moments.
///
/// It boots the real AppBootstrap, so it reads/writes the real save: back it up before running and
/// restore it afterwards (the run ends a song).
/// Batch: -executeMethod RunnerSyncIntegrationProbe.RunFromCommandLine [-runnerProbeOut "C:/path/report.txt"]
///        [-builtContent [-cleanInstall]] (the song from built remote bundles via BuiltContentPlayMode)
/// Exit code 0 = all checks passed, 1 = a check failed, 2 = timeout/exception.
/// </summary>
[InitializeOnLoad]
public static class RunnerSyncIntegrationProbe
{
    private const string KeyActive = "RunnerSyncProbe.Active";
    private const string KeyOut    = "RunnerSyncProbe.Out";

    private static readonly StringBuilder s_log = new();
    private static int    s_pass, s_fail, s_errors;
    private static int    s_step = -1;
    private static double s_stepStart, s_hardDeadline;
    private static bool   s_hooked;

    private static MusicClock      Clock   => MusicClock.Instance;
    private static GameplayManager Manager => UnityEngine.Object.FindFirstObjectByType<GameplayManager>();

    // Pulse bookkeeping.
    private static readonly List<(float eventTime, float songTimeAtReceipt, MusicClockState state)> s_pulses = new();
    private static float s_lead = 0.05f;
    private static int   s_finished, s_ended;
    private static Action<BeatPulseEvent>    s_onPulse;
    private static Action<SongFinishedEvent> s_onFinished;
    private static Action<GameEndedEvent>    s_onEnded;

    // Values carried between steps.
    private static float s_a, s_fallSong;
    private static int   s_pulsesBefore;
    private static float s_maxDeviation, s_prevSong, s_prevGame;
    private static bool  s_track;

    static RunnerSyncIntegrationProbe()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        EditorApplication.update += Tick;
    }

    public static void RunFromCommandLine()
    {
        string outPath = Path.GetFullPath("Logs/RunnerSyncIntegrationProbe.txt");
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-runnerProbeOut") outPath = args[i + 1];
        // -builtContent: the song comes from REALLY BUILT remote bundles (LocalContentServer, served locally).
        if (args.Contains("-builtContent") && !BuiltContentPlayMode.Enter(cleanInstall: args.Contains("-cleanInstall"), out string error))
        { Debug.LogError("[RunnerSyncProbe] " + error); EditorApplication.Exit(2); return; }
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, outPath);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    // (seconds to wait BEFORE, name, action; a null action with wait < 0 = wait for a condition)
    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (12f, "boot → song selection", null, () => AppFlowController.Instance.RequestState(GameFlowState.SongSelection)),
        (4f,  "select first playable", null, () => FirstPlayableRow().button.onClick.Invoke()),
        (1f,  "play",                  null, () => Find<SongSelectionView>().playButton.onClick.Invoke()),
        (0f,  "wait for warmup",       () => Clock != null && Manager != null && Manager.IsRunning && Clock.State == MusicClockState.Warmup, () =>
        {
            Subscribe();
            var m = Manager;
            s_lead = m.Config.collectibles.pulseLeadTime;
            Log($"song running: playStart={m.SongPlayStart:F2} playEnd={m.SongPlayEnd:F2} warmup={Clock.WarmupTime:F2} events={m.Timeline.Events.Length} lead={s_lead:F3}");
            Check("warmup counts from the play-range start", Clock.SongTime >= Clock.StartClipTime - 1e-4f && Clock.SongTime <= Clock.PlaybackStartSongTime + 1e-4f,
                  $"t={Clock.SongTime:F3} start={Clock.StartClipTime:F2} handoff={Clock.PlaybackStartSongTime:F2}");
            StartTracking();
        }),
        (0f,  "wait for playback",     () => Clock.HasPlaybackStarted, () =>
        {
            Check("no musical pulse during the warmup", s_pulses.Count == 0 || s_pulses.All(p => p.eventTime >= Clock.PlaybackStartSongTime),
                  $"pulses={s_pulses.Count}");
            Check("handoff at warmup + start", Mathf.Abs(Clock.SongTime - Clock.PlaybackStartSongTime) < 0.1f, $"t={Clock.SongTime:F3}");
        }),
        (4f,  "playing → pause",       null, () =>
        {
            StopTracking("warmup + 4 s of play");
            float audio = Manager != null ? AudioClipTime() : -1f;
            Check("ClipTime tracks the AudioSource (<60 ms)", Mathf.Abs(Clock.ClipTime - audio) < 0.06f, $"clock={Clock.ClipTime:F3} audio={audio:F3}");
            Check("pulses fired while playing", s_pulses.Count > 0, $"pulses={s_pulses.Count}");
            Find<PauseController>().Pause();
            s_a = Clock.SongTime;
            s_pulsesBefore = s_pulses.Count;
        }),
        (1.5f, "resume",               null, () =>
        {
            Check("pause freezes SongTime + no pulses", Clock.SongTime == s_a && Clock.State == MusicClockState.Paused && s_pulses.Count == s_pulsesBefore,
                  $"t={Clock.SongTime:F3} vs {s_a:F3} state={Clock.State}");
            Find<PauseController>().Resume();
            StartTracking();
        }),
        (3f,  "force a fall",          null, () =>
        {
            StopTracking("after resume");
            s_fallSong = Clock.SongTime;
            FallRespawnSystem.Instance.TriggerFall();
        }),
        (0f,  "wait for hold",         () => Clock.State == MusicClockState.Held, () =>
        {
            s_a = Clock.SongTime;
            s_pulsesBefore = s_pulses.Count;
        }),
        (0.3f, "held",                 null, () =>
        {
            Check("held: SongTime frozen while the audio is stopped (no wall-clock jump)", Clock.SongTime == s_a && Clock.State == MusicClockState.Held,
                  $"t={Clock.SongTime:F4} vs {s_a:F4} state={Clock.State}");
            Check("no pulses while held", s_pulses.Count == s_pulsesBefore);
        }),
        (0f,  "wait for respawn",      () => Clock.State == MusicClockState.Playing, () =>
        {
            Check("respawn resumes at the fall moment", Mathf.Abs(Clock.SongTime - s_fallSong) < 0.1f, $"t={Clock.SongTime:F3} fall={s_fallSong:F3}");
            s_pulsesBefore = s_pulses.Count;
            StartTracking();
        }),
        (3f,  "after respawn → restart", null, () =>
        {
            StopTracking("after respawn");
            var after = s_pulses.Skip(s_pulsesBefore).ToList();
            Check("no burst of pre-fall moments after the respawn", after.All(p => p.eventTime >= s_fallSong - 0.01f),
                  $"{after.Count} pulses, earliest eventTime {(after.Count > 0 ? after.Min(p => p.eventTime) : 0f):F3} vs fall {s_fallSong:F3}");
            Manager.RequestRestartSong();
        }),
        (0.5f, "restart warmup",       null, () =>
        {
            Check("restart → Warmup from the play-range start", Clock.State == MusicClockState.Warmup && !Clock.HasPlaybackStarted
                  && Clock.SongTime <= Clock.PlaybackStartSongTime + 1e-4f, $"state={Clock.State} t={Clock.SongTime:F3}");
            s_pulsesBefore = s_pulses.Count;
        }),
        (0f,  "wait for restart playback", () => Clock.HasPlaybackStarted, () =>
        {
            // Same rule as the first countdown: a pulse may only belong to a moment AT or after the handoff (a hit in
            // the song's first pulseLeadTime legitimately pulses during the countdown's last lead seconds).
            var restartPulses = s_pulses.Skip(s_pulsesBefore).ToList();
            Check("no pulses of earlier moments during the restart countdown", restartPulses.All(p => p.eventTime >= Clock.PlaybackStartSongTime),
                  $"{restartPulses.Count} pulses" + string.Concat(restartPulses.Select(p => $" [event {p.eventTime:F3} at {p.songTimeAtReceipt:F3}]")));
            s_hardDeadline = EditorApplication.timeSinceStartup + 200;
        }),
        (0f,  "wait for the song end", () => s_finished > 0, () =>
        {
            Check("SongFinishedEvent at the play-range end", Clock.State == MusicClockState.Farewell && Clock.ClipTime >= Manager.SongPlayEnd - 0.1f,
                  $"state={Clock.State} clip={Clock.ClipTime:F3} end={Manager.SongPlayEnd:F3} natural={Clock.NaturalEndDetected}");
        }),
        (0f,  "wait for GameEnded",    () => s_ended > 0, () =>
        {
            Check("GameEndedEvent after the farewell", s_ended == 1 && Clock.State == MusicClockState.Stopped, $"ended={s_ended} state={Clock.State}");
            Check("every pulse arrived at its own musical moment (|receipt − (eventTime − lead)| < 0.1 s)",
                  s_pulses.All(p => Mathf.Abs(p.songTimeAtReceipt - (p.eventTime - s_lead)) < 0.1f),
                  $"{s_pulses.Count} pulses, worst {(s_pulses.Count > 0 ? s_pulses.Max(p => Mathf.Abs(p.songTimeAtReceipt - (p.eventTime - s_lead))) : 0f):F3} s" +
                  string.Concat(s_pulses.Where(p => Mathf.Abs(p.songTimeAtReceipt - (p.eventTime - s_lead)) >= 0.05f).Take(8).Select(p => $" [event {p.eventTime:F3} received at {p.songTimeAtReceipt:F3} ({p.state})]")));
        }),
    };

    private static float AudioClipTime()
    {
        var src = UnityEngine.Object.FindObjectsByType<AudioSystemBootstrapper>(FindObjectsSortMode.None).FirstOrDefault()?.AudioSource;
        return src != null && src.clip != null ? (float)MusicTime.SamplesToSeconds(src.timeSamples, src.clip.frequency) : -1f;
    }

    private static void Subscribe()
    {
        s_onPulse    = e => s_pulses.Add((e.EventTime, Clock != null ? Clock.SongTime : -1f, Clock != null ? Clock.State : MusicClockState.Stopped));
        s_onFinished = _ => s_finished++;
        s_onEnded    = _ => s_ended++;
        EventBus.Subscribe(s_onPulse);
        EventBus.Subscribe(s_onFinished);
        EventBus.Subscribe(s_onEnded);
    }

    private static SongRowView FirstPlayableRow() =>
        UnityEngine.Object.FindObjectsByType<SongRowView>(FindObjectsSortMode.None)
            .OrderBy(r => r.transform.GetSiblingIndex())
            .First(r => r.button.interactable && !r.Played);

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
                    if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                    {
                        s_errors++;
                        s_log.AppendLine($"  [{type}] {msg}");
                    }
                };
            }
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_hardDeadline = now + 240; }
            if (now > s_hardDeadline) { Log($"TIMEOUT at step {s_step} ({(s_step < Steps.Length ? Steps[s_step].name : "end")}) state={Clock?.State} t={Clock?.SongTime:F3}"); Finish(2); return; }

            if (s_track && Clock != null)
            {
                float songStep = Clock.SongTime - s_prevSong;
                if (Clock.State == MusicClockState.Playing || Clock.State == MusicClockState.Warmup)
                    s_maxDeviation = Mathf.Max(s_maxDeviation, Mathf.Abs(songStep - (Time.time - s_prevGame)));
                s_prevSong = Clock.SongTime;
                s_prevGame = Time.time;
            }

            if (s_step >= Steps.Length) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = Steps[s_step];
            if (now - s_stepStart < step.wait) return;
            if (step.until != null && !step.until()) return;
            Log($"-- {step.name} (songTime={(Clock != null ? Clock.SongTime : 0f):F3}, state={Clock?.State})");
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

    private static void StartTracking()
    {
        s_track = true;
        s_maxDeviation = 0f;
        s_prevSong = Clock.SongTime;
        s_prevGame = Time.time;
    }

    private static void StopTracking(string label)
    {
        s_track = false;
        Check($"{label}: no SongTime jump vs game time (<100 ms)", s_maxDeviation < 0.1f, $"max deviation {s_maxDeviation * 1000f:F1} ms");
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    private static void Log(string msg)
    {
        s_log.AppendLine(msg);
        Debug.Log("[RunnerSyncProbe] " + msg);
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        if (s_onPulse    != null) EventBus.Unsubscribe(s_onPulse);
        if (s_onFinished != null) EventBus.Unsubscribe(s_onFinished);
        if (s_onEnded    != null) EventBus.Unsubscribe(s_onEnded);
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors/exceptions logged (exit {code})");
        Log(PulseTimingDiagnostics.Summary());
        try { BuiltContentPlayMode.Restore(); } catch (Exception ex) { Log("could not restore Addressables settings: " + ex.Message); }
        try
        {
            string outPath = SessionState.GetString(KeyOut, Path.GetFullPath("Logs/RunnerSyncIntegrationProbe.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, s_log.ToString());
        }
        catch (Exception ex) { Debug.LogError("[RunnerSyncProbe] could not write report: " + ex.Message); }
        EditorApplication.Exit(code);
    }
}
#endif
