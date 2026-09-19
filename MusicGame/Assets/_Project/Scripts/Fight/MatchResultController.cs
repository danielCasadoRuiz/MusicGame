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
            // hide so a stale result never lingers behind the next Opponent Selection.
            else if (e.Current == FightFlowState.OpponentSelection) Hide();
        };
        EventBus.Subscribe(_onMatchEnded);
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onMatchEnded);
        EventBus.Unsubscribe(_onFightFlowChanged);
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
        return reason + "\n" + Loc.Get("MatchResult.FinalHealth", playerHp.ToString(), opponentHp.ToString());
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

    private void OnReplaySongClicked()
    {
        Debug.Log("[MatchResultController] Replay Song clicked -> requesting GameFlowState.Gameplay directly (SongAnalysis is never requested here).");
        Hide();
        FightMusicController.Instance?.Stop();
        // GameSession.SelectedSong/Profile already hold the exact song + analysis that led to this
        // Fight — nothing overwrites them during a Fight session — so this goes STRAIGHT to
        // GameFlowState.Gameplay, never through SongAnalysis/AnalyzingScreen (task's own explicit
        // "no vull veure aquella pantalla en absolut" requirement, NOT satisfied merely by relying on
        // a cache hit being fast — see class doc). SceneFlowController.LoadMode(Runner) then loads a
        // completely FRESH Runner.unity, whose own RunnerSceneBootstrap.OnEnable() re-applies
        // GameSession.SelectedSong.Clip and re-publishes GameSession.Profile via
        // SongProfileReadyEvent — the EXACT same bootstrap a normal Song Selection -> Play entry
        // uses, never duplicated here. Every run-scoped counter/timeline/player-position/MusicClock/
        // HUD state is a brand new instance by construction (a full scene reload, not an in-place
        // reset) — a genuinely new run of the same, already-analyzed song. Does NOT consume a life.
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
