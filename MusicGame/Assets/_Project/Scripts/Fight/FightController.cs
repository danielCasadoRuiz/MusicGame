using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Fight HUD — a Street-Fighter-style top bar (player name+health on the left, a countdown timer
/// in the middle, opponent name+health on the right, a pause button at the far right) plus a pause
/// menu (Resume / Main Menu), reachable from the pause button OR the Esc key. Only becomes ACTIVE
/// once Fight's own internal sequence (see FightFlowController/FightFlowState) reaches Fighting —
/// Opponent Selection/Versus/Round Intro/Countdown each render their own full-screen UI first (see
/// OpponentSelectionController/VersusScreenController/RoundIntroController), so there's nothing for
/// this HUD to show before then. Lives in the always-loaded UI Scene (added by UIFlowController):
/// it only ever talks to GameSession.Instance/ThemeManager.Instance/AppBootstrap.Context/EventBus,
/// never to anything scene-local, so it stays correct regardless of which Mode Scene is currently
/// active underneath it.
///
/// PLACEHOLDER, not real fight mechanics: health bars start full and never change (no damage system
/// exists yet), the timer counts down but nothing happens at zero yet — this only proves the HUD
/// shell and its data wiring, matching FightSceneBootstrap's own "architectural shell" scope for
/// the 3D placeholders. The opponent's name IS real now (GameSession.SelectedOpponent, chosen by
/// OpponentSelectionController's roulette), unlike the earlier style-name placeholder.
///
/// The 3D placeholder content (arena, player/enemy placeholders, camera) is NOT here — that lives in
/// FightSceneBootstrap, in the real Fight Mode Scene (Fight.unity), loaded/unloaded by
/// SceneFlowController the instant GameFlowState enters/leaves Fight. This class only owns the
/// 2D overlay; it doesn't know Fight.unity exists.
///
/// Prefers a real FightHud.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to the old procedural build only if that
/// hasn't been run yet, same pattern as GameplayHUD/PauseController.
/// </summary>
public class FightController : MonoBehaviour
{
    private FightFlowConfig _flowConfig; // roundDuration — see Awake()

    private RectTransform _root;
    private TextMeshProUGUI _timerText;
    private TextMeshProUGUI _playerNameText, _opponentNameText;
    private Image _playerHealthFill, _opponentHealthFill;

    private RectTransform _pausePanel;
    private bool _paused;
    private bool _active;
    private float _timeRemaining;

    // Joystick (movement) + Punch/Kick — visible ONLY while _active (FightFlowState.Fighting) AND
    // PlatformService.IsMobile, same platform-gating convention as Runner's own
    // MobileControlsController, just baked into THIS prefab instead of built standalone (see
    // FightHudView's own doc on why: Section 10 asked for "amplia el FightHud actual", not a
    // second procedural-only UI class).
    private RectTransform _mobileControlsRoot;

    // Masks whatever's left of the arena/camera the instant this HUD actually reveals itself —
    // by the time ActivateHud() runs (FightFlowState.Fighting, well after Opponent Selection/
    // Versus/Round Intro/Countdown have already been covering the screen for a while),
    // Fight.unity's own camera swap (FightSceneBootstrap) happened long ago with nothing left to
    // glitch, but a brief fade-from-black still makes the "FIGHT!" -> live arena reveal itself
    // read as a deliberate beat instead of a hard cut. Lives HERE (the always-loaded UI Scene),
    // not inside Fight.unity itself, so it renders via the persistent UI Canvas regardless of
    // Fight.unity's own load state.
    private const float FightTransitionFadeSeconds = 0.4f;
    private Image      _transitionOverlay;
    private Coroutine  _transitionRoutine;

    private System.Action<GameFlowStateChangedEvent>  _onFlowStateChanged;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _flowConfig = appConfig != null ? appConfig.fightFlow : null;

        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.FightHud != null) WireUI(registry.FightHud);
        else Build();
    }

    private void OnEnable()
    {
        // Only cleanup happens directly on GameFlowState — the HUD itself only ever becomes
        // active once Fight's OWN internal sequence (see FightFlowController/FightFlowState)
        // actually reaches Fighting: Opponent Selection/Versus/Round Intro/Countdown all render
        // as their own full-screen UI first, so there's nothing for this HUD to show before then.
        _onFlowStateChanged = e =>
        {
            if (e.Previous == GameFlowState.Fight) ExitFight();
        };
        _onFightFlowChanged = e =>
        {
            if (e.Current == FightFlowState.Fighting) ActivateHud();
        };
        EventBus.Subscribe(_onFlowStateChanged);
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
        EventBus.Unsubscribe(_onFightFlowChanged);
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

    private void ActivateHud()
    {
        PopulateInfo();
        _timeRemaining = _flowConfig != null ? _flowConfig.roundDuration : 60f;
        UpdateTimerText();
        SetPaused(false);
        _active = true;
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();

        if (_mobileControlsRoot != null)
            _mobileControlsRoot.gameObject.SetActive(PlatformService.IsMobile);

        if (_transitionOverlay != null)
        {
            _transitionOverlay.gameObject.SetActive(true);
            _transitionOverlay.transform.SetAsLastSibling();
            _transitionOverlay.color = new Color(0f, 0f, 0f, 1f);
            if (_transitionRoutine != null) StopCoroutine(_transitionRoutine);
            _transitionRoutine = StartCoroutine(FadeOutTransitionOverlay());
        }
    }

    // Held fully opaque for one frame before fading — gives _root's own newly-activated content
    // (top bar, health bars) one frame to actually lay out/render before it's revealed.
    private IEnumerator FadeOutTransitionOverlay()
    {
        yield return null;

        float t = 0f;
        while (t < FightTransitionFadeSeconds)
        {
            t += Time.deltaTime;
            _transitionOverlay.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(t / FightTransitionFadeSeconds));
            yield return null;
        }
        _transitionOverlay.gameObject.SetActive(false);
        _transitionRoutine = null;
    }

    private void ExitFight()
    {
        _active = false;
        if (_root != null) _root.gameObject.SetActive(false);
        if (_mobileControlsRoot != null) _mobileControlsRoot.gameObject.SetActive(false);

        if (_transitionRoutine != null) { StopCoroutine(_transitionRoutine); _transitionRoutine = null; }
        if (_transitionOverlay != null) _transitionOverlay.gameObject.SetActive(false);
    }

    // ── Prefab path — see FightHudView's own doc ─────────────────────────────────

    private void WireUI(FightHudView view)
    {
        _root               = view.root.GetComponent<RectTransform>();
        _playerNameText     = view.playerNameText;
        _opponentNameText   = view.opponentNameText;
        _playerHealthFill   = view.playerHealthFill;
        _opponentHealthFill = view.opponentHealthFill;
        _timerText          = view.timerText;
        _pausePanel         = view.pausePanel.GetComponent<RectTransform>();
        _transitionOverlay  = view.transitionOverlay;
        if (_transitionOverlay != null) _transitionOverlay.gameObject.SetActive(false);

        view.pauseButton.onClick.AddListener(() => SetPaused(!_paused));
        view.resumeButton.onClick.AddListener(() => SetPaused(false));
        view.mainMenuButton.onClick.AddListener(OnMainMenuClicked);

        // Baked once at Editor-bake time — re-apply from the current locale here, same reasoning as
        // every other prefab-backed screen's labels. OpponentName gets overwritten by the very next
        // PopulateInfo() call regardless, but re-setting it here avoids a stale-locale flash before
        // that first call.
        view.playerNameText.text      = Loc.Get("Fight.PlayerName");
        view.opponentNameText.text    = Loc.Get("Fight.RivalUnknown");
        view.pauseTitleText.text      = Loc.Get("Fight.Paused");
        view.pauseButtonLabel.text    = Loc.Get("Fight.Pause");
        view.resumeButtonLabel.text   = Loc.Get("Fight.Resume");
        view.mainMenuButtonLabel.text = Loc.Get("Fight.MainMenu");

        _mobileControlsRoot = view.mobileControlsRoot.GetComponent<RectTransform>();
        WireMobileControls(view.joystickBackground, view.joystickHandle, view.punchButton, view.kickButton);
        if (view.punchButtonLabel != null) view.punchButtonLabel.text = Loc.Get("Mobile.Punch");
        if (view.kickButtonLabel  != null) view.kickButtonLabel.text  = Loc.Get("Mobile.Kick");
        _mobileControlsRoot.gameObject.SetActive(false);

        _pausePanel.gameObject.SetActive(false);
        _root.gameObject.SetActive(false);
    }

    // Wires an already-built joystick background/handle + Punch/Kick button pair to
    // FightTouchInputState — shared by both the prefab path (WireUI, everything already built)
    // and the procedural fallback (BuildMobileControls, built here first). Reuses VirtualJoystick/
    // a fresh TouchActionButton per instance — see their own doc on why this is "reuse the
    // component, wire a new instance" rather than either duplicating the drag math or driving
    // Runner's own TouchInputState from Fight.
    private static void WireMobileControls(RectTransform joystickBackground, RectTransform joystickHandle,
        GameObject punchButton, GameObject kickButton)
    {
        var joystick = joystickBackground.gameObject.AddComponent<VirtualJoystick>();
        joystick.Initialize(joystickBackground, joystickHandle,
            v => { FightTouchInputState.Horizontal = v.x; FightTouchInputState.Vertical = v.y; },
            driveTouchInputState: false);

        punchButton.AddComponent<TouchActionButton>().Initialize(() => FightTouchInputState.PunchRequested = true);
        kickButton.AddComponent<TouchActionButton>().Initialize(() => FightTouchInputState.KickRequested = true);
    }

    // ── UI shell (procedural fallback — no UIRegistry in the scene yet) ──────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("FightScreen", canvas);
        UIFactory.Stretch(_root);

        BuildTopBar();
        BuildPauseMenu();
        BuildMobileControls();
        BuildTransitionOverlay();

        _root.gameObject.SetActive(false);
    }

    private void BuildMobileControls()
    {
        _mobileControlsRoot = UIFactory.CreateRect("MobileControls", _root);
        UIFactory.Stretch(_mobileControlsRoot);

        const float joySize = 180f;
        var background = UIFactory.CreatePanel("JoystickBackground", _mobileControlsRoot, new Color(1f, 1f, 1f, 0.15f));
        UIFactory.SetBox(background.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(40f, 40f), new Vector2(joySize, joySize));
        var handle = UIFactory.CreatePanel("JoystickHandle", background.rectTransform, new Color(1f, 1f, 1f, 0.4f));
        handle.rectTransform.sizeDelta        = new Vector2(joySize * 0.45f, joySize * 0.45f);
        handle.rectTransform.anchoredPosition = Vector2.zero;

        var punchGO = BuildActionButtonPanel("PunchButton", -170f, "Mobile.Punch");
        var kickGO  = BuildActionButtonPanel("KickButton",  -40f,  "Mobile.Kick");

        WireMobileControls(background.rectTransform, handle.rectTransform, punchGO, kickGO);

        _mobileControlsRoot.gameObject.SetActive(false);
    }

    private GameObject BuildActionButtonPanel(string name, float xOffset, string locKey)
    {
        const float size = 110f;
        var button = UIFactory.CreatePanel(name, _mobileControlsRoot, new Color(1f, 1f, 1f, 0.25f));
        UIFactory.SetBox(button.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(xOffset, 40f), new Vector2(size, size));

        var label = UIFactory.CreateText("Label", button.rectTransform, Loc.Get(locKey), 16, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);

        return button.gameObject;
    }

    private void BuildTransitionOverlay()
    {
        var overlay = UIFactory.CreatePanel("TransitionOverlay", _root, new Color(0f, 0f, 0f, 1f));
        UIFactory.Stretch(overlay.rectTransform);
        _transitionOverlay = overlay;
        overlay.gameObject.SetActive(false);
    }

    private void BuildTopBar()
    {
        var topBar = UIFactory.CreatePanel("TopBar", _root, new Color(0.02f, 0.02f, 0.05f, 0.55f));
        UIFactory.SetBox(topBar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 90f));
        topBar.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        // Player — left.
        _playerNameText = UIFactory.CreateText("PlayerName", topBar.rectTransform, Loc.Get("Fight.PlayerName"), 18, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        UIFactory.SetBox(_playerNameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(20f, -12f), new Vector2(320f, 24f));
        _playerNameText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);

        var playerHealthBg = UIFactory.CreateFillBar("PlayerHealth", topBar.rectTransform, new Color(0.12f, 0.12f, 0.12f), new Color(0.3f, 0.85f, 0.3f), out _playerHealthFill);
        UIFactory.SetBox(playerHealthBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(20f, -42f), new Vector2(320f, 18f));
        _playerHealthFill.fillAmount = 1f;
        // Health = Positive/Negative tokens, not just "some color" — the player's own bar drains
        // green, the rival's drains red, in every Theme.
        _playerHealthFill.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Positive);

        // Opponent — right (leaves room for the pause button at the far right). Same key as the
        // "unknown style" case in PopulateInfo() — this default is only ever visible for a moment
        // before the first PopulateInfo() call overwrites it, so it doesn't earn its own key.
        _opponentNameText = UIFactory.CreateText("OpponentName", topBar.rectTransform, Loc.Get("Fight.RivalUnknown"), 18, Color.white, TextAlignmentOptions.TopRight, FontStyles.Bold);
        UIFactory.SetBox(_opponentNameText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-90f, -12f), new Vector2(320f, 24f));
        _opponentNameText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);

        var opponentHealthBg = UIFactory.CreateFillBar("OpponentHealth", topBar.rectTransform, new Color(0.12f, 0.12f, 0.12f), new Color(0.9f, 0.3f, 0.25f), out _opponentHealthFill);
        UIFactory.SetBox(opponentHealthBg.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-90f, -42f), new Vector2(320f, 18f));
        _opponentHealthFill.fillAmount = 1f;
        _opponentHealthFill.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Negative);
        // Health bars fill from the LEFT by default (UIFactory.CreateFillBar) — mirror the
        // opponent's so it visibly drains toward its own name, like the player's does.
        _opponentHealthFill.fillOrigin = (int)Image.OriginHorizontal.Right;

        // Timer — center.
        _timerText = UIFactory.CreateText("Timer", topBar.rectTransform, "", 28, Color.white, TextAlignmentOptions.Top, FontStyles.Bold);
        UIFactory.SetBox(_timerText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -12f), new Vector2(140f, 36f));
        _timerText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        // Pause — far right.
        var pauseBtn = UIFactory.CreateButton("PauseButton", topBar.rectTransform, Loc.Get("Fight.Pause"), out var pauseLabel);
        pauseLabel.fontSize = 12;
        UIFactory.SetBox(pauseBtn.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-15f, -15f), new Vector2(60f, 60f));
        pauseBtn.onClick.AddListener(() => SetPaused(!_paused));
        pauseBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        pauseLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
    }

    private void BuildPauseMenu()
    {
        _pausePanel = UIFactory.CreateRect("PauseMenu", _root);
        UIFactory.Stretch(_pausePanel);

        var dim = UIFactory.CreatePanel("Dim", _pausePanel, new Color(0f, 0f, 0f, 0.75f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var title = UIFactory.CreateText("Title", _pausePanel, Loc.Get("Fight.Paused"), 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 60f), new Vector2(400f, 50f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        var resumeBtn = UIFactory.CreateButton("ResumeButton", _pausePanel, Loc.Get("Fight.Resume"), out var resumeLabel);
        UIFactory.SetBox(resumeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -10f), new Vector2(220f, 48f));
        resumeBtn.onClick.AddListener(() => SetPaused(false));
        resumeBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        resumeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);

        // The equivalent of the old top-level "Back" button — moved into the pause menu, since it's
        // an exit action, not something that belongs permanently on-screen during a match.
        var mainMenuBtn = UIFactory.CreateButton("MainMenuButton", _pausePanel, Loc.Get("Fight.MainMenu"), out var mainMenuLabel);
        UIFactory.SetBox(mainMenuBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -70f), new Vector2(220f, 48f));
        mainMenuBtn.onClick.AddListener(OnMainMenuClicked);
        mainMenuBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        mainMenuLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        _pausePanel.gameObject.SetActive(false);
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        _pausePanel.gameObject.SetActive(paused);
    }

    private void PopulateInfo()
    {
        // The real pick from OpponentSelectionController's roulette (see GameSession.SelectedOpponent's
        // own doc) — null only when Fight is reached directly for debugging, without a real
        // Opponent Selection pass (see FightSceneBootstrap's own tolerant logging for that case).
        var opponent = GameSession.Instance?.SelectedOpponent;
        _opponentNameText.text = opponent != null ? opponent.displayName : Loc.Get("Fight.RivalUnknown");

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
