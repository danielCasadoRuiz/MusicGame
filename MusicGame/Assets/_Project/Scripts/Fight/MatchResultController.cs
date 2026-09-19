using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The real post-Fight result screen — ONE configurable MatchResultView representing both Win and
/// Lose (see that class's own doc), never two parallel screens. Shown for FightFlowState.MatchWon/
/// MatchLost; FightMatchController already decided everything shown here (rounds, reason, health%,
/// and — for a win — the OLD/NEW PlayerLevel) via MatchEndedEvent, cached the instant it arrives
/// (same _pendingData pattern as RoundEndController) — this class only presents it and reacts to the
/// player's own choice:
///
///   WIN  -> Continue: hands off entirely to NextSongTransitionController (see its own doc) — this
///           class never touches song selection/loading itself.
///   LOSE -> Fight Again (GameSession.FightResources.ExtraLives > 0): consumes one life, restarts the
///           SAME match (same GameSession.SelectedOpponent/SelectedOpponentLevelConfig/
///           SelectedOpponentSong — never re-runs Opponent Selection) via FightFlowState.VersusIntro,
///           exactly the "Fight Again -> VS curt -> Round 1 -> Countdown -> Fight" sequence the task
///           asked for (FightMatchController.BeginMatch/VersusScreenController's own existing
///           sequencing does the rest, unchanged).
///        -> Fight Again (no lives): opens the Rewarded Ad confirmation sub-panel instead (see
///           IRewardedAdService's own doc) — Watch Ad grants +1 life and immediately starts Fight
///           Again on success; Cancel returns to this same Lose screen.
///        -> Replay Song: GameSession.SelectedSong/Profile already hold the exact song + analysis
///           that led to this Fight (nothing overwrites them during a Fight session) — requests
///           GameFlowState.Gameplay DIRECTLY, skipping SongAnalysis/AnalyzingScreen entirely (see
///           OnReplaySongClicked's own doc on why a cache hit isn't good enough here). A fresh
///           Runner.unity load (via RunnerSceneBootstrap, same as any normal entry) gives a genuinely
///           new run of the SAME song. Does NOT consume a life.
///   BOTH -> Main Menu: unchanged.
///
/// Prefers a real MatchResult.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to a procedural build only if that hasn't been
/// run yet, same pattern as every other Fight-flow screen.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// FightFlowStateChangedEvent directly.
/// </summary>
public class MatchResultController : MonoBehaviour
{
    private RectTransform   _root;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _rivalText;
    private TextMeshProUGUI _roundsText;
    private TextMeshProUGUI _summaryText;
    private TextMeshProUGUI _levelText;

    private Button          _continueButton;
    private Button          _fightAgainButton;
    private TextMeshProUGUI _fightAgainButtonLabel;
    private Button          _replaySongButton;
    private Button          _mainMenuButton;

    private GameObject _rewardAdModalRoot;

    private readonly IRewardedAdService _adService = new NotImplementedRewardedAdService();

    private MatchEndedEvent? _pendingMatchEnded;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;
    private System.Action<MatchEndedEvent> _onMatchEnded;
    private System.Action<GameFlowStateChangedEvent> _onGameFlowChanged;

    /// <summary>Set only while Replay Song is waiting for Runner to actually be ready — see
    /// OnReplaySongClicked's own doc. Lets _onGameStarted below tell "a real Replay Song transition
    /// finished" apart from GameStartedEvent firing for any unrelated reason.</summary>
    private bool _awaitingReplaySongReady;
    private System.Action<GameStartedEvent> _onGameStarted;

    private void Awake()
    {
        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.MatchResult != null) WireUI(registry.MatchResult);
        else Build();

