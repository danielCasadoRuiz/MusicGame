using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in real catalog/preparation/next-selection check. Progress writes stay suspended.</summary>
[InitializeOnLoad]
public static class DancePreparationPlayCheck
{
    const string Active = "DancePreparationPlayCheck.Active";
    static bool started;
    static double deadline;
    static DancePreparationPlayCheck() { EditorApplication.update += Tick; }
    public static void RunTypography()
    {
        SessionState.SetBool("DancePreparationPlayCheck.Typography", true);
        Run();
    }
    public static void Run()
    {
        SessionState.SetBool("DancePreparationPlayCheck.CloseEditor", true);
        SessionState.SetBool(Active, true);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        bool typography = File.Exists("Logs/TypographyPlayCheck.request");
        string request = typography ? "Logs/TypographyPlayCheck.request" : "Logs/StaffPreparationPlayCheck.request";
        if (File.Exists(request) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
        {
            try { File.Delete(request); } catch (IOException) { return; }
            started = false;
            SessionState.SetBool("DancePreparationPlayCheck.CloseEditor", false);
            SessionState.SetBool("DancePreparationPlayCheck.Typography", typography);
            SessionState.SetBool(Active, true); EditorApplication.isPlaying = true;
        }
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        if (!started)
        {
            started = true; deadline = EditorApplication.timeSinceStartup + 240;
            Directory.CreateDirectory("../VisualReviews/DancePreparation");
            File.WriteAllText("../VisualReviews/DancePreparation/play-result.txt", "RUNNING: neutral staff and direct HUD handoff check");
            PlayerProgressStore.SuspendWrites = true;
            var go = new GameObject("Dance preparation play check"); Object.DontDestroyOnLoad(go);
            go.AddComponent<DancePreparationPlayCheckRunner>();
        }
        var gameView = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        EditorWindow.GetWindow(gameView).Focus();
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup > deadline) Finish(false, "Timed out waiting for real preparation");
    }
    public static void Finish(bool success, string report)
    {
        SessionState.SetBool(Active, false);
        Directory.CreateDirectory("../VisualReviews/DancePreparation");
        File.WriteAllText("../VisualReviews/DancePreparation/play-result.txt", (success ? "PASS\n" : "FAIL\n") + report);
        if (SessionState.GetBool("DancePreparationPlayCheck.Typography", false))
            File.WriteAllText("../VisualReviews/DancePreparation/typography-result.txt", (success ? "PASS\n" : "FAIL\n") + report);
        // Exit outside a coroutine's profiler scope, after the current player loop has unwound.
        PlayerProgressStore.SuspendWrites = false;
        EditorApplication.delayCall += () =>
        {
            if (SessionState.GetBool("DancePreparationPlayCheck.CloseEditor", true)) EditorApplication.Exit(success ? 0 : 1);
            else EditorApplication.isPlaying = false;
        };
    }
}

