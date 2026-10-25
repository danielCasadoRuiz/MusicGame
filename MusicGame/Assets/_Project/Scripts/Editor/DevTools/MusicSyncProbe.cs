#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play Mode probe of the music sync layer against a REAL AudioSource (phase P1): warmup cap / load
/// wait, warmup → playback handoff, pause, fall-hold + respawn seek, unexplained stop (stall +
/// auto-resync), an unannounced external seek (live analyzer reset), restart with a mid-song start,
/// and the clip's natural end → farewell. Builds its own throwaway scene (a synthetic 8 s click
/// clip, MusicClock, AudioSampler + AudioAnalyzer with a counting detector) — it never loads the
/// game's scenes or AppBootstrap, so it does not touch the player save.
///
/// Batch (with graphics or -nographics):
///   -executeMethod MusicSyncProbe.RunFromCommandLine [-syncProbeOut "C:/path/report.txt"]
/// Exit code 0 = every check passed, 1 = a check failed, 2 = timeout/exception.
/// </summary>
[InitializeOnLoad]
public static class MusicSyncProbe
{
    private const string KeyActive = "MusicSyncProbe.Active";
    private const string KeyOut    = "MusicSyncProbe.Out";

    private const float Warmup   = 1f;
    private const float Ups      = 10f;
    private const int   Freq     = 48000;
    private const float ClipLen  = 8f;

    private static readonly StringBuilder s_log = new();
    private static int    s_pass, s_fail;
    private static int    s_step = -1;
    private static double s_stepStart;
    private static double s_deadline;

    // Scene objects (created in play mode).
    private static AudioSource     s_src;
    private static MusicClock      s_clock;
    private static AudioAnalyzer   s_analyzer;
    private static CountingDetector s_det;
    private static readonly List<MusicTimeDiscontinuityEvent> s_disc = new();
    private static Action<MusicTimeDiscontinuityEvent> s_onDisc;

    // Values carried between steps.
    private static float s_a, s_b;
    private static int   s_resetsBefore;
    private static float  s_maxStep, s_maxDeviation, s_prevSong;
    private static double s_prevReal;
    private static bool  s_trackSteps;

    private class CountingDetector : IAudioDetector
    {
        public int Resets, Analyses;
        public bool IsEnabled => true;
        public void Initialize(AudioAnalysisConfig config) { }
        public void SetSongProfile(SongProfile profile) { }
        public void Analyze(AudioData data) => Analyses++;
        public void Reset() => Resets++;
    }

