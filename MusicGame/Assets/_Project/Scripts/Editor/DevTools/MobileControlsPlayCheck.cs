using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in real Core → catalog → Runner visibility and pointer-routing check.</summary>
[InitializeOnLoad]
public static class MobileControlsPlayCheck
{
    const string Active = "MobileControlsPlayCheck.Active";
    static bool started;
    static double deadline;
    static MobileControlsPlayCheck() { EditorApplication.update += Tick; }
    static void Tick()
    {
        const string request = "Logs/MobileControlsPlayCheck.request";
        if (File.Exists(request) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
        {
            try { File.Delete(request); } catch (IOException) { return; }
            SessionState.SetBool(Active, true); started = false; EditorApplication.isPlaying = true;
        }
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        if (!started)
        {
            started = true; deadline = EditorApplication.timeSinceStartup + 180;
            PlayerProgressStore.SuspendWrites = true;
            var go = new GameObject("Mobile controls real-play check"); Object.DontDestroyOnLoad(go);
            go.AddComponent<MobileControlsPlayCheckRunner>();
        }
        EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView")).Repaint();
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup > deadline) Finish(false, "Timed out reaching the real runner");
    }
    public static void Finish(bool passed, string report)
    {
        SessionState.SetBool(Active, false); Directory.CreateDirectory("../VisualReviews/MobileControlsRuntime");
        File.WriteAllText("../VisualReviews/MobileControlsRuntime/result.txt", (passed ? "PASS\n" : "FAIL\n") + report);
        PlayerProgressStore.SuspendWrites = false;
        EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
    }
}

public class MobileControlsPlayCheckRunner : MonoBehaviour
{
    readonly StringBuilder report = new();
    bool priorBackground;
    IEnumerator Start()
    {
        priorBackground = Application.runInBackground; Application.runInBackground = true;
        var routine = Run();
        while (true)
        {
            object next;
            try { if (!routine.MoveNext()) break; next = routine.Current; }
            catch (Exception e) { MobileControlsPlayCheck.Finish(false, report + "\n" + e); yield break; }
            yield return next;
        }
        MobileControlsPlayCheck.Finish(true, report.ToString());
    }
    void OnDestroy() { Application.runInBackground = priorBackground; }
    void Check(bool value, string message) { report.AppendLine((value ? "PASS: " : "FAIL: ") + message); if (!value) throw new Exception(message); }
    IEnumerator Run()
    {
        // Let the real intro finish: forcing MainMenu earlier races its delayed transition.
        while (AppBootstrap.Context == null || AppBootstrap.Context.AppFlow.CurrentState != GameFlowState.MainMenu) yield return null;
        yield return null;
        Check(PlatformService.IsMobile, "Editor is configured for mobile controls");
        Check(PlatformService.DualInputInEditor && UnityEngine.InputSystem.Keyboard.current != null, "Keyboard remains available alongside touch controls in the editor");
        yield return SongCatalogService.EnsureReady();
        var song = SongCatalogService.Entries.FirstOrDefault(e => e.gameStyle == GameMusicStyle.Dance);
        Check(song != null, "A real published Dance song is available");
        var service = gameObject.AddComponent<SongSelectionService>(); bool selected = false;
        yield return service.SelectSong(new CatalogSongSource(song.songId, song.displayName), null, ok => selected = ok);
        Check(selected, "Published song selected");
        bool running = false; Action<GameStartedEvent> onStarted = _ => running = true;
        EventBus.Subscribe(onStarted);
        AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongAnalysis);
        while (!running) yield return null;
        EventBus.Unsubscribe(onStarted);
        yield return null; yield return new WaitForEndOfFrame();
        var view = Object.FindFirstObjectByType<MobileControlsView>(FindObjectsInactive.Include);
        var controller = Object.FindFirstObjectByType<MobileControlsController>(FindObjectsInactive.Include);
        Check(view != null && controller != null, "Actual mobile controls and controller exist");
        report.AppendLine($"flow={AppBootstrap.Context.AppFlow.CurrentState}; activeSelf={view.gameObject.activeSelf}; activeHierarchy={view.gameObject.activeInHierarchy}; controllerEnabled={controller.enabled}; finished={typeof(MobileControlsController).GetField("_songFinished", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller)}");
        foreach (var image in view.GetComponentsInChildren<Image>(true))
            report.AppendLine($"{image.name}: enabled={image.enabled}, sprite={image.sprite?.name}, alpha={image.color.a:F2}, culled={image.canvasRenderer.cull}");
        Directory.CreateDirectory("../VisualReviews/MobileControlsRuntime");
        var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes("../VisualReviews/MobileControlsRuntime/Runner.png", screenshot.EncodeToPNG()); Object.Destroy(screenshot);
        Check(view.gameObject.activeInHierarchy, "Controls are shown in the real runner");
        var skin = view.GetComponent<MobileControlSkin>();
        Check(skin != null && view.joystickBackground.GetComponent<Image>().sprite != null, "Touch artwork initialized by its real lifecycle");
        CheckPointer(view.jumpButton.gameObject, view.jumpButton.GetComponent<RectTransform>());
        CheckPointer(view.crouchButton.gameObject, view.crouchButton.GetComponent<RectTransform>());
        CheckPointer(view.joystick.gameObject, view.joystickBackground);
        var data = new PointerEventData(EventSystem.current);
        bool jump = TouchInputState.JumpRequested, crouch = TouchInputState.CrouchHeld;
        float lateral = TouchInputState.Lateral; bool surge = TouchInputState.Surging, back = TouchInputState.HoldingBack;
        try
        {
            view.jumpButton.OnPointerDown(data); Check(TouchInputState.JumpRequested, "Jump press reaches touch input");
            view.crouchButton.OnPointerDown(data); Check(TouchInputState.CrouchHeld, "Crouch hold reaches touch input");
            view.crouchButton.OnPointerUp(data); Check(!TouchInputState.CrouchHeld, "Crouch releases");
            data.position = RectTransformUtility.WorldToScreenPoint(null, view.joystickBackground.TransformPoint(new Vector3(view.joystickBackground.rect.width * .25f, 0, 0)));
            view.joystick.OnDrag(data); Check(TouchInputState.Lateral > .1f, "Joystick drag reaches lateral input");
            view.joystick.OnPointerUp(data); Check(TouchInputState.Lateral == 0, "Joystick releases");
        }
        finally { TouchInputState.JumpRequested = jump; TouchInputState.CrouchHeld = crouch; TouchInputState.Lateral = lateral; TouchInputState.Surging = surge; TouchInputState.HoldingBack = back; }

