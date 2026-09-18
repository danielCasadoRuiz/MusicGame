using UnityEngine;

/// <summary>
/// Connects the Runner Mode Scene's local components to the persistent, cross-scene services
/// composed by AppBootstrap — the scene-local counterpart to the Composition Root (see AppContext's
/// own doc on why new code should reach for a service through here/AppContext rather than another
/// `SomeService.Instance`). Formerly "SceneBootstrap", back when only one scene existed in the whole
/// project — renamed once the multi-scene refactor actually split Runner out as its own Mode Scene
/// (see SceneFlowController), exactly as this class's own doc predicted it eventually would.
///
/// TRANSITIONAL: Song Analysis is triggered and MusicStyleId is resolved HERE, inside the Runner
/// scene — that's a temporary arrangement, not the target architecture (Section 13 of the
/// multi-scene refactor plan: Runner should only ever CONSUME an already-resolved SongProfile/
/// CurrentTheme, never trigger analysis itself). It stays here for now because there's no real
/// Frontend Mode Scene yet to own Song Selection/Song Analysis — once that scene exists, this
/// responsibility moves there, and this class shrinks to just reading GameSession/CurrentTheme
/// before Gameplay starts.
///
/// Calls SceneFlowController.NotifyCurrentModeAlreadyLoaded(Runner) on enable — Runner.unity can be
/// opened directly (Editor testing) rather than loaded via LoadMode, so without this
/// SceneFlowController's own bookkeeping wouldn't know Runner is "the current mode", and a later
/// LoadMode(Fight)/LoadMode(Frontend) call would either double-load Runner or fail to unload it.
/// Fight and Frontend now fully REPLACE Runner (not overlay it) — see SceneFlowController's own doc.
///
/// Bridges GameFlowState to the existing, scene-local Song Analysis pipeline
/// (AudioSystemBootstrapper) — entering GameFlowState.SongAnalysis starts analysis (a COMMAND:
/// audioSystem.BeginAnalysis()), and SongProfileReadyEvent finishing analysis resolves MusicStyleId
/// (a direct call — classification is a cheap lookup, not something worth an event round-trip),
/// writes it to GameSession, announces it via MusicStyleDetectedEvent, then advances the flow to
/// GameFlowState.Gameplay. GameplayManager itself is untouched — it already only reacts to
/// SongProfileReadyEvent, regardless of what triggered the analysis that produced it.
///
/// Also re-applies GameSession.SelectedSong's clip onto AudioSystemBootstrapper's AudioSource before
/// triggering analysis, if Song Selection ever set one (see SongSelectionService) — so a song picked
/// elsewhere is what actually gets analyzed, not whatever clip happens to be scene-authored. And
/// turns GameplayHUD's ContinuePressedEvent notification into a flow transition to
/// GameFlowState.Fight — GameplayHUD itself stays exactly as it was (it doesn't know Fight exists).
/// </summary>
public class RunnerSceneBootstrap : MonoBehaviour
{
    [SerializeField] private AudioSystemBootstrapper audioSystem;

    private readonly IMusicStyleClassifier _musicStyleClassifier = new TagBasedMusicStyleClassifier();

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;
    private System.Action<SongProfileReadyEvent>      _onProfileReady;
    private System.Action<ContinuePressedEvent>       _onContinuePressed;

    private void OnEnable()
    {
        AppBootstrap.Context?.SceneFlow.NotifyCurrentModeAlreadyLoaded(GameMode.Runner);

        _onFlowStateChanged = e =>
        {
            if (e.Current == GameFlowState.SongAnalysis && audioSystem != null)
                BeginAnalysis();
        };
        _onProfileReady = e =>
        {
            var style = _musicStyleClassifier.Classify(e.Profile);
            if (GameSession.Instance != null) GameSession.Instance.DetectedMusicStyleId = style;
            EventBus.Publish(new MusicStyleDetectedEvent { Style = style, Profile = e.Profile });

            AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.Gameplay);
        };
        _onContinuePressed = _ => AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.Fight);

        EventBus.Subscribe(_onFlowStateChanged);
        EventBus.Subscribe(_onProfileReady);
        EventBus.Subscribe(_onContinuePressed);

        // The flow may already be past Boot by the time this scene's OnEnable runs (AppFlowController
        // is persistent and could have advanced in a previous scene) — check the current state once
        // instead of only reacting to the next change, so analysis isn't missed on first load.
        if (AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.SongAnalysis && audioSystem != null)
            BeginAnalysis();
    }

    private void BeginAnalysis()
    {
        var selected = GameSession.Instance != null ? GameSession.Instance.SelectedSong : null;
        if (selected is { Clip: not null } info)
            audioSystem.AudioSource.clip = info.Clip;

        audioSystem.BeginAnalysis();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
        EventBus.Unsubscribe(_onProfileReady);
        EventBus.Unsubscribe(_onContinuePressed);
    }
}
