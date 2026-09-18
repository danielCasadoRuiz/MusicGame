using UnityEngine;

/// <summary>
/// Connects THIS scene's local components to the persistent, cross-scene services composed by
/// AppBootstrap — the scene-local counterpart to the Composition Root (see AppContext's own doc
/// on why new code should reach for a service through here/AppContext rather than another
/// `SomeService.Instance`).
///
/// Only one scene exists right now (no Frontend/Gameplay/Fight split yet), so this is deliberately
/// small and generic rather than named "GameplaySceneBootstrap" — it'll likely be renamed/split
/// once that scene split actually happens.
///
/// PHASE 2 responsibility: bridge GameFlowState to the existing, scene-local Song Analysis
/// pipeline (AudioSystemBootstrapper) — entering GameFlowState.SongAnalysis starts analysis
/// (a COMMAND: audioSystem.BeginAnalysis()), and SongProfileReadyEvent finishing analysis advances
/// the flow to GameFlowState.Gameplay (a NOTIFICATION driving a flow transition). GameplayManager
/// itself is untouched — it already only reacts to SongProfileReadyEvent, regardless of what
/// triggered the analysis that produced it.
///
/// PHASE 3 responsibility: the same SongProfileReadyEvent handler also runs IMusicStyleClassifier
/// (a direct call — classification is a cheap lookup, not something worth an event round-trip),
/// writes the result to GameSession, and announces it via MusicStyleDetectedEvent — BEFORE
/// advancing to Gameplay, so anything reacting to that event (the future Theme system) always sees
/// GameSession.DetectedMusicStyleId already set.
///
/// PHASE 10 responsibility: right before triggering analysis, re-apply GameSession.SelectedSong's
/// clip onto AudioSystemBootstrapper's AudioSource if Song Selection ever set one (see
/// SongSelectionService) — so a song picked earlier (possibly in a different scene, once a real
/// Frontend/Gameplay split exists) is what actually gets analyzed, not whatever clip happens to be
/// scene-authored. A no-op today (nothing calls SongSelectionService yet), so current behavior
/// (analyzing the scene-authored AudioClip) is unchanged.
///
/// PHASE 11 responsibility: GameplayHUD's end-screen Continue button already publishes
/// ContinuePressedEvent (a NOTIFICATION — "the player pressed Continue", not a command aimed at any
/// one system). This is where that notification is turned into a flow transition to
/// GameFlowState.Fight — GameplayHUD itself stays exactly as it was (it doesn't know Fight exists),
/// and FightController (scene-local, alongside this component) reacts to that state change.
/// </summary>
public class SceneBootstrap : MonoBehaviour
{
    [SerializeField] private AudioSystemBootstrapper audioSystem;

    private readonly IMusicStyleClassifier _musicStyleClassifier = new TagBasedMusicStyleClassifier();

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;
    private System.Action<SongProfileReadyEvent>      _onProfileReady;
    private System.Action<ContinuePressedEvent>       _onContinuePressed;

    private void OnEnable()
    {
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
