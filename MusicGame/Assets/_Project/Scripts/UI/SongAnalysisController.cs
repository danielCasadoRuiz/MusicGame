using System.Collections;
using UnityEngine;

/// <summary>
/// Owns the whole Play → analyze → classify style → theme transition → advance-to-Gameplay pipeline
/// for GameFlowState.SongAnalysis — the piece RunnerSceneBootstrap's own doc used to call out as
/// "temporary, not the target architecture" (Section 13 of the multi-scene refactor plan: "Runner
/// should only ever CONSUME an already-resolved SongProfile/CurrentTheme, never trigger analysis
/// itself"). Lives in the always-loaded UI Scene (added by UIFlowController, same as
/// AnalyzingScreenController) precisely so analysis can run WITHOUT the Runner Mode Scene loaded at
/// all — SceneFlowController.ModeFor(SongAnalysis) now maps to Frontend, not Runner, so the 3D
/// placeholder world never flashes into view behind the Analyzing screen while a song is still being
/// analyzed or its theme still transitioning.
///
/// Sequence once GameFlowState.SongAnalysis is entered:
///   0. Wait for GameSession.SelectedSong.Clip to actually be non-null — SongSelectionController
///      requests this state IMMEDIATELY on Play, before a catalog song's Addressables clip load
///      has necessarily finished, specifically so the Analyzing screen appears instantly rather
///      than only after that load completes (bounded by ClipWaitTimeoutSeconds, and abandoned if
///      the flow leaves SongAnalysis first — e.g. a failed load bounces back to Song Selection).
///   1. Run AudioPreAnalyzer directly on that clip (this needs no AudioSource and no scene-local
///      dependency at all — see AudioPreAnalyzer's own doc; it operates purely on the raw clip
///      data) — publishes PreAnalysisStartedEvent/PreAnalysisProgressEvent exactly as before,
///      which AnalyzingScreenController already reacts to regardless of who's driving it.
///   2. As soon as AudioPreAnalyzer's semantic-tagging pass knows this song's tags — which happens
///      BEFORE the rest of its (level-generation-critical) per-frame analysis even starts, see its
///      own doc — OnTagsReady classifies the style (same IMusicStyleClassifier RunnerSceneBootstrap
///      used to own), writes GameSession.DetectedMusicStyleId, and publishes
///      MusicStyleDetectedEvent right away. ThemeManager already subscribes to that on its own and
///      swaps CurrentTheme accordingly (Addressables-loading the style's visual, then Rebuild());
///      nothing new to wire there. The rest of the analysis keeps running in the background.
///      FALLBACK: if semantic tagging is disabled (OnTagsReady never fires), classification instead
///      happens once the full profile is ready, exactly as before this early path existed.
///   3. Wait roughly one theme-transition's worth of time so the Analyzing screen's own
///      ThemeColorReceiver/ThemeTextReceiver children actually finish animating to the new style
///      BEFORE Runner loads underneath them — NOT by waiting on ThemeChangedEvent itself, since
///      ThemeManager legitimately never publishes it when a style has no authored visual yet (falls
///      back to whatever's already showing), which would hang this coroutine forever.
///   4. Request GameFlowState.Gameplay — SceneFlowController maps that to Runner and loads it only
///      now, with RunnerSceneBootstrap re-publishing SongProfileReadyEvent once Runner's own
///      scene-local listeners (GameplayManager, MusicWorldManager, ...) have subscribed, so they
///      keep working completely unchanged despite the event having originally fired before their
///      scene even existed.
///
/// SongProfileReadyEvent is still published globally exactly as before (from AudioPreAnalyzer) — so
/// GameSession's own subscription (which fills GameSession.Profile) and anything else listening
/// globally keeps working unchanged; this class doesn't duplicate that, it just also reads the
/// resulting SongProfile to drive the sequence above.
/// </summary>
public class SongAnalysisController : MonoBehaviour
{
    // Local Addressables resolution + the theme transition itself are both normally well under a
    // second — this pads a bit beyond ThemeTransitionController.Duration so the transition has
    // genuinely finished (not just started) before Runner loads. Purely a UX beat, not a real
    // dependency — nothing breaks if a slow load overruns it, the theme just keeps transitioning
    // for a moment after Gameplay has already begun loading underneath.
    private const float PostDetectionBufferSeconds = 0.15f;

    private readonly IMusicStyleClassifier _musicStyleClassifier = new TagBasedMusicStyleClassifier();

