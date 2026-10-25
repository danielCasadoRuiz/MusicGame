using System.Collections;
using UnityEngine;

/// <summary>
/// Owns GameFlowState.SongAnalysis: the CONTENT half of the readiness barrier between "Play" and the first
/// playable instant (see SongPreparation for the explicit states), then the style / theme transition, then
/// the advance to Gameplay. Lives in the always-loaded UI Scene (added by UIFlowController), so nothing of
/// the Runner scene exists yet while this runs.
///
/// Two kinds of selection (GameSession.SelectedSong, bound to its SelectionGeneration):
///   CATALOG song (published, P3) — CatalogSongLoader: published index → download (real bytes) → audio +
///     profile loaded in parallel → header/revision validation → profile built on a worker → clip data
///     loaded. NO FFT, NO inference, no analysis cache. The loaded content is ADOPTED by GameSession
///     (lease = handle ownership), then SongProfileReadyEvent is published once and the style is resolved
///     from the profile's own published tags + features (MusicStyleResolver.Resolve — no Sentis model).
///     A failure is typed (download / load / incompatible content) and returns to Song Selection with that
///     reason; it is NEVER covered by a silent analysis.
///   IMPORTED file (unchanged path) — AudioPreAnalyzer runs the dynamic analysis (one request bound to the
///     selection generation; early style from tags when conclusive), as before.
/// Then, for both: wait for the style reveal (Analyzing screen, a presentation beat) AND the theme change
/// to be really settled (ThemeManager.IsStyleTransitionSettled: style visual loaded/applied + its colour
/// transition finished) — explicit completions, the theme one bounded by a timeout that FAILS instead of
/// continuing — and request Gameplay. The Runner half of the barrier (scene, world, timeline, pools, clip
/// state) is GameplayManager.GenerateAndStart, which alone may publish GameStartedEvent (countdown + clock).
///
/// Identity: every step re-checks its own token and the selection generation; leaving SongAnalysis (back
/// to Song Selection) cancels the running preparation, which then releases only its own handles and
/// publishes nothing.
/// </summary>
public class SongAnalysisController : MonoBehaviour
{
    // Safety nets — never a substitute for the real completions they bound.
    private const float SelectionWaitTimeoutSeconds = 20f; // a selection is recorded within a frame of Play
    private const float RevealTimeoutSeconds = 4f;         // presentation beat (Analyzing screen caption)
    private const float ThemeSettleTimeoutSeconds = 15f;   // style visual load + colour transition

    private MusicStyleRulesSO _styleRules; // AppConfigSO.musicStyleRules (null = built-in defaults)

    private AudioPreAnalyzer     _preAnalyzer;
    private AudioAnalysisConfig  _config;

    // GameMusicStyle is published ONCE per song: at tags time only when conclusive (imports), otherwise
    // once the full profile is known. _stylePublished guards against a second publication.
    private bool _stylePublished;
    private bool _revealFinished;

    // The preparation this controller is currently driving: a token per state entry. Every coroutine step
    // compares its own token with this one; the analysis request id is tracked separately.
    private int _token;
    private int _requestId;
    // Selection generation the running preparation belongs to (−1 = none yet / waiting for a selection).
    private int _preparingGeneration = -1;
    private CatalogSongLoader _loader;
    private Coroutine _routine;