        _root.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        // Cached the instant it arrives — FightMatchController publishes MatchEndedEvent BEFORE
        // requesting FightFlowState.MatchWon/MatchLost (see its own NotifyRoundEndDisplayComplete),
        // so this data is always ready by the time Show() below actually runs.
        _onMatchEnded = e => _pendingMatchEnded = e;
        _onFightFlowChanged = e =>
        {
            if (e.Current == FightFlowState.MatchWon) Show(true);
            else if (e.Current == FightFlowState.MatchLost) Show(false);
            // A fresh match starting over (see FightFlowController's own re-entry announcement) —
            // clean up so a stale result never lingers behind the next Opponent Selection.
            else if (e.Current == FightFlowState.OpponentSelection) ExitFightUI();
        };
        // See OnReplaySongClicked's own doc — this is the REAL "Runner is fully ready" signal
        // (GameplayManager only publishes it once the level is generated AND the player/camera are
        // already placed), the exact same one AnalyzingScreenController already uses to hide its own
        // full-screen cover after a scene transition into Gameplay. _awaitingReplaySongReady gates
        // this so an unrelated GameStartedEvent (e.g. a later real Gameplay entry) never hides this
        // screen by accident.
        _onGameStarted = _ =>
        {
            if (!_awaitingReplaySongReady) return;
            _awaitingReplaySongReady = false;
            Hide();
            SetReplaySongButtonsInteractable(true);
        };
        // Top-level safety net — every other Fight-exclusive screen gets the exact same one (see
        // VersusScreenController.OnEnable's own doc): FightFlowStateChangedEvent alone freezes the
        // instant top-level GameFlowState leaves Fight (FightFlowController stops publishing), so
        // without this, a result screen left showing at MatchWon/MatchLost (e.g. Main Menu clicked)
        // would stay active forever — exactly the reported "Match Result still up under Runner's
        // countdown" bug. The ONE exception: while _awaitingReplaySongReady is true, this screen IS
        // the intended cover for that specific transition (see OnReplaySongClicked's own doc) — the
        // very same GameFlowStateChangedEvent that fires here (Previous == Fight, on the Replay Song
        // click itself) is what starts that transition, so skipping cleanup in that one case is what
        // keeps Replay Song's fix intact; _onGameStarted above still guarantees it gets cleaned up
        // for real the moment Runner is actually ready.
        _onGameFlowChanged = e =>
        {
            if (e.Previous != GameFlowState.Fight) return;
            if (_awaitingReplaySongReady) return;
            ExitFightUI();
        };
        EventBus.Subscribe(_onMatchEnded);
        EventBus.Subscribe(_onFightFlowChanged);
        EventBus.Subscribe(_onGameStarted);
        EventBus.Subscribe(_onGameFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onMatchEnded);
        EventBus.Unsubscribe(_onFightFlowChanged);
        EventBus.Unsubscribe(_onGameStarted);
        EventBus.Unsubscribe(_onGameFlowChanged);
    }

    /// <summary>The one real cleanup point for this screen when Fight itself is genuinely being left
    /// behind (as opposed to mid-Replay-Song-transition — see OnEnable's own doc) — hides the root,
    /// closes the reward-ad sub-panel, re-enables every button, and drops the cached match data so a
    /// stale MatchEndedEvent can never leak into a later Show(). Also reachable via the normal
    /// FightFlowState.OpponentSelection re-entry hide (a fresh match starting over within the SAME
    /// Fight session) — this is the stronger, top-level version of that same idea.</summary>
    private void ExitFightUI()
    {
        Hide();
        HideRewardAdModal();
        SetReplaySongButtonsInteractable(true);
        _awaitingReplaySongReady = false;
        _pendingMatchEnded = null;
    }

    // ── Prefab path — see MatchResultView's own doc ──────────────────────────────

    private void WireUI(MatchResultView view)
    {
        _root        = view.root.GetComponent<RectTransform>();
        _titleText   = view.titleText;
        _rivalText   = view.rivalText;
        _roundsText  = view.roundsText;
        _summaryText = view.summaryText;
        _levelText   = view.levelText;

        _continueButton = view.continueButton;
        view.continueButton.onClick.AddListener(OnContinueClicked);
        view.continueButtonLabel.text = Loc.Get("MatchResult.Continue");

        _fightAgainButton = view.fightAgainButton;
        _fightAgainButtonLabel = view.fightAgainButtonLabel;
        view.fightAgainButton.onClick.AddListener(OnFightAgainClicked);

        _replaySongButton = view.replaySongButton;
        view.replaySongButton.onClick.AddListener(OnReplaySongClicked);
        view.replaySongButtonLabel.text = Loc.Get("MatchResult.ReplaySong");

        _mainMenuButton = view.mainMenuButton;
        view.mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        view.mainMenuButtonLabel.text = Loc.Get("Fight.MainMenu");

        _rewardAdModalRoot = view.rewardAdModalRoot;
        view.rewardAdTitleText.text = Loc.Get("MatchResult.WatchAdTitle");
        view.watchAdButton.onClick.AddListener(OnWatchAdClicked);
        view.watchAdButtonLabel.text = Loc.Get("MatchResult.WatchAd");
        view.cancelButton.onClick.AddListener(OnCancelAdClicked);
        view.cancelButtonLabel.text = Loc.Get("MatchResult.Cancel");
        _rewardAdModalRoot.SetActive(false);
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("MatchResultScreen", canvas);
        UIFactory.Stretch(_root);

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 0.9f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        _titleText = UIFactory.CreateText("Title", _root, "", 64, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 220f), new Vector2(800f, 90f));
        _titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        _rivalText = UIFactory.CreateText("Rival", _root, "", 20, Color.white);
        UIFactory.SetBox(_rivalText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 160f), new Vector2(700f, 30f));
        _rivalText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        _roundsText = UIFactory.CreateText("Rounds", _root, "", 40, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_roundsText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 110f), new Vector2(400f, 50f));
        _roundsText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);

        _summaryText = UIFactory.CreateText("Summary", _root, "", 16, Color.white);
        UIFactory.SetBox(_summaryText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 55f), new Vector2(700f, 50f));
        _summaryText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        _levelText = UIFactory.CreateText("Level", _root, "", 22, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_levelText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0f), new Vector2(500f, 34f));
        _levelText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Positive, UIFontToken.Body);

        var continueBtn = UIFactory.CreateButton("ContinueButton", _root, Loc.Get("MatchResult.Continue"), out var continueLabel);
        UIFactory.SetBox(continueBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -60f), new Vector2(260f, 52f));
        continueBtn.onClick.AddListener(OnContinueClicked);
        continueBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        continueLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        _continueButton = continueBtn;

        var fightAgainBtn = UIFactory.CreateButton("FightAgainButton", _root, "", out var fightAgainLabel);
        UIFactory.SetBox(fightAgainBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -60f), new Vector2(260f, 52f));
        fightAgainBtn.onClick.AddListener(OnFightAgainClicked);
        fightAgainBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        fightAgainLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        _fightAgainButton = fightAgainBtn;
        _fightAgainButtonLabel = fightAgainLabel;

        var replaySongBtn = UIFactory.CreateButton("ReplaySongButton", _root, Loc.Get("MatchResult.ReplaySong"), out var replaySongLabel);
        UIFactory.SetBox(replaySongBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -120f), new Vector2(260f, 52f));
        replaySongBtn.onClick.AddListener(OnReplaySongClicked);
        replaySongBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        replaySongLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        _replaySongButton = replaySongBtn;

        var mainMenuBtn = UIFactory.CreateButton("MainMenuButton", _root, Loc.Get("Fight.MainMenu"), out var mainMenuLabel);
        UIFactory.SetBox(mainMenuBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -180f), new Vector2(260f, 52f));
        mainMenuBtn.onClick.AddListener(OnMainMenuClicked);
        mainMenuBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        mainMenuLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        _mainMenuButton = mainMenuBtn;

        BuildRewardAdModal();
    }

    private void BuildRewardAdModal()
    {
        var modal = UIFactory.CreateRect("RewardAdModal", _root);
        UIFactory.Stretch(modal);

        var dim = UIFactory.CreatePanel("Dim", modal, new Color(0f, 0f, 0f, 0.75f));
        UIFactory.Stretch(dim.rectTransform);

        var panel = UIFactory.CreatePanel("Panel", modal, new Color(0.05f, 0.05f, 0.05f, 0.98f));
        UIFactory.SetBox(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 220f));
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        var title = UIFactory.CreateText("Title", panel.rectTransform, Loc.Get("MatchResult.WatchAdTitle"), 22, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(380f, 60f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);

        var watchAdBtn = UIFactory.CreateButton("WatchAdButton", panel.rectTransform, Loc.Get("MatchResult.WatchAd"), out var watchAdLabel);
        UIFactory.SetBox(watchAdBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(320f, 46f));
        watchAdBtn.onClick.AddListener(OnWatchAdClicked);
        watchAdBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        watchAdLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);

        var cancelBtn = UIFactory.CreateButton("CancelButton", panel.rectTransform, Loc.Get("MatchResult.Cancel"), out var cancelLabel);
        UIFactory.SetBox(cancelBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(320f, 46f));
        cancelBtn.onClick.AddListener(OnCancelAdClicked);
        cancelBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        cancelLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        _rewardAdModalRoot = modal.gameObject;
        _rewardAdModalRoot.SetActive(false);
    }

    // ── Show / hide ───────────────────────────────────────────────────────────────

    private void Show(bool playerWon)
    {
        var data = _pendingMatchEnded ?? default;

        // A fresh result screen always starts from a clean slate — in particular this cancels any
        // Replay Song wait left over from a PREVIOUS match's screen (should never happen in practice,
        // since OpponentSelection already hides this screen first, but never leave buttons disabled
        // or a stale subscription gate armed either way).
        _awaitingReplaySongReady = false;
        SetReplaySongButtonsInteractable(true);

        _titleText.text   = Loc.Get(playerWon ? "Fight.YouWin" : "Fight.YouLose");
        _rivalText.text   = OpponentDisplayName();
        _roundsText.text  = $"{data.PlayerRoundsWon} - {data.OpponentRoundsWon}";
        _summaryText.text = BuildSummary(data);

        _levelText.gameObject.SetActive(playerWon);
        if (playerWon) _levelText.text = Loc.Get("MatchResult.LevelUp", data.OldPlayerLevel.ToString(), data.NewPlayerLevel.ToString());

        _continueButton.gameObject.SetActive(playerWon);
        _fightAgainButton.gameObject.SetActive(!playerWon);
        _replaySongButton.gameObject.SetActive(!playerWon);
        if (!playerWon) RefreshFightAgainLabel();

        HideRewardAdModal();
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
    }

    private void Hide() => _root.gameObject.SetActive(false);

    /// <summary>Disables Fight Again/Replay Song/Main Menu the instant Replay Song is clicked (see
    /// its own doc — prevents a double-click firing a second scene transition while the first is
    /// still loading underneath this still-visible screen), re-enabled once it's actually hidden
    /// again (either by GameStartedEvent completing the transition, or by Show() starting a fresh
    /// result screen).</summary>
    private void SetReplaySongButtonsInteractable(bool interactable)
    {
        if (_fightAgainButton != null) _fightAgainButton.interactable = interactable;
        if (_replaySongButton != null) _replaySongButton.interactable = interactable;
        if (_mainMenuButton != null) _mainMenuButton.interactable = interactable;
    }

    private void RefreshFightAgainLabel()
    {
        int lives = ExtraLives;
        _fightAgainButtonLabel.text = lives > 0
            ? Loc.Get("MatchResult.FightAgainCount", lives.ToString())
            : Loc.Get("MatchResult.FightAgain");
    }

    private static int ExtraLives =>
        GameSession.Instance != null && GameSession.Instance.FightResources != null
            ? GameSession.Instance.FightResources.ExtraLives
            : 0;

    private static string BuildSummary(MatchEndedEvent data)
    {
        string reason    = Loc.Get(data.LastRoundReason == RoundEndReason.KO ? "Fight.KO" : "Fight.TimeUp");
        int playerHp     = Mathf.RoundToInt(data.PlayerHealthPercent * 100f);
        int opponentHp   = Mathf.RoundToInt(data.OpponentHealthPercent * 100f);
        string summary   = reason + "\n" + Loc.Get("MatchResult.FinalHealth", playerHp.ToString(), opponentHp.ToString());

        // The round scoreboard (_roundsText, e.g. "1-1") always stays honest about what actually
        // happened round by round — this line is the ONLY place a points/fallback decision is ever
        // surfaced, deliberately kept separate from that scoreboard (this format's own explicit
        // "Round result != Match resolution" rule — never fake a Round 3 win to explain this).
        if (data.Resolution == MatchResolution.PointsTiebreak || data.Resolution == MatchResolution.ExactTieFallback)
        {
            string winnerName = data.Winner == FighterSide.Player ? Loc.Get("Fight.PlayerName") : OpponentDisplayName();
            summary += "\n" + Loc.Get("Fight.WinsOnPoints", winnerName);
        }

        return summary;
    }

    private static string OpponentDisplayName() =>
        GameSession.Instance?.SelectedOpponent != null
            ? GameSession.Instance.SelectedOpponent.displayName
            : Loc.Get("Fight.RivalUnknown");

    // ── Button handlers ───────────────────────────────────────────────────────────

    private void OnContinueClicked()
    {
        int newLevel = _pendingMatchEnded?.NewPlayerLevel ?? (GameSession.Instance != null ? GameSession.Instance.PlayerLevel : 1);
        Hide();
        NextSongTransitionController.Instance?.BeginWin(newLevel);
    }

    private void OnFightAgainClicked()
    {
        if (ExtraLives > 0) BeginFightAgain();
        else ShowRewardAdModal();
    }

    private void BeginFightAgain()
    {
        if (GameSession.Instance?.FightResources != null) GameSession.Instance.FightResources.ExtraLives--;
        Hide();
        // Same rival/level/song as before — no Opponent Selection re-run (task's own explicit
        // requirement). FightMusicController.Lock is a no-op if this exact clip is already playing.
        FightMusicController.Instance?.Lock(GameSession.Instance?.SelectedOpponentSong);
        FightFlowController.Instance?.RequestState(FightFlowState.VersusIntro);
    }

    /// <summary>
    /// Requests GameFlowState.Gameplay DIRECTLY — SongAnalysis is never requested here (unchanged;
    /// see class doc). GameSession.SelectedSong/Profile already hold the exact song + analysis that
    /// led to this Fight (nothing overwrites them during a Fight session), so SceneFlowController.
    /// LoadMode(Runner) loads a completely FRESH Runner.unity, whose own RunnerSceneBootstrap.
    /// OnEnable() re-applies GameSession.SelectedSong.Clip and re-publishes GameSession.Profile via
    /// SongProfileReadyEvent — the EXACT same bootstrap a normal Song Selection -> Play entry uses,
    /// never duplicated here. A genuinely new run of the same, already-analyzed song. Does NOT
    /// consume a life.
    ///
    /// VISUAL: this path skips SongAnalysis/AnalyzingScreen — which is exactly what leaves it with no
    /// full-screen cover of its own for the async Runner scene load, unlike a normal Play entry
    /// (AnalyzingScreenController's own doc explains it stays up through that entire gap for THAT
    /// path). Hiding this screen immediately here would expose several raw frames of the Fight arena/
    /// stripped HUD/badly-placed camera underneath while Runner loads — so instead THIS screen stays
    /// fully visible and becomes the cover for that gap: buttons are disabled (no double-click
    /// firing a second transition) and Hide() is deferred to _onGameStarted, the same real "Runner is
    /// fully ready" signal (GameStartedEvent — GameplayManager only publishes it once the level is
    /// generated AND the player/camera are correctly placed) AnalyzingScreenController already
    /// reuses for this exact purpose — never an arbitrary WaitForSeconds timer.
    /// </summary>
    private void OnReplaySongClicked()
    {
        if (_awaitingReplaySongReady) return; // already in flight — ignore a stray extra click
        Debug.Log("[MatchResultController] Replay Song clicked -> requesting GameFlowState.Gameplay directly (SongAnalysis is never requested here); this screen stays visible until GameStartedEvent.");

        SetReplaySongButtonsInteractable(false);
        _awaitingReplaySongReady = true;
        FightMusicController.Instance?.Stop();
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.Gameplay);
    }

    private void OnMainMenuClicked()
    {
        FightMusicController.Instance?.Stop();
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
    }

    private void ShowRewardAdModal()
    {
        if (_rewardAdModalRoot != null) _rewardAdModalRoot.SetActive(true);
    }

    private void HideRewardAdModal()
    {
        if (_rewardAdModalRoot != null) _rewardAdModalRoot.SetActive(false);
    }

    private void OnWatchAdClicked()
    {
        _adService.ShowRewardedAd(success =>
        {
            HideRewardAdModal();
            if (!success)
            {
                Debug.Log("[MatchResultController] Rewarded Ad not granted — staying on Match Lost.");
                return;
            }

            // "+1 retry/life -> consumeix-la / inicia Fight Again" — granted and immediately spent,
            // see this phase's own explicit Rewarded Ad flow requirement.
            if (GameSession.Instance?.FightResources != null) GameSession.Instance.FightResources.ExtraLives++;
            BeginFightAgain();
        });
    }

    private void OnCancelAdClicked() => HideRewardAdModal();
}
