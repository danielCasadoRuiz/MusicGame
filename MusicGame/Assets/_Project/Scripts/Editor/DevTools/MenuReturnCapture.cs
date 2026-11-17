using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Opt-in capture of the real Continue -> Runner -> Pause -> Main Menu path.
[InitializeOnLoad]
public static class MenuReturnCapture
{
    static MenuReturnCapture() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Logs/MenuReturnCapture/run.request";
        if (!File.Exists(request) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        File.Delete(request);
        var go = new GameObject("Menu return diagnostic");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<MenuReturnCaptureRunner>();
    }
}

public class MenuReturnCaptureRunner : MonoBehaviour
{
    const string Folder = "Logs/MenuReturnCapture";
    bool previousSuspend;
    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);
        previousSuspend = PlayerProgressStore.SuspendWrites;
        PlayerProgressStore.SuspendWrites = true;
        while (AppBootstrap.Context == null || AppBootstrap.Context.AppFlow.CurrentState != GameFlowState.MainMenu) yield return null;
        yield return null;
        var menu = Object.FindFirstObjectByType<MainMenuView>(FindObjectsInactive.Include);
        if (AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.MainMenu) menu.playButton.onClick.Invoke();
        float deadline = Time.realtimeSinceStartup + 150;
        while (PauseController.Instance == null || Object.FindFirstObjectByType<GameplayManager>()?.IsRunning != true)
        {
            if (Time.realtimeSinceStartup > deadline) { File.WriteAllText(Folder + "/failure.txt", "Runner did not start"); Destroy(gameObject); yield break; }
            yield return null;
        }
        while (MusicClock.Instance == null || MusicClock.Instance.State != MusicClockState.Playing)
        {
            if (Time.realtimeSinceStartup > deadline) { File.WriteAllText(Folder + "/failure.txt", "Playback did not start"); Destroy(gameObject); yield break; }
            yield return null;
        }
        yield return new WaitForSecondsRealtime(2);
        PauseController.Instance.Pause();
        yield return new WaitForEndOfFrame();
        Record(0);
        Object.FindFirstObjectByType<PauseView>(FindObjectsInactive.Include).mainMenuButton.onClick.Invoke();
        for (int i = 1; i <= 65; i++) { yield return new WaitForEndOfFrame(); Record(i); }
        File.WriteAllText(Folder + "/done.txt", "Real Runner pause return captured");
        Destroy(gameObject);
    }
    void Record(int index)
    {
        var flow = AppBootstrap.Context.SceneFlow;
        var frame = Object.FindFirstObjectByType<SceneTransitionFrame>(FindObjectsInactive.Include);
        string state = $"{index} frame={Time.frameCount} mode={flow.CurrentMode} loading={flow.IsModeTransitionInProgress} cover={flow.IsTransitionFrameVisible}";
        if (frame != null)
        {
            foreach (var canvas in frame.GetComponentsInChildren<Canvas>(true)) state += $" canvas={canvas.enabled}/{canvas.gameObject.activeInHierarchy} rect={((RectTransform)canvas.transform).rect} scale={canvas.transform.lossyScale}";
            var texture = typeof(SceneTransitionFrame).GetField("_frame", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(frame) as RenderTexture;
            if (index < 5 && texture != null) Save(texture, $"frozen-{index:000}");
        }
        File.AppendAllText(Folder + "/frames.txt", state + "\n");
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes($"{Folder}/screen-{index:000}.jpg", image.EncodeToJPG(80));
        Destroy(image);
    }
    static void Save(RenderTexture source, string name)
    {
        var old = RenderTexture.active;
        RenderTexture.active = source;
        var image = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        image.Apply();
        RenderTexture.active = old;
        File.WriteAllBytes($"{Folder}/{name}.jpg", image.EncodeToJPG(80));
        Destroy(image);
    }
    void OnDestroy() { PlayerProgressStore.SuspendWrites = previousSuspend; }
}