public class DancePreparationPlayCheckRunner : MonoBehaviour
{
    readonly List<string> checks = new();
    IEnumerator Start()
    {
        var routine = Run();
        while (true)
        {
            object next;
            try { if (!routine.MoveNext()) break; next = routine.Current; }
            catch (Exception e) { DancePreparationPlayCheck.Finish(false, string.Join("\n", checks) + "\n" + e); yield break; }
            yield return next;
        }
        DancePreparationPlayCheck.Finish(true, string.Join("\n", checks));
    }
    void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks.Add(message);
    }
    IEnumerator Run()
    {
        while (AppBootstrap.Context == null || Object.FindFirstObjectByType<AnalyzingScreenController>() == null) yield return null;
        AppBootstrap.Context.AppFlow.RequestState(GameFlowState.MainMenu);
        yield return SongCatalogService.EnsureReady();
        bool typography = SessionState.GetBool("DancePreparationPlayCheck.Typography", false);
        var expectedStyle = typography ? GameMusicStyle.Electronic : GameMusicStyle.Dance;
        var entry = SongCatalogService.Entries.FirstOrDefault(e => e.gameStyle == expectedStyle);
        Check(entry != null, "Published " + expectedStyle + " song available");
        bool selected = false;
        var service = gameObject.AddComponent<SongSelectionService>();
        yield return service.SelectSong(new CatalogSongSource(entry.songId, entry.displayName), null, result => selected = result);
        Check(selected, "Real catalog selection succeeded");
        var view = Object.FindFirstObjectByType<AnalyzingScreenView>(FindObjectsInactive.Include);
        AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongAnalysis);
        while (view.transform.Find("AnalysisContent/LoadingWave") == null) yield return null;
        var wave = view.transform.Find("AnalysisContent/LoadingWave").GetComponent<Image>();
        var sprites = new HashSet<int>();
        bool started = false;
        Action<GameStartedEvent> onStarted = _ => started = true;
        EventBus.Subscribe(onStarted);
        bool captured = false, neutralCaptured = false, hudCaptured = false, fitChecked = false;
        float styleSeenAt = -1;
        while (!started)
        {
            CheckVisible(view);
            if (wave.sprite != null) sprites.Add(wave.sprite.GetInstanceID());
            var presentation = view.GetComponent<AnalysisPresentation>();
            if (presentation.CurrentStyle == expectedStyle && styleSeenAt < 0) styleSeenAt = Time.unscaledTime;
            var movingStyle = view.transform.parent.Find("DetectedStyle")?.GetComponent<TMP_Text>();
            if (typography && movingStyle != null && movingStyle.textInfo.characterCount > 0 && styleSeenAt >= 0)
            {
                if (movingStyle.isTextOverflowing || movingStyle.textInfo.lineCount > 1 || movingStyle.text.Contains("…"))
                    throw new Exception("Electronic overflowed during the letter animation");
            }
            if (!neutralCaptured && presentation.CurrentStyle == GameMusicStyle.Unknown)
            {
                yield return new WaitForEndOfFrame(); Capture("01-Analysing"); neutralCaptured = true;
            }
            var border = view.transform.Find("DanceFrame")?.GetComponent<RawImage>();
            if (!captured && presentation.CurrentStyle == expectedStyle && Time.unscaledTime - styleSeenAt > 1.05f)
            {
                yield return new WaitForEndOfFrame();
                Capture("02-Dance-reveal"); captured = true;
                if (typography)
                {
                    var caption = DetectedStyleLabel.Caption(expectedStyle);
                    AssertFits(movingStyle, caption);
                    foreach (GameMusicStyle sampleStyle in Enum.GetValues(typeof(GameMusicStyle)))
                    {
                        movingStyle.text = DetectedStyleLabel.Caption(sampleStyle);
                        movingStyle.ForceMeshUpdate(true, true); AssertFits(movingStyle, movingStyle.text);
                    }
                    movingStyle.text = caption; movingStyle.ForceMeshUpdate(true, true);
                    var title = view.transform.Find("AnalysisContent/Metadata/Song").GetComponent<TMP_Text>();
                    string original = title.text;
                    foreach (string sample in new[] { "Combo Master Tribe", "All Together Under The Northern Lights Tonight", "A Very Long Song Title With Many Words And An Extended Live Recording Version From The Concert Hall", new string('W', 100) })
                    {
                        title.text = sample; title.ForceMeshUpdate(true, true);
                        AssertFits(title, sample);
                    }
                    title.text = "Combo Master Tribe"; title.ForceMeshUpdate(true, true);
                    yield return new WaitForEndOfFrame(); Capture("Typography-Electronic");
                    title.text = "All Together Under The Northern Lights Tonight"; title.ForceMeshUpdate(true, true);
                    yield return new WaitForEndOfFrame(); Capture("Typography-Long-title");
                    title.text = original; fitChecked = true;
                }
            }
            if (!hudCaptured && presentation.HandoffProgress > .9f && DetectedStyleLabel.IsDocked)
            {
                Check(!Object.FindFirstObjectByType<GameplayManager>().IsRunning, "HUD settles before the run/countdown begins");
                yield return new WaitForEndOfFrame(); Capture("03-Hud-ready"); hudCaptured = true;
            }
            yield return null;
        }
        EventBus.Unsubscribe(onStarted);
        Check(captured, expectedStyle + " revealed during actual preparation");
        if (typography) Check(fitChecked, "All 18 style captions and four title lengths fit on one line, including a 100-character unbroken title");
        Check(neutralCaptured, "Neutral analysis appeared before Dance identification");
        Check(hudCaptured, "Direct central reveal to HUD handoff rendered before gameplay");
        Check(sprites.Count == AnalysisPresentation.WaveFrames, "All 32 grayscale atlas frames played during preparation");
        yield return new WaitForEndOfFrame(); Capture("04-Game-started");
        if (CameraFollow.Instance.ViewMode == CameraViewMode.ThirdPerson)
        {
            Vector3 delta = CameraFollow.Instance.transform.position - Object.FindFirstObjectByType<PlayerController>().transform.position;
            delta.y = 0;
            Check(delta.magnitude > 1f, "First countdown frame has a trailing camera, not an overhead jump");
        }
        var styleLabel = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).First(t => t.name == "DetectedStyle");
        if (typography) Check(styleLabel.text == DetectedStyleLabel.Caption(expectedStyle) && !styleLabel.isTextOverflowing && styleLabel.textInfo.lineCount == 1,
            "Electronic also fits completely in its final HUD slot");
        if (typography) AssertFits(styleLabel, DetectedStyleLabel.Caption(expectedStyle));
        Check(!styleLabel.enableVertexGradient && Mathf.Abs(styleLabel.color.r - .953f) < .01f, "Docked style is high-contrast ivory, not a purple gradient");
        Check(!view.gameObject.activeSelf, "GameStarted hides preparation only when Runner is ready");
        Check(Object.FindObjectsByType<NextSongTransitionView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0, "No separate Next Song screen instantiated");
        if (typography)
        {
            // This probe selected an arbitrary Electronic song, without winning/advancing the
            // campaign. Its next-song route may legitimately select that same identity.
            Check(PlayerProgressStore.SuspendWrites, "Progress writes stayed suspended throughout the typography check");
            yield break;
        }
        string previous = GameSession.Instance.SelectedSong.Value.SongId;
        Check(!CampaignService.LapFinished, "Campaign has a next-song route");
        NextSongTransitionController.Instance.BeginWin();
        Check(view.gameObject.activeSelf, "Next Song immediately opens the same preparation view");
        Check(view.transform.Find("AnalysisContent/Metadata/Song").GetComponent<TMP_Text>().text == "", "Pending selection does not show the previous song title");
        while (AppBootstrap.Context.AppFlow.CurrentState != GameFlowState.SongAnalysis) { CheckVisible(view); yield return null; }
        Check(GameSession.Instance.SelectedSong.Value.SongId != previous, "Next Song selected a new catalog identity");
        CheckVisible(view);
        Check(Object.FindObjectsByType<AnalyzingScreenView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1, "Selection and analysis reuse one view instance");
        // Let the real scene replacement finish before stopping Play Mode. Stopping during
        // UnloadUnusedAssets would resume its loader during teardown and log a spurious load error.
        while (AppBootstrap.Context.SceneFlow.IsModeTransitionInProgress || AppBootstrap.Context.SceneFlow.CurrentMode != GameMode.Frontend)
        { CheckVisible(view); yield return null; }
        Check(true, "Next-song scene replacement completed before test teardown");
        Check(PlayerProgressStore.SuspendWrites, "Progress writes stayed suspended throughout the check");
    }
    static void CheckVisible(AnalyzingScreenView view)
    {
        if (!view.gameObject.activeInHierarchy) throw new Exception("Preparation disappeared before readiness");
        if (view.titleText.gameObject.activeSelf) throw new Exception("Old large analysis heading is still visible");
        foreach (var text in view.GetComponentsInChildren<TMP_Text>())
            if (text.text.Contains("%")) throw new Exception("Percentage displayed on the redesigned screen");
    }
    static void Capture(string name)
    {
        if (SessionState.GetBool("DancePreparationPlayCheck.Typography", false) && !name.StartsWith("Typography"))
            name = "Typography-" + name.Replace("Dance", "Electronic");
        var png = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes("../VisualReviews/DancePreparation/" + name + ".png", png.EncodeToPNG());
        Object.Destroy(png);
    }
    static void AssertFits(TMP_Text text, string expected)
    {
        if (text.isTextOverflowing || text.textInfo.lineCount != 1 || text.textInfo.characterCount != expected.Length ||
            text.GetRenderedValues(false).x > text.rectTransform.rect.width + 8)
            throw new Exception($"Text did not fit: {expected} (rendered {text.GetRenderedValues(false).x:F1}, box {text.rectTransform.rect.width:F1}, size {text.fontSize:F1})");
    }
}
