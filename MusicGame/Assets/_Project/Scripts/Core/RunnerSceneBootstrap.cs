using System.Collections;
using UnityEngine;

/// <summary>
/// Connects the Runner Mode Scene's local components to the persistent, cross-scene services
/// composed by AppBootstrap — the scene-local counterpart to the Composition Root (see AppContext's
/// own doc on why new code should reach for a service through here/AppContext rather than another
/// `SomeService.Instance`). Formerly "SceneBootstrap", back when only one scene existed in the whole
/// project — renamed once the multi-scene refactor actually split Runner out as its own Mode Scene
/// (see SceneFlowController), exactly as this class's own doc predicted it eventually would.
///
/// Song Analysis no longer happens here — SongAnalysisController (in the always-loaded UI Scene)
/// owns the whole Play → analyze → classify style → theme transition → advance-to-Gameplay pipeline
/// now (Section 13 of the multi-scene refactor plan's original target: "Runner should only ever
/// CONSUME an already-resolved SongProfile/CurrentTheme, never trigger analysis itself" — this class
/// finally shrinks to exactly that). By the time Runner loads, GameSession.Profile/SelectedSong/
/// DetectedMusicStyleId are already resolved; this class's only remaining jobs are:
///   1. Apply the already-selected clip onto the scene-local AudioSystemBootstrapper's AudioSource
///      (GameplayManager plays THAT same AudioSource — see AudioSystemBootstrapper's own doc).
///   2. Re-publish SongProfileReadyEvent once every scene-local object has had a chance to
///      subscribe (GameplayManager/MusicWorldManager/etc. all subscribe in their OWN OnEnable(),
///      which couldn't have existed yet when the event first fired — analysis finished before this
///      scene even loaded) — see RepublishProfileNextFrame's own doc.
///
/// Calls SceneFlowController.NotifyCurrentModeAlreadyLoaded(Runner) on enable — Runner.unity can be
/// opened directly (Editor testing) rather than loaded via LoadMode, so without this
/// SceneFlowController's own bookkeeping wouldn't know Runner is "the current mode", and a later
/// LoadMode(Fight)/LoadMode(Frontend) call would either double-load Runner or fail to unload it.
/// Fight and Frontend now fully REPLACE Runner (not overlay it) — see SceneFlowController's own doc.
///
/// Also turns GameplayHUD's ContinuePressedEvent notification into a flow transition to
/// GameFlowState.Fight — GameplayHUD itself stays exactly as it was (it doesn't know Fight exists).
/// </summary>
public class RunnerSceneBootstrap : MonoBehaviour
{
    [SerializeField] private AudioSystemBootstrapper audioSystem;

    private System.Action<ContinuePressedEvent> _onContinuePressed;

    private void OnEnable()
    {
        AppBootstrap.Context?.SceneFlow.NotifyCurrentModeAlreadyLoaded(GameMode.Runner);

        if (audioSystem != null)
        {
            var selected = GameSession.Instance != null ? GameSession.Instance.SelectedSong : null;
            if (selected is { Clip: not null } info)
                audioSystem.AudioSource.clip = info.Clip;
        }

        if (GameSession.Instance != null && GameSession.Instance.Profile != null)
            StartCoroutine(RepublishProfileNextFrame(GameSession.Instance.Profile));

        _onContinuePressed = _ => AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.Fight);
        EventBus.Subscribe(_onContinuePressed);
    }

    // GameplayManager/MusicWorldManager/CameraFollow/MusicEnvironmentController/etc. all subscribe
    // to SongProfileReadyEvent in their own OnEnable() — waiting one frame guarantees every object
    // Unity activated as part of THIS scene load has already run its OnEnable() (and therefore
    // subscribed) before this fires, so every existing listener keeps working completely unchanged,
    // with no per-class catch-up logic needed anywhere else.
    private IEnumerator RepublishProfileNextFrame(SongProfile profile)
    {
        yield return null;
        EventBus.Publish(new SongProfileReadyEvent { Profile = profile });
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onContinuePressed);
    }
}