        // Restart late in the FIRST countdown, then repeatedly in replacement countdowns.
        // Cross the cancelled deadline each time: no stale audio handoff or TEMPO may fire.
        var clock = MusicClock.Instance;
        var pauseView = Object.FindFirstObjectByType<PauseView>(FindObjectsInactive.Include);
        var countdown = Object.FindFirstObjectByType<CountdownScreenView>(FindObjectsInactive.Include);
        var manager = Object.FindFirstObjectByType<GameplayManager>();
        Check(clock.WarmupTime >= 2f, "Regression scenario has a multi-tick countdown");
        int tempos = 0;
        var audio = GameAudio.Instance;
        Action<AudioClip> announced = clip => { if (clip == audio.Announcer.tempo) tempos++; };
        GameAudio.AnnouncerPlayed += announced;
        try
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                yield return new WaitForSeconds(clock.WarmupTime - .6f);
                Check(!clock.HasPlaybackStarted, "Countdown still pending before pause/restart " + attempt);
                PauseController.Instance.Pause();
                pauseView.restartButton.onClick.Invoke();
                Check(!PauseController.Instance.IsPaused && Time.timeScale == 1f, "Restart unpauses attempt " + attempt);
                Check(countdown.numberText.text == Mathf.RoundToInt(clock.WarmupTime).ToString(), "Restart resets the countdown on attempt " + attempt);
                int before = tempos;
                yield return new WaitForSeconds(.85f);
                Check(!clock.HasPlaybackStarted, "Cancelled playback deadline cannot start music on attempt " + attempt);
                Check(tempos == before, "No old TEMPO overlaps replacement countdown " + attempt);
                // The following iteration waits only the remaining time to the last tick.
                if (attempt < 3) yield return RestartEarly(pauseView);
            }
            yield return new WaitForSeconds(clock.WarmupTime);
            Check(clock.HasPlaybackStarted && !FallRespawnSystem.Instance.IsProcessing, "Latest countdown completes and releases restart state");
            Check(tempos == 1, "Only the final countdown announces TEMPO");
        }
        finally { GameAudio.AnnouncerPlayed -= announced; }

        // Deliver the normal finish notifications to the real UI subscribers, then use
        // its actual summary button. No fake scene/state transition masks the stale flag.
        EventBus.Publish(new SongFinishedEvent());
        EventBus.Publish(new GameEndedEvent { Stats = new CollectionStats() });
        var endView = Object.FindFirstObjectByType<EndScreenView>(FindObjectsInactive.Include);
        Check(endView.gameObject.activeInHierarchy, "Finish notifications open the real runner summary");
        Check(!view.gameObject.activeInHierarchy, "Mobile controls hide at song finish");
        endView.restartButton.onClick.Invoke();
        yield return null;
        Check(view.gameObject.activeInHierarchy, "Summary restart restores mobile controls");
        Check(!endView.gameObject.activeInHierarchy && !PauseController.Instance.IsPaused, "Summary restart closes results and unpauses");
        PauseController.Instance.Pause(); pauseView.restartButton.onClick.Invoke();
        Check(!PauseController.Instance.IsPaused && view.gameObject.activeInHierarchy, "Pause restart also works during the post-summary countdown");
        yield return new WaitForSeconds(clock.WarmupTime + .15f);
        Check(clock.HasPlaybackStarted && !FallRespawnSystem.Instance.IsProcessing, "Post-summary restart completes normally");
    }
    IEnumerator RestartEarly(PauseView pauseView)
    {
        PauseController.Instance.Pause();
        pauseView.restartButton.onClick.Invoke();
        Check(!PauseController.Instance.IsPaused && !MusicClock.Instance.HasPlaybackStarted, "Early repeated restart is accepted");
        yield break;
    }
    void CheckPointer(GameObject expected, RectTransform rect)
    {
        var data = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
        var routed = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject) : null;
        Check(routed == expected, expected.name + " is visible to the actual UI pointer raycast (top hit " + (hits.Count > 0 ? hits[0].gameObject.name : "none") + ")");
    }
}
