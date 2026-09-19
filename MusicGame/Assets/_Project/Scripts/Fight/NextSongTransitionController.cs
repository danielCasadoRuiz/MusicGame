using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// The single, short "LEVEL {n} / NEXT SONG / {name}" cartela shown right after the Player presses
/// Continue on a won match — see this phase's own explicit "UNA sola cartela, no Next Song + Song
/// Reveal separades" requirement. Owns the ENTIRE Continue hand-off:
///   1. Picks the next song via INextSongSelector (RandomNextSongSelector today — see that
///      interface's own doc on why this is never a raw Random.Range call inline here), from the
///      exact same "Song" Addressables catalog SongSelectionController itself queries.
///   2. Writes it into GameSession.SelectedSong via the EXACT SAME SongSelectionService/
///      AddressableSongSource SongSelectionController's own PLAY button already uses — never a
///      second, parallel song-loading path (task's own explicit "no dupliquis... càrrega d'àudio"
///      requirement).
///   3. Shows this cartela (already visible, with the level, the instant BeginWin is called — the
///      song name fills in a moment later once step 1-2 resolve, which in practice is a near-instant
///      local catalog lookup — see SongSelectionController's own doc), holds it for
///      FightFlowConfig.nextSongCardHoldDuration, fades it out over nextSongCardFadeDuration, then
///      requests GameFlowState.SongAnalysis.
///
/// From that instant on, SongAnalysisController/AnalyzingScreenController own EVERYTHING (analysis,
/// cache, theme transition, advancing to Gameplay) exactly as if the player had just pressed Play on
/// Song Selection — this class never touches any of that itself, and never fades to Main Menu/Song
/// Selection/Play in between (task's own explicit "no vull pantalles intermèdies" requirement): the
/// AnalyzingScreen's own opaque overlay (which shows itself the instant GameFlowState.SongAnalysis is
/// entered) is what actually covers the Fight-scene-to-Frontend-scene swap underneath, the same way
/// it already covers Song Selection's own scene-load gap.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController). NOT reactive to any
/// FightFlowState/GameFlowState itself — there is no dedicated state for this cartela — it is only
/// ever invoked directly by MatchResultController.OnContinueClicked, the same "one controller calls
/// directly into another to hand off a decision" pattern RoundEndController already uses with
/// FightMatchController.NotifyRoundEndDisplayComplete.
/// </summary>
public class NextSongTransitionController : MonoBehaviour
{
    public static NextSongTransitionController Instance { get; private set; }

    private const string SongLabel = "Song";

    private RectTransform   _root;
    private CanvasGroup     _canvasGroup;
    private TextMeshProUGUI _levelText;
    private TextMeshProUGUI _headerText;
    private TextMeshProUGUI _songNameText;

    private FightFlowConfig _config;
    private SongSelectionService _service;
    private readonly INextSongSelector _selector = new RandomNextSongSelector();

    private Coroutine _routine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _config = appConfig != null ? appConfig.fightFlow : null;

