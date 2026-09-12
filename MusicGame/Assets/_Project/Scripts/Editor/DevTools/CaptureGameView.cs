#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Dev-only tool (Editor folder — never ships): enters Play mode on SampleScene in batchmode,
/// waits for the song/world to actually spin up, renders Camera.main to an offscreen
/// RenderTexture, and saves a PNG — so visual changes (e.g. FrequencyBackground) can be
/// screenshotted and compared without a human at the keyboard.
///
/// IMPORTANT: entering Play Mode triggers a domain reload, which wipes ordinary static fields
/// and event subscriptions made beforehand — a plain `EditorApplication.update +=` callback
/// registered in RunCapture() would silently vanish the moment Play Mode actually starts,
/// leaving nothing to ever fire the capture (this was diagnosed by observing the process hang
/// indefinitely right after Play Mode's own startup logs, with zero further activity). Fixed by
/// persisting the pending-capture state in SessionState (survives domain reload) and using
/// [InitializeOnLoad] to re-attach the update hook every time the domain (re)loads.
///
/// Invoke via: Unity.exe -batchmode -nographics -projectPath &lt;proj&gt;
///             -executeMethod CaptureGameView.RunCapture
///             -captureOutput "C:/path/out.png" -captureWaitSeconds 12
/// </summary>
[InitializeOnLoad]
public static class CaptureGameView
{
    private const string KeyActive = "CaptureGameView.Active";
    private const string KeyDeadline = "CaptureGameView.Deadline";
    private const string KeyOutput = "CaptureGameView.Output";

    private static int _lastHeartbeatSecond = -1;

    static CaptureGameView()
    {
        // Runs on EVERY domain reload, including the one triggered by entering Play Mode — this
        // is what makes the wait survive that reload instead of silently losing its callback.
        if (SessionState.GetBool(KeyActive, false))
        {
            Debug.Log("[CaptureGameView] Domain (re)loaded with a capture still pending — re-attaching.");
            EditorApplication.update += EditorTick;
        }
    }

    public static void RunCapture()
    {
        string outputPath = GetArg("-captureOutput", "capture.png");
        float waitSeconds = float.TryParse(GetArg("-captureWaitSeconds", "12"), out var v) ? v : 12f;

        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOutput, outputPath);
        SessionState.SetString(KeyDeadline, (EditorApplication.timeSinceStartup + waitSeconds).ToString());

        EditorApplication.update += EditorTick;

        EditorSceneManager.OpenScene("Assets/_Project/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
    }

    private static void EditorTick()
    {
        if (!SessionState.GetBool(KeyActive, false))
        {
            EditorApplication.update -= EditorTick;
            return;
        }

        double deadline = double.Parse(SessionState.GetString(KeyDeadline, "0"));
        double now = EditorApplication.timeSinceStartup;

        int wholeSecond = (int)now;
        if (wholeSecond != _lastHeartbeatSecond)
        {
            _lastHeartbeatSecond = wholeSecond;
            Debug.Log($"[CaptureGameView] heartbeat now={now:F1} deadline={deadline:F1} isPlaying={EditorApplication.isPlaying}");
        }

        if (now < deadline) return;

        EditorApplication.update -= EditorTick;
        SessionState.SetBool(KeyActive, false);

        string outputPath = SessionState.GetString(KeyOutput, "capture.png");
        Capture(outputPath);
        EditorApplication.Exit(0);
    }

    private static string GetArg(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return fallback;
    }

    private static void Capture(string path)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[CaptureGameView] Camera.main is null — cannot capture.");
            return;
        }

        int w = 1920, h = 1080;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;

        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();

        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;

        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Debug.Log($"[CaptureGameView] Saved screenshot to {path}");

        UnityEngine.Object.Destroy(tex);
        rt.Release();
        UnityEngine.Object.Destroy(rt);
    }
}
#endif
