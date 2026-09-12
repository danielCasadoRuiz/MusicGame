#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Dev-only tool (Editor folder — never ships): plays SampleScene in batchmode for real, waits
/// for the song to end NATURALLY (polls GameplayHUD's own _gameEnded field via reflection — see
/// note below on why NOT an EventBus subscription), then calls
/// GameplayManager.RequestRestartSong() — the EXACT same call GameplayHUD's end-screen "RESTART"
/// button makes — and logs, second by second, whether the game actually comes back to life
/// (IsRunning goes back to true, audio actually plays again, GameplayHUD._gameEnded actually
/// clears, MusicClock actually ticks again).
///
/// Invoke via: Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod RestartDiagnostic.Run
/// </summary>
[InitializeOnLoad]
public static class RestartDiagnostic
{
    private const string KeyActive   = "RestartDiagnostic.Active";
    private const string KeyPhase    = "RestartDiagnostic.Phase";
    private const string KeyDeadline = "RestartDiagnostic.Deadline";

    private static int _lastHeartbeatSecond = -1;

    static RestartDiagnostic()
    {
        // Survives the domain reload triggered by entering Play Mode — a plain
        // EditorApplication.update subscription made before Play starts would otherwise be
        // silently wiped by that reload (same reasoning as CaptureGameView.cs).
        if (SessionState.GetBool(KeyActive, false))
            EditorApplication.update += Tick;
    }

    public static void Run()
    {
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyPhase, "WaitEnd");
        SessionState.SetString(KeyDeadline, (EditorApplication.timeSinceStartup + 100).ToString());

        EditorApplication.update += Tick;

        EditorSceneManager.OpenScene("Assets/_Project/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
        // Deliberately NOT using EditorApplication.playModeStateChanged to set up an
        // EventBus.Subscribe here — that callback (and any subscription made from it) is
        // itself wiped by the SAME domain reload entering Play Mode triggers. Polling
        // GameplayHUD's own _gameEnded field via reflection sidesteps that entirely — it's
        // driven by a MonoBehaviour.OnEnable() subscription made fresh after the reload, so
        // it's reliable ground truth (confirmed correct in an earlier run of this tool).
    }

    private static bool ReadGameEndedFlag()
    {
        var hud = Object.FindFirstObjectByType<GameplayHUD>();
        if (hud == null) return false;
        var field = typeof(GameplayHUD).GetField("_gameEnded", BindingFlags.NonPublic | BindingFlags.Instance);
        return field != null && (bool)field.GetValue(hud);
    }

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        string phase = SessionState.GetString(KeyPhase, "WaitEnd");
        double deadline = double.Parse(SessionState.GetString(KeyDeadline, "0"));

        var mgr      = Object.FindFirstObjectByType<GameplayManager>();
        var clock    = MusicClock.Instance;
        var audioSrc = Object.FindFirstObjectByType<AudioSource>();
        bool hudEnded = ReadGameEndedFlag();

        int wholeSecond = (int)now;
        if (wholeSecond != _lastHeartbeatSecond)
        {
            _lastHeartbeatSecond = wholeSecond;
            Debug.Log($"[RestartDiag] t={now:F0} phase={phase} hud._gameEnded={hudEnded} " +
                      $"mgr.IsRunning={(mgr != null ? mgr.IsRunning.ToString() : "null")} " +
                      $"audio.isPlaying={(audioSrc != null ? audioSrc.isPlaying.ToString() : "null")} " +
                      $"clock.IsRunning={(clock != null ? clock.IsRunning.ToString() : "null")} " +
                      $"clock.SongTime={(clock != null ? clock.SongTime.ToString("F2") : "null")} " +
                      $"timeScale={Time.timeScale:F2}");
        }

        if (phase == "WaitEnd")
        {
            if (hudEnded)
            {
                Debug.Log("[RestartDiag] === Song ended. Clicking RESTART now: mgr.RequestRestartSong() " +
                          "(the EXACT call GameplayHUD's end-screen button makes) ===");
                mgr?.RequestRestartSong();
                SessionState.SetString(KeyPhase, "WaitAfterClick");
                SessionState.SetString(KeyDeadline, (now + 20).ToString());
                return;
            }
            if (now >= deadline)
            {
                Debug.LogError("[RestartDiag] TIMEOUT waiting for the song to end naturally.");
                Finish();
            }
            return;
        }

        if (phase == "WaitAfterClick")
        {
            if (now >= deadline)
            {
                bool success = !hudEnded && mgr != null && mgr.IsRunning
                               && audioSrc != null && audioSrc.isPlaying
                               && clock != null && clock.IsRunning;
                Debug.Log($"[RestartDiag] === RESULT === hud._gameEnded={hudEnded} " +
                          $"mgr.IsRunning={mgr?.IsRunning} audio.isPlaying={audioSrc?.isPlaying} " +
                          $"clock.IsRunning={clock?.IsRunning} clock.SongTime={clock?.SongTime:F2} " +
                          $"=> {(success ? "PASS" : "FAIL")}");
                Finish();
            }
            return;
        }
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        EditorApplication.Exit(0);
    }
}
#endif