    private AudioPreAnalyzer     _preAnalyzer;
    private AudioAnalysisConfig  _config;

    // Set the instant onTagsReady fires (see BeginAnalysis) — lets FinishAnalysis know whether it
    // still needs to classify+publish itself (semantic tagging was disabled, so onTagsReady never
    // fired) or whether that already happened early and it should just do the theme-transition
    // wait + advance to Gameplay.
    private bool _styleDetectedEarly;

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;

    private void Awake()
    {
        _preAnalyzer = gameObject.AddComponent<AudioPreAnalyzer>();

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _config = appConfig != null ? appConfig.audioAnalysis : null;
    }

    private void OnEnable()
    {
        _onFlowStateChanged = e =>
        {
            if (e.Current == GameFlowState.SongAnalysis) BeginAnalysis();
        };
        EventBus.Subscribe(_onFlowStateChanged);

        // Same UI-Scene-loads-asynchronously race as the other Frontend screens.
        if (AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.SongAnalysis)
            BeginAnalysis();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
    }

    // SongSelectionController now requests SongAnalysis IMMEDIATELY on Play — for a catalog song,
    // GameSession.SelectedSong.Clip is very likely still null at that instant (the Addressables
    // clip load is still in flight) so the Analyzing screen shows up instantly instead of only
    // after that load finishes. Wait here instead of bailing out the moment the clip isn't ready.
    private const float ClipWaitTimeoutSeconds = 20f;

    private void BeginAnalysis() => StartCoroutine(WaitForClipThenAnalyze());

    private IEnumerator WaitForClipThenAnalyze()
    {
        if (_config == null)
        {
            Debug.LogWarning("[SongAnalysisController] No AudioAnalysisConfig (AppConfig.audioAnalysis) " +
                              "assigned — cannot analyze.");
            yield break;
        }

        float waited = 0f;
        SelectedSongInfo? selected;
        while ((selected = GameSession.Instance != null ? GameSession.Instance.SelectedSong : null) is not { Clip: not null })
        {
            // The player may have bailed back to Song Selection (a failed load) while we were
            // waiting — stop polling instead of analyzing a stale/irrelevant clip once one finally
            // does appear from a LATER selection.
            if (AppBootstrap.Context == null || AppBootstrap.Context.AppFlow.CurrentState != GameFlowState.SongAnalysis)
                yield break;

            waited += Time.deltaTime;
            if (waited > ClipWaitTimeoutSeconds)
            {
                Debug.LogWarning("[SongAnalysisController] Gave up waiting for GameSession.SelectedSong's " +
                                  "clip to finish loading — nothing to analyze.");
                yield break;
            }

            yield return null;
        }

        _styleDetectedEarly = false;
        StartCoroutine(_preAnalyzer.Analyze(selected.Value.Clip, _config, OnProfileReady, OnTagsReady));
    }

    // Fires as soon as the semantic-tagging pass knows this song's tags — genuinely before the
    // rest of the (level-generation-critical) analysis finishes, since that pass only needs the
    // raw clip data (see AudioPreAnalyzer's own doc). Classifying and publishing HERE, instead of
    // waiting for the full SongProfile, is what lets the theme start changing while the detailed
    // per-frame analysis is still running in the background.
    private void OnTagsReady(MusicTagScore[] tags)
    {
        _styleDetectedEarly = true;
        ClassifyAndPublish(new SongProfile { musicTags = tags });
    }

    private void OnProfileReady(SongProfile profile) => StartCoroutine(FinishAnalysis(profile));

    private IEnumerator FinishAnalysis(SongProfile profile)
    {
        // Fallback only — semantic tagging was disabled (or produced nothing), so OnTagsReady
        // never fired. Classify from the now-complete profile instead, exactly as before this
        // early-detection path existed.
        if (!_styleDetectedEarly)
            ClassifyAndPublish(profile);

        yield return new WaitForSeconds(ThemeTransitionController.Duration + PostDetectionBufferSeconds);

        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.Gameplay);
    }

    private void ClassifyAndPublish(SongProfile profileForClassification)
    {
        var style = _musicStyleClassifier.Classify(profileForClassification);
        if (GameSession.Instance != null) GameSession.Instance.DetectedMusicStyleId = style;
        EventBus.Publish(new MusicStyleDetectedEvent { Style = style, Profile = profileForClassification });
    }
}