    static MusicSyncProbe()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        EditorApplication.update += Tick;
    }

    public static void RunFromCommandLine()
    {
        string outPath = Path.GetFullPath("Logs/MusicSyncProbe.txt");
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-syncProbeOut") outPath = args[i + 1];
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, outPath);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    // ── Steps: (seconds to wait BEFORE running, name, action) ────────────────────────────────
    private static readonly (float wait, string name, Action act)[] Steps =
    {
        (0.5f, "setup", Setup),
        (0f,   "warmup start", () =>
        {
            s_clock.Initialize(s_src, Warmup, Ups, 0f);
            Check("Initialize → Warmup, SongTime 0", s_clock.State == MusicClockState.Warmup && s_clock.SongTime == 0f, $"state={s_clock.State} t={s_clock.SongTime:F3}");
            CheckLastDisc(MusicTimeDiscontinuityReason.NewRun);
            StartStepTracking();
        }),
        (1.6f, "load wait after warmup", () =>
        {
            // 1 s of warmup + 0.6 s "clip still loading": capped at the handoff, no overrun.
            Check("warmup capped at warmup+start during load wait", Mathf.Abs(s_clock.SongTime - Warmup) < 1e-4f && s_clock.State == MusicClockState.Warmup,
                  $"t={s_clock.SongTime:F4} state={s_clock.State}");
            s_a = s_clock.SongTime;
            s_clock.StartPlayback(0f, MusicTimeDiscontinuityReason.PlaybackStarted);
            Check("handoff without snap-back", s_clock.State == MusicClockState.Playing && s_clock.SongTime >= s_a - 1e-4f && s_clock.SongTime - s_a < 0.05f,
                  $"before={s_a:F4} after={s_clock.SongTime:F4}");
            CheckLastDisc(MusicTimeDiscontinuityReason.PlaybackStarted);
            Check("AudioSource playing after StartPlayback", s_src.isPlaying);
        }),
        (1.0f, "playing tracks audio", () =>
        {
            float audioClip = (float)MusicTime.SamplesToSeconds(s_src.timeSamples, Freq);
            Check("audio advanced (~1 s)", audioClip > 0.7f && audioClip < 1.4f, $"clip={audioClip:F3}");
            Check("ClipTime tracks AudioSource.timeSamples (<60 ms)", Mathf.Abs(s_clock.ClipTime - audioClip) < 0.06f,
                  $"clock.ClipTime={s_clock.ClipTime:F4} audio={audioClip:F4}");
            Check("live analyzer ran on clip time", s_det.Analyses > 0, $"analyses={s_det.Analyses}");
            StopStepTracking("warmup→playing", 0.1f);
            // Pause (PauseController's sequence minus timeScale).
            s_src.Pause();
            s_clock.Pause();
            s_a = s_clock.SongTime;
        }),
        (1.0f, "pause", () =>
        {
            Check("paused: SongTime frozen", s_clock.SongTime == s_a && s_clock.State == MusicClockState.Paused, $"t={s_clock.SongTime:F4} vs {s_a:F4}");
            s_src.UnPause();
            s_clock.Resume();
            StartStepTracking();
        }),
        (0.4f, "resume", () =>
        {
            float adv = s_clock.SongTime - s_a;
            Check("resume continues from the paused moment (no 1 s jump)", adv > 0.2f && adv < 0.6f, $"advanced {adv:F3} s in 0.4 s");
            StopStepTracking("resume", 0.1f);
            // Fall: FallRespawnSystem's sequence (Hold before Pause).
            s_clock.Hold();
            s_src.Pause();
            s_a = s_clock.SongTime;
            s_b = s_clock.ClipTime;
            s_resetsBefore = s_det.Resets;
        }),
        (1.0f, "fall hold", () =>
        {
            Check("held: no wall-clock advance while the audio is stopped", s_clock.SongTime == s_a && s_clock.State == MusicClockState.Held,
                  $"t={s_clock.SongTime:F4} vs {s_a:F4} state={s_clock.State}");
            float target = Mathf.Max(0f, s_b - 0.5f);
            s_clock.StartPlayback(target, MusicTimeDiscontinuityReason.Seek);
            float audioClip = (float)MusicTime.SamplesToSeconds(s_src.timeSamples, Freq);
            Check("respawn seek: clock at warmup + seek", Mathf.Abs(s_clock.SongTime - (Warmup + target)) < 0.02f && Mathf.Abs(audioClip - target) < 0.02f,
                  $"t={s_clock.SongTime:F4} expected={Warmup + target:F4} audio={audioClip:F4}");
            CheckLastDisc(MusicTimeDiscontinuityReason.Seek);
            Check("live detectors reset on the seek", s_det.Resets > s_resetsBefore, $"resets {s_resetsBefore}→{s_det.Resets}");
        }),
        (0.5f, "unexplained stop", () =>
        {
            s_src.Pause(); // nobody tells the clock
            s_a = s_clock.SongTime;
        }),
        (0.5f, "stalled", () =>
        {
            Check("unexplained stop → Stalled, frozen, NOT natural end", s_clock.State == MusicClockState.Stalled && !s_clock.NaturalEndDetected && Mathf.Abs(s_clock.SongTime - s_a) < 0.05f,
                  $"state={s_clock.State} t={s_clock.SongTime:F4} vs {s_a:F4}");
            Check("ReachedPlaybackEnd false while stalled", !s_clock.ReachedPlaybackEnd(ClipLen));
            s_disc.Clear();
            s_src.UnPause();
        }),
        (0.3f, "auto resync", () =>
        {
            Check("source playing again → Playing + Resync event", s_clock.State == MusicClockState.Playing && s_disc.Exists(d => d.Reason == MusicTimeDiscontinuityReason.Resync),
                  $"state={s_clock.State} events={s_disc.Count}");
            // Unannounced external seek (+2 s) while playing: the analyzer must catch it itself.
            s_resetsBefore = s_det.Resets;
            s_src.timeSamples = Mathf.Min(s_src.timeSamples + 2 * Freq, (int)(ClipLen * Freq) - Freq);
        }),
        (0.3f, "external seek", () =>
        {
            Check("analyzer reset on an unannounced clip-time jump", s_det.Resets > s_resetsBefore, $"resets {s_resetsBefore}→{s_det.Resets}");
            float audioClip = (float)MusicTime.SamplesToSeconds(s_src.timeSamples, Freq);
            Check("clock snapped to the external seek", Mathf.Abs(s_clock.ClipTime - audioClip) < 0.06f, $"clock={s_clock.ClipTime:F3} audio={audioClip:F3}");
            // Restart with a mid-song start (play range starting at 5 s).
            s_src.Stop();
            s_clock.Initialize(s_src, Warmup, Ups, 5f);
            Check("restart mid-song: clock starts at the offset", s_clock.SongTime == 5f && s_clock.State == MusicClockState.Warmup && !s_clock.HasPlaybackStarted,
                  $"t={s_clock.SongTime:F3} state={s_clock.State}");
            CheckLastDisc(MusicTimeDiscontinuityReason.NewRun);
            StartStepTracking();
        }),
        (1.2f, "mid-song handoff", () =>
        {
            Check("mid-song warmup capped at warmup + start", Mathf.Abs(s_clock.SongTime - (Warmup + 5f)) < 1e-4f, $"t={s_clock.SongTime:F4}");
            s_clock.StartPlayback(5f, MusicTimeDiscontinuityReason.PlaybackStarted);
            Check("mid-song handoff without jump", Mathf.Abs(s_clock.SongTime - (Warmup + 5f)) < 0.03f, $"t={s_clock.SongTime:F4}");
        }),
        (0.5f, "mid-song playing", () =>
        {
            StopStepTracking("restart/mid-start", 0.1f);
            s_deadline = EditorApplication.timeSinceStartup + 6.0;
        }),
        (-1f, "natural end", null), // waits for Ended (see Tick)
        (0f, "farewell", () =>
        {
            Check("natural end detected with evidence", s_clock.State == MusicClockState.Ended && s_clock.NaturalEndDetected && !s_src.isPlaying,
                  $"state={s_clock.State} natural={s_clock.NaturalEndDetected} playing={s_src.isPlaying}");
            Check("SongTime at the clip end", Mathf.Abs(s_clock.SongTime - (Warmup + ClipLen)) < 0.15f, $"t={s_clock.SongTime:F4} expected {Warmup + ClipLen:F2}");
            Check("ReachedPlaybackEnd(clip length) true", s_clock.ReachedPlaybackEnd(ClipLen));
            s_a = s_clock.SongTime;
            s_clock.BeginManualAdvance();
        }),
        (0.5f, "farewell advance", () =>
        {
            float adv = s_clock.SongTime - s_a;
            Check("farewell advances by game time from the end", s_clock.State == MusicClockState.Farewell && adv > 0.3f && adv < 0.8f, $"advanced {adv:F3}");
        }),
    };

    private static void Setup()
    {
        var go  = new GameObject("MusicSyncProbe");
        s_src   = go.AddComponent<AudioSource>();
        s_src.playOnAwake = false;
        s_src.volume      = 0.2f;
        var data = new float[(int)(ClipLen * Freq)];
        for (int beat = 0; beat < ClipLen * 2; beat++) // a 20 ms 1 kHz click every 0.5 s
        {
            int s0 = beat * Freq / 2;
            for (int i = 0; i < Freq / 50 && s0 + i < data.Length; i++)
                data[s0 + i] = 0.8f * Mathf.Sin(2f * Mathf.PI * 1000f * i / Freq) * (1f - i / (Freq / 50f));
        }
        var clip = AudioClip.Create("SyncProbeClicks", data.Length, 1, Freq, false);
        clip.SetData(data, 0);
        s_src.clip = clip;

        s_clock = go.AddComponent<MusicClock>();

        var cfg     = ScriptableObject.CreateInstance<AudioAnalysisConfig>();
        var sampler = go.AddComponent<AudioSampler>();
        s_analyzer  = go.AddComponent<AudioAnalyzer>();
        s_det       = new CountingDetector();
        sampler.Initialize(s_src, cfg);
        s_analyzer.Initialize(sampler, cfg, new IAudioDetector[] { s_det });

        s_onDisc = e => s_disc.Add(e);
        EventBus.Subscribe(s_onDisc);
        Log($"setup: clip {ClipLen}s @{Freq}Hz, output {AudioSettings.outputSampleRate}Hz, dsp buffer {GetDsp()}, batch={Application.isBatchMode}");
    }

    private static string GetDsp()
    {
        AudioSettings.GetDSPBufferSize(out int len, out int num);
        return $"{len}x{num}";
    }

    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_deadline = now + 120; }

            if (s_trackSteps && s_clock != null)
            {
                // A jump = SongTime moving differently from GAME time (Time.time — what warmup and the
                // filter advance by) between two editor updates; a slow frame alone is not a jump.
                float songStep = s_clock.SongTime - s_prevSong;
                s_maxStep      = Mathf.Max(s_maxStep, Mathf.Abs(songStep));
                s_maxDeviation = Mathf.Max(s_maxDeviation, Mathf.Abs(songStep - (float)(Time.time - s_prevReal)));
                s_prevSong     = s_clock.SongTime;
                s_prevReal     = Time.time;
            }

            if (s_step >= Steps.Length) { Finish(s_fail == 0 ? 0 : 1); return; }
            var step = Steps[s_step];

            if (step.wait < 0f)
            {
                // Wait for the clip's natural end.
                if (s_clock.State == MusicClockState.Ended) Advance(now);
                else if (now > s_deadline) { Check("natural end reached before timeout", false, $"state={s_clock.State} clip={s_clock.AudioClipTime:F3} playing={s_src.isPlaying}"); Finish(1); }
                return;
            }
            if (now - s_stepStart < step.wait) return;
            Log($"-- {step.name} (t={now:F2}, songTime={(s_clock != null ? s_clock.SongTime : 0f):F4})");
            step.act?.Invoke();
            Advance(now);
        }
        catch (Exception ex)
        {
            Log("EXCEPTION " + ex);
            Finish(2);
        }
    }

    private static void Advance(double now) { s_step++; s_stepStart = now; }

    private static void StartStepTracking()
    {
        s_trackSteps   = true;
        s_maxStep      = s_maxDeviation = 0f;
        s_prevSong     = s_clock.SongTime;
        s_prevReal     = Time.time;
    }

    // Holds/pauses inside a tracked span are excluded by construction (spans only cover running time).
    private static void StopStepTracking(string label, float limit)
    {
        s_trackSteps = false;
        Check($"{label}: |SongTime step − game-time step| < {limit * 1000f:F0} ms (no time jump)", s_maxDeviation < limit,
              $"max deviation {s_maxDeviation * 1000f:F1} ms, max raw step {s_maxStep * 1000f:F1} ms");
    }

    private static void CheckLastDisc(MusicTimeDiscontinuityReason reason)
    {
        bool ok = s_disc.Count > 0 && s_disc[s_disc.Count - 1].Reason == reason;
        Check($"MusicTimeDiscontinuityEvent {reason}", ok, s_disc.Count > 0 ? $"last={s_disc[s_disc.Count - 1].Reason}" : "none");
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    private static void Log(string msg)
    {
        s_log.AppendLine(msg);
        Debug.Log("[MusicSyncProbe] " + msg);
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        if (s_onDisc != null) EventBus.Unsubscribe(s_onDisc);
        Log($"RESULT: {s_pass} passed, {s_fail} failed (exit {code})");
        try
        {
            string outPath = SessionState.GetString(KeyOut, Path.GetFullPath("Logs/MusicSyncProbe.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, s_log.ToString());
        }
        catch (Exception ex) { Debug.LogError("[MusicSyncProbe] could not write report: " + ex.Message); }
        EditorApplication.Exit(code);
    }
}
#endif