    /// <summary>The last failed preparation's reason (Song Selection shows it).</summary>
    public static SongContentError LastError { get; private set; }

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
            if (e.Current == GameFlowState.SongAnalysis) Begin();
            // Left SongAnalysis while still preparing (e.g. back to Song Selection): cancel it.
            else if (e.Previous == GameFlowState.SongAnalysis) Cancel();
        };
        EventBus.Subscribe(_onFlowStateChanged);
        _onRevealFinished = _ => _revealFinished = true;
        EventBus.Subscribe(_onRevealFinished);

        // Same UI-Scene-loads-asynchronously race as the other Frontend screens.
        if (InSongAnalysis) Begin();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
        EventBus.Unsubscribe(_onRevealFinished);
        Cancel();
    }

    private static bool InSongAnalysis =>
        AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.SongAnalysis;

    private void Begin()
    {
        // One preparation per state entry (a second Begin — the OnEnable race — replaces the first);
        // AudioPreAnalyzer itself joins a duplicate request for the same clip + selection.
        Cancel();
        int token = ++_token;
        _routine = StartCoroutine(Prepare(token));
    }

    // A selection that changes WHILE in SongAnalysis (an entry point that recorded it after entering the
    // state, or a second request) supersedes the running preparation: that one is cancelled (its stale
    // results are still rejected by the generation checks) and the current selection is prepared — the
    // screen is never left waiting with no preparation running.
    private void Update()
    {
        if (_preparingGeneration < 0 || !InSongAnalysis || GameSession.Instance == null) return;
        // A cleared selection (null) is a change too: restart so Prepare waits for the new one under its own
        // timeout (→ Fail → Song Selection) instead of the cancelled preparation leaving the screen waiting.
        int current = GameSession.Instance.SelectionGeneration;
        if (current == _preparingGeneration) return;
        Debug.Log($"[SongAnalysisController] Selection changed during the preparation (#{_preparingGeneration} → #{current}) — preparing the current selection.");
        Begin();
    }

    private void Cancel()
    {
        _preparingGeneration = -1;
        _token++; // every running step of the previous preparation sees a foreign token from now on
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        if (_loader != null)
        {
            // A stopped coroutine does not run the loader's own cleanup: cancel it explicitly and release what
            // it acquired but never handed over (a lease is only ever owned by GameSession after adoption).
            _loader.Abort();
            _loader = null;
        }
        if (_requestId != 0 && _preAnalyzer != null && _preAnalyzer.CurrentRequestId == _requestId) _preAnalyzer.Cancel();
        _requestId = 0;
    }

    private IEnumerator Prepare(int token)
    {
        if (_config == null)
        {
            Fail(CurrentGeneration, SongContentError.IncompatibleContent, "no AudioAnalysisConfig (AppConfig.audioAnalysis) assigned");
            yield break;
        }

        // The selection is recorded right after Play (SongSelectionService); wait for it, never for a clip.
        float waited = 0f;
        SelectedSongInfo? selected;
        while ((selected = GameSession.Instance != null ? GameSession.Instance.SelectedSong : null) == null)
        {
            if (!InSongAnalysis || token != _token) yield break;
            waited += Time.unscaledDeltaTime;
            if (waited > SelectionWaitTimeoutSeconds)
            {
                Fail(CurrentGeneration, SongContentError.LoadFailed, "no song selection was recorded");
                yield break;
            }
            yield return null;
        }

        int generation = GameSession.Instance.SelectionGeneration;
        _preparingGeneration = generation;
        var song = selected.Value;
        _stylePublished = false;
        _revealFinished = false;

        if (song.IsCatalogSong) yield return PrepareCatalogSong(song, generation, token);
        else if (song.Clip != null) BeginAnalysis(song, generation, token);
        else Fail(generation, SongContentError.LoadFailed, "imported song without audio");
    }

    private static int CurrentGeneration => GameSession.Instance != null ? GameSession.Instance.SelectionGeneration : 0;

    // ── Catalog song: published content, no analysis ─────────────────────────────

    private IEnumerator PrepareCatalogSong(SelectedSongInfo song, int generation, int token)
    {
        var loader = _loader = new CatalogSongLoader(song.SongId, generation, _config);
        yield return loader.Run();
        if (_loader == loader) _loader = null;
        if (token != _token || !loader.IsCurrent)
        {
            loader.Lease?.Release(); // prepared for a selection that is gone: never adopted, never published
            yield break;
        }
        if (!loader.Succeeded) { Fail(generation, loader.Error, loader.ErrorDetail); yield break; }

        if (!GameSession.Instance.AdoptCatalogContent(generation, loader.Lease))
        {
            loader.Lease.Release();
            yield break;
        }

        // ONE publish of the complete profile for this selection (GameSession fills Profile from it).
        EventBus.Publish(new SongProfileReadyEvent { Profile = loader.Profile, SelectionGeneration = generation });

        // Style from the PUBLISHED profile (its stored tags + features): the semantic model is never touched.
        SongPreparation.Enter(generation, SongPreparationStage.ResolvingStyle);
        PublishFinal(MusicStyleResolver.Resolve(loader.Profile, _styleRules), loader.Profile);
        yield return FinishPreparation(token, generation);
    }

    // ── Imported song: dynamic analysis (unchanged behaviour) ─────────────────────

    private void BeginAnalysis(SelectedSongInfo song, int generation, int token)
    {
        SongPreparation.Enter(generation, SongPreparationStage.Analysing, song.DisplayName);
        int id = 0;
        id = _preAnalyzer.Begin(new SongAnalysisRequest
        {
            Clip                = song.Clip,
            SongId              = song.SongId,
            LocalFilePath       = song.LocalFilePath,
            SelectionGeneration = generation,
            OnTagsReady         = OnTagsReady,
            OnProfileReady      = p =>
            {
                if (token != _token || id != _requestId) return;
                _requestId = 0;
                // Pending (or tagging disabled): resolve once from tags + the full SongProfile features.
                if (!_stylePublished) PublishFinal(MusicStyleResolver.Resolve(p, _styleRules), p);
                _routine = StartCoroutine(FinishPreparation(token, generation));
            },
            OnFailed            = OnAnalysisFailed,
        }, _config);
        _requestId = id;
    }

    // Fires as soon as the semantic-tagging pass knows this song's tags, before the rest of the
    // analysis. The style is FINAL here only when conclusive (a dominant recognized genre — see
    // MusicStyleResolver.ResolveEarly / MusicStyleRulesSO early thresholds); otherwise it stays pending
    // and nothing user-facing changes until the profile is complete.
    private void OnTagsReady(MusicTagScore[] tags)
    {
        if (_stylePublished) return;
        var early = MusicStyleResolver.ResolveEarly(tags, _styleRules);
        Debug.Log($"[SongAnalysisController] Tags: {early.Style} {early.Score:0.00} (margin {early.Margin:0.00} over {early.RunnerUp}) → " +
                  (early.Conclusive ? "conclusive, final" : "pending full analysis"));
        if (early.Conclusive) PublishFinal(early, new SongProfile { musicTags = tags });
        else EventBus.Publish(new MusicStylePendingEvent());
    }

    // No silent fallback: back to Song Selection, the cause is logged.
    private void OnAnalysisFailed(string error)
    {
        _requestId = 0;
        Fail(CurrentGeneration, SongContentError.LoadFailed, "song analysis failed: " + error);
    }

    // ── Shared tail: style reveal + theme really settled → Gameplay ─────────────

    private IEnumerator FinishPreparation(int token, int generation)
    {
        bool Current() => token == _token && GameSession.Instance != null && GameSession.Instance.SelectionGeneration == generation;

        SongPreparation.Enter(generation, SongPreparationStage.ApplyingTheme);
        float revealDeadline = Time.unscaledTime + RevealTimeoutSeconds;
        float themeDeadline  = Time.unscaledTime + ThemeSettleTimeoutSeconds;
        var themes = ThemeManager.Instance;
        while (!_revealFinished || (themes != null && !themes.IsStyleTransitionSettled))
        {
            if (!Current()) yield break; // cancelled, or a newer selection took over
            if (!_revealFinished && Time.unscaledTime > revealDeadline)
            {
                // The caption is a presentation beat, not a dependency of the game.
                Debug.LogWarning("[SongAnalysisController] Style reveal not reported in time — continuing.");
                _revealFinished = true;
            }
            if (Time.unscaledTime > themeDeadline)
            {
                Fail(generation, SongContentError.LoadFailed, $"theme change not settled within {ThemeSettleTimeoutSeconds:F0} s " +
                                                              $"({themes.PendingStyleLoads} style visual load(s) pending)");
                yield break;
            }
            yield return null;
        }
        if (!Current()) yield break;

        _routine = null;
        if (!InSongAnalysis) yield break;
        SongPreparation.Enter(generation, SongPreparationStage.LoadingScene);
        AppBootstrap.Context.AppFlow.RequestState(GameFlowState.Gameplay);
    }

    private void Fail(int generation, SongContentError error, string detail)
    {
        LastError = error;
        Debug.LogError($"[SongAnalysisController] Preparation #{generation} failed ({error}): {detail} — back to Song Selection.");
        if (SongPreparation.Current != SongPreparationStage.Failed || SongPreparation.CurrentGeneration != generation)
            SongPreparation.Enter(generation, SongPreparationStage.Failed, detail, -1f, 0, error);
        _routine = null;
        if (InSongAnalysis) AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongSelection);
    }

    // Raw tags/features → controlled GameMusicStyle (MusicStyleResolver); never the raw top tag.
    private void PublishFinal(MusicStyleResolution res, SongProfile profile)
    {
        _stylePublished = true;
        Debug.Log($"[SongAnalysisController] FINAL game style {res.Style} (score {res.Score:0.00}, margin {res.Margin:0.00}, " +
                  $"theme {res.ThemeStyle}, features {(res.UsedFeatures ? "yes" : "tags only")})");
        if (GameSession.Instance != null)
        {
            GameSession.Instance.DetectedMusicStyleId = res.ThemeStyle;
            GameSession.Instance.DetectedGameStyle = res.Style;
        }
        EventBus.Publish(new MusicStyleDetectedEvent { Style = res.ThemeStyle, GameStyle = res.Style, Profile = profile });
    }
}