        _service = gameObject.AddComponent<SongSelectionService>();

        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.NextSongTransition != null) WireUI(registry.NextSongTransition);
        else Build();

        _root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Prefab path — see NextSongTransitionView's own doc ───────────────────────

    private void WireUI(NextSongTransitionView view)
    {
        _root         = view.root.GetComponent<RectTransform>();
        _canvasGroup  = view.canvasGroup;
        _levelText    = view.levelText;
        _headerText   = view.headerText;
        _songNameText = view.songNameText;

        view.headerText.text = Loc.Get("NextSong.Header");
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("NextSongTransitionScreen", canvas);
        UIFactory.Stretch(_root);
        _canvasGroup = _root.gameObject.AddComponent<CanvasGroup>();

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        _levelText = UIFactory.CreateText("Level", _root, "", 26, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_levelText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 70f), new Vector2(700f, 40f));
        _levelText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Secondary, UIFontToken.Body);

        _headerText = UIFactory.CreateText("Header", _root, Loc.Get("NextSong.Header"), 20, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_headerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 20f), new Vector2(700f, 34f));
        _headerText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);

        _songNameText = UIFactory.CreateText("SongName", _root, "", 40, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_songNameText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -40f), new Vector2(900f, 60f));
        _songNameText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
    }

    // ── The Continue hand-off — see class doc ────────────────────────────────────

    /// <summary>Call ONLY from MatchResultController.OnContinueClicked. `newPlayerLevel` is whatever
    /// FightMatchController already decided (see MatchEndedEvent.NewPlayerLevel's own doc) — this
    /// only ever DISPLAYS it, never decides it.</summary>
    public void BeginWin(int newPlayerLevel)
    {
        if (_routine != null) StopCoroutine(_routine);

        // Shown immediately (level already known) so there is no dead frame of the Fight scene
        // behind it while the next song resolves — the song name fills in a moment later, once
        // RunWinSequence's catalog query/load actually completes.
        _canvasGroup.alpha = 1f;
        _levelText.text = Loc.Get("NextSong.Level", newPlayerLevel.ToString());
        _songNameText.text = "";
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();

        _routine = StartCoroutine(RunWinSequence());
    }

    private IEnumerator RunWinSequence()
    {
        // The just-played song's own identity — see INextSongSelector's own doc on why this (its
        // Addressables PrimaryKey) is reused as-is rather than inventing a new Song ID scheme.
        string previousDisplayName = GameSession.Instance != null && GameSession.Instance.SelectedSong.HasValue
            ? GameSession.Instance.SelectedSong.Value.DisplayName
            : null;

        var catalog = new List<IResourceLocation>();
        AsyncOperationHandle<IList<IResourceLocation>> locationsHandle =
            Addressables.LoadResourceLocationsAsync(SongLabel, typeof(AudioClip));
        yield return locationsHandle;
        if (locationsHandle.Status == AsyncOperationStatus.Succeeded)
            catalog.AddRange(locationsHandle.Result);
        else
            Debug.LogWarning("[NextSongTransitionController] Failed to query the 'Song' Addressables label.");
        Addressables.Release(locationsHandle);

        var nextLocation = _selector.SelectNext(catalog, previousDisplayName);
        if (nextLocation == null)
        {
            Debug.LogWarning("[NextSongTransitionController] No song available in the 'Song' catalog — cannot continue. Falling back to Main Menu.");
            _root.gameObject.SetActive(false);
            FightMusicController.Instance?.Stop();
            AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
            yield break;
        }

        bool loaded = false;
        yield return _service.SelectSong(new AddressableSongSource(nextLocation), null, success => loaded = success);

        if (!loaded)
        {
            Debug.LogWarning($"[NextSongTransitionController] Failed to load '{nextLocation.PrimaryKey}' — falling back to Main Menu.");
            _root.gameObject.SetActive(false);
            FightMusicController.Instance?.Stop();
            AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
            yield break;
        }

        _songNameText.text = nextLocation.PrimaryKey;

        float hold = _config != null ? Mathf.Max(0f, _config.nextSongCardHoldDuration) : 1.6f;
        float fade = _config != null ? Mathf.Max(0.05f, _config.nextSongCardFadeDuration) : 0.35f;

        yield return new WaitForSeconds(hold);

        // Leaving Fight for good — see FightMusicController.Stop's own doc. Requested BEFORE the
        // fade finishes so AnalyzingScreenController's own opaque overlay is already up underneath
        // this cartela, covering the Fight-to-Frontend scene swap the instant it happens (see class
        // doc) — this cartela's own fade-out then simply reveals it, a clean cross-fade rather than a
        // hard cut.
        FightMusicController.Instance?.Stop();
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongAnalysis);

        float t = 0f;
        while (t < fade)
        {
            t += Time.deltaTime;
            _canvasGroup.alpha = Mathf.Lerp(1f, 0f, t / fade);
            yield return null;
        }

        _root.gameObject.SetActive(false);
        _routine = null;
    }
}
