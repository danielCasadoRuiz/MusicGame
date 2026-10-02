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
///      own doc — OnTagsReady runs MusicStyleResolver.ResolveEarly. ONLY if the tags are conclusive
///      (dominant recognized genre, confidence + margin from MusicStyleRulesSO) the FINAL style is
///      published right away (GameSession + MusicStyleDetectedEvent → ThemeManager swaps the theme).
///      Otherwise the style stays PENDING (MusicStylePendingEvent, no UI/theme change).
///   3. Full profile ready: if still pending, resolve ONCE from tags + all SongProfile features and
///      publish. The style is never published twice. Then wait for MusicStyleRevealFinishedEvent
///      from the Analyzing screen (already true if the early reveal ended) and at least one theme
///      transition since publication — with a timeout fallback, never forever.
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

    private MusicStyleRulesSO _styleRules; // AppConfigSO.musicStyleRules (null = built-in defaults)

    private AudioPreAnalyzer     _preAnalyzer;
    private AudioAnalysisConfig  _config;

    // GameMusicStyle is published ONCE per song: at tags time only when conclusive, otherwise after
    // the full analysis. _stylePublished guards against a second publication.
    private bool _stylePublished;
    private bool _revealFinished;
    private float _publishedAt;

    // Safety net only — the Analyzing screen normally reports the end of its reveal well before this.
    private const float RevealTimeoutSeconds = 4f;

    private System.Action<MusicStyleRevealFinishedEvent> _onRevealFinished;
    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;

    private void Awake()
    {
        _preAnalyzer = gameObject.AddComponent<AudioPreAnalyzer>();

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _styleRules = appConfig != null ? appConfig.musicStyleRules : null;
        _config = appConfig != null ? appConfig.audioAnalysis : null;
    }

    private void OnEnable()
    {
        _onFlowStateChanged = e =>
        {
            if (e.Current == GameFlowState.SongAnalysis) BeginAnalysis();
        };
        EventBus.Subscribe(_onFlowStateChanged);
        _onRevealFinished = _ => _revealFinished = true;
        EventBus.Subscribe(_onRevealFinished);

        // Same UI-Scene-loads-asynchronously race as the other Frontend screens.
        if (AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.SongAnalysis)
            BeginAnalysis();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
        EventBus.Unsubscribe(_onRevealFinished);
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

        _stylePublished = false;
        _revealFinished = false;
        StartCoroutine(_preAnalyzer.Analyze(selected.Value.Clip, _config, OnProfileReady, OnTagsReady));
    }

    // Fires as soon as the semantic-tagging pass knows this song's tags, before the rest of the
    // analysis. The style is FINAL here only when conclusive (a dominant recognized genre — see
    // MusicStyleResolver.ResolveEarly / MusicStyleRulesSO early thresholds); otherwise it stays pending
    // and nothing user-facing changes until FinishAnalysis.
    private void OnTagsReady(MusicTagScore[] tags)
    {
        if (_stylePublished) return;
        var early = MusicStyleResolver.ResolveEarly(tags, _styleRules);
        Debug.Log($"[SongAnalysisController] Tags: {early.Style} {early.Score:0.00} (margin {early.Margin:0.00} over {early.RunnerUp}) → " +
                  (early.Conclusive ? "conclusive, final" : "pending full analysis"));
        if (early.Conclusive) PublishFinal(early, new SongProfile { musicTags = tags });
        else EventBus.Publish(new MusicStylePendingEvent());
    }

    private void OnProfileReady(SongProfile profile) => StartCoroutine(FinishAnalysis(profile));

    private IEnumerator FinishAnalysis(SongProfile profile)
    {
        // Pending (or tagging disabled): resolve once from tags + the full SongProfile features.
        if (!_stylePublished)
            PublishFinal(MusicStyleResolver.Resolve(profile, _styleRules), profile);

        // Wait for the Analyzing screen to finish revealing the final style (immediate when the early
        // reveal already ended), and for the theme transition started at publication. Never forever.
        float minThemeEnd = _publishedAt + ThemeTransitionController.Duration + PostDetectionBufferSeconds;
        float timeout = Time.unscaledTime + RevealTimeoutSeconds;
        while ((!_revealFinished || Time.unscaledTime < minThemeEnd) && Time.unscaledTime < timeout)
            yield return null;
        if (!_revealFinished) Debug.LogWarning("[SongAnalysisController] Style reveal not reported in time — continuing to Gameplay.");

        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.Gameplay);
    }

    // Raw tags/features → controlled GameMusicStyle (MusicStyleResolver); never the raw top tag.
    private void PublishFinal(MusicStyleResolution res, SongProfile profile)
    {
        _stylePublished = true;
        _publishedAt = Time.unscaledTime;
        Debug.Log($"[SongAnalysisController] FINAL game style {res.Style} (score {res.Score:0.00}, margin {res.Margin:0.00}, " +
                  $"theme {res.ThemeStyle}, features {(res.UsedFeatures ? "yes" : "tags only")})");
        Publish(res, profile);
    }

    private static void Publish(MusicStyleResolution res, SongProfile profile)
    {
        if (GameSession.Instance != null)
        {
            GameSession.Instance.DetectedMusicStyleId = res.ThemeStyle;
            GameSession.Instance.DetectedGameStyle = res.Style;
        }
        EventBus.Publish(new MusicStyleDetectedEvent { Style = res.ThemeStyle, GameStyle = res.Style, Profile = profile });
    }
}
