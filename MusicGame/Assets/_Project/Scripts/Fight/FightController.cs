using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Fight HUD — a Street-Fighter-style top bar (player name+health on the left, a countdown timer
/// in the middle, opponent name+health on the right, a pause button at the far right) plus a pause
/// menu (Resume / Main Menu), reachable from the pause button OR the Esc key. Shown on top of
/// whatever's rendering underneath while GameFlowState is Fight. Lives in the always-loaded UI
/// Scene (added by UIFlowController): it only ever talks to GameSession.Instance/
/// ThemeManager.Instance/AppBootstrap.Context/EventBus, never to anything scene-local, so it stays
/// correct regardless of which Mode Scene is currently active underneath it.
///
/// PLACEHOLDER, not real fight mechanics: health bars start full and never change (no damage system
/// exists yet), the timer counts down but nothing happens at zero yet, and the opponent is just a
/// themed name derived from GameSession.DetectedMusicStyleId — this only proves the HUD shell and
/// its data wiring (Section 18 of the multi-scene refactor plan), matching FightSceneBootstrap's own
/// "architectural shell" scope for the 3D placeholders.
///
/// The 3D placeholder content (arena, player/enemy placeholders, camera) is NOT here — that lives in
/// FightSceneBootstrap, in the real Fight Mode Scene (Fight.unity), loaded/unloaded by
/// SceneFlowController the instant GameFlowState enters/leaves Fight. This class only owns the
/// 2D overlay; it doesn't know Fight.unity exists.
/// </summary>
public class FightController : MonoBehaviour
{
    private const float MatchDuration = 60f; // no real FightConfig yet — a single tunable value
                                              // doesn't earn one; promote this if Fight grows more.

    private RectTransform _root;
    private Text _timerText;
    private Text _playerNameText, _opponentNameText;
    private Image _playerHealthFill, _opponentHealthFill;

    private RectTransform _pausePanel;
    private bool _paused;
    private bool _active;
    private float _timeRemaining;

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;

    private void Awake() => Build();

    private void OnEnable()
    {
        _onFlowStateChanged = e =>
        {
            if (e.Current == GameFlowState.Fight) EnterFight();
            else if (e.Previous == GameFlowState.Fight) ExitFight();
        };
        EventBus.Subscribe(_onFlowStateChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
        ExitFight();
    }

    private void Update()
    {
        if (!_active) return;

        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            SetPaused(!_paused);

        if (_paused) return;

        _timeRemaining = Mathf.Max(0f, _timeRemaining - Time.deltaTime);
        UpdateTimerText();
        // TODO: real match-end logic (win/lose/draw) once actual fight mechanics exist — the timer
        // just holds at 0:00 for now.
    }

    private void EnterFight()
    {
        PopulateInfo();
        _timeRemaining = MatchDuration;
        UpdateTimerText();
        SetPaused(false);
        _active = true;
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
    }

    private void ExitFight()
    {
        _active = false;
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // ── UI shell (built at runtime via UIFactory, same pattern as AnalyzingScreenController) ─────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("FightScreen", canvas);
        UIFactory.Stretch(_root);

        BuildTopBar();
        BuildPauseMenu();

        _root.gameObject.SetActive(false);
    }

    private void BuildTopBar()
    {
        var topBar = UIFactory.CreatePanel("TopBar", _root, new Color(0.02f, 0.02f, 0.05f, 0.55f));
        UIFactory.SetBox(topBar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 90f));

        // Player — left.
        _playerNameText = UIFactory.CreateText("PlayerName", topBar.rectTransform, "PLAYER", 18, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
        UIFactory.SetBox(_playerNameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(20f, -12f), new Vector2(320f, 24f));

        var playerHealthBg = UIFactory.CreateFillBar("PlayerHealth", topBar.rectTransform, new Color(0.12f, 0.12f, 0.12f), new Color(0.3f, 0.85f, 0.3f), out _playerHealthFill);
        UIFactory.SetBox(playerHealthBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(20f, -42f), new Vector2(320f, 18f));
        _playerHealthFill.fillAmount = 1f;

        // Opponent — right (leaves room for the pause button at the far right).
        _opponentNameText = UIFactory.CreateText("OpponentName", topBar.rectTransform, "RIVAL", 18, Color.white, TextAnchor.UpperRight, FontStyle.Bold);
        UIFactory.SetBox(_opponentNameText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-90f, -12f), new Vector2(320f, 24f));

        var opponentHealthBg = UIFactory.CreateFillBar("OpponentHealth", topBar.rectTransform, new Color(0.12f, 0.12f, 0.12f), new Color(0.9f, 0.3f, 0.25f), out _opponentHealthFill);
        UIFactory.SetBox(opponentHealthBg.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-90f, -42f), new Vector2(320f, 18f));
        _opponentHealthFill.fillAmount = 1f;
        // Health bars fill from the LEFT by default (UIFactory.CreateFillBar) — mirror the
        // opponent's so it visibly drains toward its own name, like the player's does.
        _opponentHealthFill.fillOrigin = (int)Image.OriginHorizontal.Right;

        // Timer — center.
        _timerText = UIFactory.CreateText("Timer", topBar.rectTransform, "", 28, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
        UIFactory.SetBox(_timerText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -12f), new Vector2(140f, 36f));

        // Pause — far right.
        var pauseBtn = UIFactory.CreateButton("PauseButton", topBar.rectTransform, Loc.Get("Fight.Pause"), out var pauseLabel);
        pauseLabel.fontSize = 12;
        UIFactory.SetBox(pauseBtn.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-15f, -15f), new Vector2(60f, 60f));
        pauseBtn.onClick.AddListener(() => SetPaused(!_paused));
    }

    private void BuildPauseMenu()
    {
        _pausePanel = UIFactory.CreateRect("PauseMenu", _root);
        UIFactory.Stretch(_pausePanel);

        var dim = UIFactory.CreatePanel("Dim", _pausePanel, new Color(0f, 0f, 0f, 0.75f));
        UIFactory.Stretch(dim.rectTransform);

        var title = UIFactory.CreateText("Title", _pausePanel, Loc.Get("Fight.Paused"), 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 60f), new Vector2(400f, 50f));

        var resumeBtn = UIFactory.CreateButton("ResumeButton", _pausePanel, Loc.Get("Fight.Resume"), out _);
        UIFactory.SetBox(resumeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -10f), new Vector2(220f, 48f));
        resumeBtn.onClick.AddListener(() => SetPaused(false));

        // The equivalent of the old top-level "Back" button — moved into the pause menu, since it's
        // an exit action, not something that belongs permanently on-screen during a match.
        var mainMenuBtn = UIFactory.CreateButton("MainMenuButton", _pausePanel, Loc.Get("Fight.MainMenu"), out _);
        UIFactory.SetBox(mainMenuBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -70f), new Vector2(220f, 48f));
        mainMenuBtn.onClick.AddListener(OnMainMenuClicked);

        _pausePanel.gameObject.SetActive(false);
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        _pausePanel.gameObject.SetActive(paused);
    }

    private void PopulateInfo()
    {
        var session = GameSession.Instance;
        var style = session != null ? session.DetectedMusicStyleId : MusicStyleId.Unknown;
        _opponentNameText.text = style == MusicStyleId.Unknown
            ? Loc.Get("Fight.RivalUnknown")
            : Loc.Get("Fight.RivalNamed", style.ToString().ToUpperInvariant());

        _playerHealthFill.fillAmount   = 1f;
        _opponentHealthFill.fillAmount = 1f;
    }

    private void UpdateTimerText()
    {
        int totalSeconds = Mathf.CeilToInt(_timeRemaining);
        _timerText.text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private void OnMainMenuClicked()
    {
        // NOT GameFlowState.Results: Runner already fully unloaded the instant Fight replaced it
        // (see SceneFlowController) — its GameplayManager/GameplayHUD (which owned the results end-
        // screen) are gone. Requesting Results would just reload a completely FRESH Runner (a new
        // run starting from scratch), not bring back the run that just ended. MainMenu is the
        // correct, honest "you're done" destination until a real Results screen exists in the UI
        // Scene that reads GameSession.RunnerResults directly instead of depending on Runner being
        // alive.
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
    }
}
