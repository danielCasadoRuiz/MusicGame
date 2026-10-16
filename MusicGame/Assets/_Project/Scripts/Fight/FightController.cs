using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Fight HUD — a Street-Fighter-style top bar (player name+health on the left, a countdown timer
/// in the middle, opponent name+health on the right, a pause button at the far right) plus a pause
/// menu (Resume / Main Menu), reachable from the pause button OR the Esc key.
///
/// SHOWS ITSELF the instant FightFlowState.RoundIntro begins (right as the VS cartela ends) and stays
/// visible, unchanged, all the way through Countdown and into Fighting — it is NOT a separate screen
/// that pops in when Fighting starts. RoundIntroController's own "ROUND {n}"/3-2-1/FIGHT! sequence
/// (plus its one dark overlay — see that class's own doc) renders ON TOP of this already-visible HUD
/// for readability; when that sequence ends there is nothing left to reveal, so no second fade/reveal
/// happens here (a previous version of this class owned its own redundant transition overlay — removed,
/// see RoundIntroController's own doc on why there is now exactly ONE curtain for the whole reveal).
///
/// The interactive controls (mobile joystick/Punch/Kick) are the ONE part of this HUD that stays
/// hidden/inactive until FightFlowState.Fighting actually begins — see ActivateControls(). Real
/// keyboard/gamepad input is separately (and already correctly) gated to Fighting by
/// FighterInputController itself; this class never duplicates that gate.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController): it only ever talks to
/// GameSession.Instance/ThemeManager.Instance/AppBootstrap.Context/EventBus, never to anything
/// scene-local, so it stays correct regardless of which Mode Scene is currently active underneath it.
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
/// Structure and look: FightHud.prefab (UIPrefabLibrarySO.fightHud — top bar, portraits, camera toggle,
/// pause menu, mobile controls), instantiated once in Awake; this class only fills data. A missing
/// prefab is logged by UIPrefabLibrarySO.Spawn and disables this controller — no procedural fallback.
/// </summary>
public class FightController : MonoBehaviour
{
    private FightFlowConfig _flowConfig; // roundDuration — see Awake()

    private FightHudView _view;
    private RectTransform _root;
    private TextMeshProUGUI _timerText;
    private TextMeshProUGUI _playerNameText, _opponentNameText;
    private Image _playerHealthFill, _opponentHealthFill;
    private TextMeshProUGUI _playerRoundPipsText, _opponentRoundPipsText;

    private RectTransform _pausePanel;
    private bool _paused;

    /// <summary>True while the fight pause menu is open: Time.timeScale = 0 (fighters, round timer,
    /// round/intro sequences frozen), fight music paused, fighter input + AI ignored.</summary>
    public static bool IsPaused { get; private set; }
    private bool _active;

    // Only ever READ here — see class doc: FightController presents combat data, never decides it.
    // Found once per Fighting-activation (Fight.unity's FighterActors don't exist yet at Awake
    // time, since that scene loads separately) via the same cross-scene FindObjectsByType pattern
    // FightDebugHUD already uses.
    private FighterHealth _playerHealth;
    private FighterHealth _opponentHealth;

    // Joystick (movement) + Punch/Kick — visible ONLY while _active (FightFlowState.Fighting) AND
    // PlatformService.IsMobile, same platform-gating convention as Runner's own
    // MobileControlsController, just baked into THIS prefab instead of built standalone (see
    // FightHudView's own doc on why: Section 10 asked for "amplia el FightHud actual", not a
    // second procedural-only UI class).
    private RectTransform _mobileControlsRoot;

    private System.Action<GameFlowStateChangedEvent>  _onFlowStateChanged;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _flowConfig = appConfig != null ? appConfig.fightFlow : null;

        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.fightHud : null, UIFactory.RootCanvas(), "FightHud");
        if (_view == null) { enabled = false; return; }
        UIFactory.Stretch((RectTransform)_view.transform);
        WireUI(_view);
    }

    private void OnDestroy()
    {
        if (_view != null) Destroy(_view.gameObject);
    }

    // ── Portraits beside each fighter's name / health (the SAME sprites the VS screen shows) ─────
    private Image _playerPortrait, _opponentPortrait;
    private PortraitCrop _playerPortraitCrop, _opponentPortraitCrop;
    private Color _portraitPlaceholderColor = new(1f, 1f, 1f, 0.2f);

    // Face-centred crop when the slot has one (FightHud.prefab's PortraitCrop); plain fit otherwise.
    private void SetPortrait(PortraitCrop crop, Image img, Sprite sprite)
    {
        if (crop != null) { crop.Show(sprite, _portraitPlaceholderColor); return; }
        if (img == null) return;
        img.sprite = sprite;
        img.color = sprite != null ? Color.white : _portraitPlaceholderColor; // same neutral placeholder as the VS screen
    }

    // ── Camera view toggle (presentation only — FightCameraController.ToggleView) ─────────────
    private TMPro.TextMeshProUGUI _cameraToggleLabel;
    private CameraViewMode? _shownCameraMode;

    private void RefreshCameraToggle()
    {
        var mode = FightCameraController.Instance != null ? FightCameraController.Instance.ViewMode : CameraViewMode.ThirdPerson;
        if (_shownCameraMode == mode || _cameraToggleLabel == null) return;
        _shownCameraMode = mode;
        // The label names the perspective the button SWITCHES TO (the action), not the current one.
        Loc.Set(_cameraToggleLabel, mode == CameraViewMode.ThirdPerson ? "Pause.FirstPerson" : "Pause.ThirdPerson");
    }

    private void OnEnable()
    {
        // Only cleanup happens directly on GameFlowState.
        _onFlowStateChanged = e =>
        {
            if (e.Previous == GameFlowState.Fight) ExitFight();
        };
        _onFightFlowChanged = e =>
        {
            // The HUD itself (top bar, live-updating) shows the instant RoundIntro begins — see
            // class doc. Only the interactive controls wait for the real Fighting state.
            if (e.Current == FightFlowState.RoundIntro) ShowHud();
            else if (e.Current == FightFlowState.Fighting) ActivateControls();
            // Fight Again re-enters the in-arena Versus with the HUD still up from the last match:
            // hide it again so the presentation (card, camera pass, bubble) is clean.
            else if (e.Current == FightFlowState.VersusIntro) HideHudForPresentation();
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
        // Esc pauses only while rounds are being played. In VersusIntro / MatchFarewell ESC belongs to
        // the presentation's Skip (FightPresentationSkip) — never both; on the result screen it does nothing.
        var fightState = FightFlowController.Instance != null ? FightFlowController.Instance.CurrentState : FightFlowState.Fighting;
        bool pausable = fightState == FightFlowState.RoundIntro || fightState == FightFlowState.Countdown ||
                        fightState == FightFlowState.Fighting || fightState == FightFlowState.RoundEnd;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && (pausable || _paused))
            SetPaused(!_paused);
        // V = the same First/Third Person toggle as the HUD button (one method: FightCameraController.ToggleView).
        if (kb != null && kb.vKey.wasPressedThisFrame && !_paused)
            FightCameraController.Instance?.ToggleView();

        RefreshCameraToggle();
        if (_paused) return;

        UpdateHealthBars();
        UpdateRoundPips();
        UpdateTimerText();
    }

    private void UpdateHealthBars()
    {
        if (_playerHealth == null || _opponentHealth == null) FindHealthRefs();

        if (_playerHealth != null)
            _playerHealthFill.fillAmount = _playerHealth.MaxHealth > 0f ? _playerHealth.CurrentHealth / _playerHealth.MaxHealth : 0f;
        if (_opponentHealth != null)
            _opponentHealthFill.fillAmount = _opponentHealth.MaxHealth > 0f ? _opponentHealth.CurrentHealth / _opponentHealth.MaxHealth : 0f;
    }

    private void FindHealthRefs()
    {
        foreach (var actor in FindObjectsByType<FighterActor>(FindObjectsSortMode.None))
        {
            if (actor.Side == FighterSide.Player) _playerHealth = actor.Health;
            else _opponentHealth = actor.Health;
        }
    }

    // Simple text "pips" (●●○) — see FightHudView's own doc. maxRounds is informational display
    // only (see FightFlowConfig.maxRounds' own doc); the actual match-winning rule is roundsToWin.
    private void UpdateRoundPips()
    {
        if (_playerRoundPipsText == null || _opponentRoundPipsText == null) return;
        var match = FightMatchController.Instance;
        if (match == null) return;

        int slots = _flowConfig != null ? Mathf.Max(1, _flowConfig.maxRounds) : 3;
        _playerRoundPipsText.text   = PipsText(match.PlayerRoundsWon, slots);
        _opponentRoundPipsText.text = PipsText(match.OpponentRoundsWon, slots);
    }

    private static string PipsText(int won, int slots)
    {
        won = Mathf.Clamp(won, 0, slots);
        return new string('●', won) + new string('○', slots - won); // ● / ○
    }

    // Shows the top bar (health/timer/round pips) the instant RoundIntro begins — see class doc.
    // Interactive controls stay hidden until ActivateControls() (real Fighting).
    private void ShowHud()
    {
        PopulateInfo();
        FindHealthRefs();
        UpdateTimerText();
        UpdateRoundPips();
        SetPaused(false);
        _active = true;
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling(); // RoundIntroController re-asserts its OWN root on top of this right after — see its own doc
    }

    private void HideHudForPresentation()
    {
        if (_paused) SetPaused(false);
        _active = false;
        if (_root != null) _root.gameObject.SetActive(false);
        if (_mobileControlsRoot != null) _mobileControlsRoot.gameObject.SetActive(false);
    }

    // The ONE thing that still waits for the real FightFlowState.Fighting — see class doc.
    private void ActivateControls()
    {
        if (_mobileControlsRoot != null)
            _mobileControlsRoot.gameObject.SetActive(PlatformService.IsMobile);
    }

    private void ExitFight()
    {
        // Leaving Fight while paused: never carry timeScale 0 / a paused song into the next screen.
        if (_paused)
        {
            FightMusicController.Instance?.Stop();
            SetPaused(false);
        }
        _active = false;
        if (_root != null) _root.gameObject.SetActive(false);
        if (_mobileControlsRoot != null) _mobileControlsRoot.gameObject.SetActive(false);

        // Fight.unity (and every FighterActor in it) unloads on exit — stale references would
        // otherwise survive into a fresh Fight entry until FindHealthRefs happens to be called again.
        _playerHealth = null;
        _opponentHealth = null;
    }

    // ── Prefab wiring — see FightHudView's own doc ───────────────────────────────

    private void WireUI(FightHudView view)
    {
        _root               = view.root.GetComponent<RectTransform>();
        _playerNameText     = view.playerNameText;
        _opponentNameText   = view.opponentNameText;
        _playerHealthFill   = UIFactory.EnsureFillSprite(view.playerHealthFill);
        _opponentHealthFill = UIFactory.EnsureFillSprite(view.opponentHealthFill);
        _playerRoundPipsText   = view.playerRoundPipsText;
        _opponentRoundPipsText = view.opponentRoundPipsText;
        _timerText          = view.timerText;
        _pausePanel         = view.pausePanel.GetComponent<RectTransform>();
        _playerPortrait     = view.playerPortrait;
        _opponentPortrait   = view.opponentPortrait;
        _playerPortraitCrop   = view.playerPortraitCrop;
        _opponentPortraitCrop = view.opponentPortraitCrop;
        _portraitPlaceholderColor = view.portraitPlaceholderColor;
        _cameraToggleLabel  = view.cameraToggleLabel;

        view.pauseButton.onClick.AddListener(() => SetPaused(!_paused));
        view.resumeButton.onClick.AddListener(() => SetPaused(false));
        view.mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        if (view.cameraToggleButton != null)
            view.cameraToggleButton.onClick.AddListener(() => { if (FightCameraController.Instance != null) FightCameraController.Instance.ToggleView(); });

        // Static labels follow the language through their LocalizedText. OpponentName is data
        // (overwritten by every PopulateInfo()), so its placeholder is set — and tracked — here.
        Loc.Set(view.opponentNameText, "Fight.RivalUnknown");

        _mobileControlsRoot = view.mobileControlsRoot.GetComponent<RectTransform>();
        WireMobileControls(view);
        _mobileControlsRoot.gameObject.SetActive(false);

        _pausePanel.gameObject.SetActive(false);
        _root.gameObject.SetActive(false);
    }

    // Wires the prefab's joystick + Punch/Kick TouchActionButtons to FightTouchInputState. Reuses
    // VirtualJoystick/a TouchActionButton per instance — see their own doc on why this is "reuse the
    // component, wire a new instance" rather than either duplicating the drag math or driving
    // Runner's own TouchInputState from Fight.
    private static void WireMobileControls(FightHudView view)
    {
        if (view.joystick != null)
            view.joystick.Initialize(view.joystickBackground, view.joystickHandle,
                v => { FightTouchInputState.Horizontal = v.x; FightTouchInputState.Vertical = v.y; },
                driveTouchInputState: false);
        else Debug.LogError("[FightController] FightHud.prefab: joystick (VirtualJoystick) not assigned.");

        if (view.punchAction != null)
            view.punchAction.Initialize(
                () => { FightTouchInputState.PunchRequested = true; FightTouchInputState.PunchHeld = true; },
                () => FightTouchInputState.PunchHeld = false);
        else Debug.LogError("[FightController] FightHud.prefab: punchAction (TouchActionButton) not assigned.");

        if (view.kickAction != null)
            view.kickAction.Initialize(
                () => { FightTouchInputState.KickRequested = true; FightTouchInputState.KickHeld = true; },
                () => FightTouchInputState.KickHeld = false);
        else Debug.LogError("[FightController] FightHud.prefab: kickAction (TouchActionButton) not assigned.");
    }

    // A REAL pause (it used to only show the panel while the match kept running underneath).
    private void SetPaused(bool paused)
    {
        bool changed = _paused != paused;
        _paused = paused;
        IsPaused = paused;
        if (_pausePanel != null) _pausePanel.gameObject.SetActive(paused);
        if (!changed) return;
        Time.timeScale = paused ? 0f : 1f;
        FightMusicController.Instance?.SetPaused(paused);
        Debug.Log($"[FightController] Fight {(paused ? "PAUSED" : "resumed")}.");
    }

    private void PopulateInfo()
    {
        // The real pick from OpponentSelectionController's roulette (see GameSession.SelectedOpponent's
        // own doc) — null only when Fight is reached directly for debugging, without a real
        // Opponent Selection pass (see FightSceneBootstrap's own tolerant logging for that case).
        var opponent = GameSession.Instance?.SelectedOpponent;
        _opponentNameText.text = opponent != null ? opponent.displayName : Loc.Get("Fight.RivalUnknown");
        // Same sources as VersusScreenController: the committed rival version's portrait; the player has
        // no portrait art yet (neutral placeholder, exactly like the VS screen).
        SetPortrait(_opponentPortraitCrop, _opponentPortrait, GameSession.Instance?.SelectedOpponentLevelConfig?.portrait);
        SetPortrait(_playerPortraitCrop, _playerPortrait, null);

        _playerHealthFill.fillAmount   = 1f;
        _opponentHealthFill.fillAmount = 1f;
    }

    // Reads FightMatchController's own timer — never maintains its own (see class doc: presents
    // data, never decides/owns it). Shown in plain seconds ("45 s", Fight.TimerSeconds) — rounds are
    // short, a minutes:seconds clock read as minutes. Re-localized only when the value changes.
    private int _shownTimerSeconds = -1;

    private void UpdateTimerText()
    {
        float remaining = FightMatchController.Instance != null ? FightMatchController.Instance.RoundTimeRemaining
            : (_flowConfig != null ? _flowConfig.roundDuration : 60f);
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(remaining));
        if (totalSeconds == _shownTimerSeconds) return;
        _shownTimerSeconds = totalSeconds;
        Loc.Set(_timerText, "Fight.TimerSeconds", totalSeconds.ToString());
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
        // Quitting mid-fight ENDS the match (no result, the song stays undefended — its pendingRun is
        // kept so CONTINUE resumes it): stop the match + music BEFORE unpausing, so nothing resumes.
        FightMatchController.Instance?.AbortMatch("main menu");
        FightMusicController.Instance?.Stop();
        SetPaused(false);
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
    }
}
