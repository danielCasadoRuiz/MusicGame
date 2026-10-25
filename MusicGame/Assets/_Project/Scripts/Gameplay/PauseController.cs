using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Basic pause menu: RESUME, RESTART SONG and MAIN MENU, plus the live Pause and First/Third Person
/// buttons. The UI is the PauseMenu.prefab instance in Runner.unity (handed over by GameplayManager
/// from the scene's UIRegistry) — this class only wires PauseView and toggles visibility; it never
/// builds UI. A missing view is logged as an error and the menu simply stays off (no procedural
/// fallback). Real uGUI Button components instead of IMGUI's GUI.Button, which is what let the
/// old Restart button's click occasionally get eaten (GUIUtility.hotControl left stuck when a
/// button's branch stopped being drawn between MouseDown and MouseUp).
///
/// The music (AudioSource) is the temporal authority for gameplay (MusicClock.SongTime is
/// derived from it), so Time.timeScale = 0 alone is NOT enough to pause — the AudioSource
/// would keep playing (audioSource.time keeps advancing under timeScale=0) and SongTime
/// would drift away from what the player sees frozen on screen. Pause() therefore stops
/// three things explicitly and in this order:
///   1. Time.timeScale = 0   → freezes Update()-driven movement/spawn logic (Time.deltaTime = 0)
///   2. audioSource.Pause()  → actually stops song playback (timeScale does not touch this)
///   3. MusicClock.Pause()   → freezes SongTime/MusicDistance instead of letting its wall-clock
///                             fallback branch snap to a stale reading while audio is paused
/// Resume() reverses all three, in a way that resyncs cleanly (MusicClock.Resume() forces a
/// hard resync on the next tick instead of easing in from a frozen value).
///
/// Created dynamically by GameplayManager — call Initialize() after AddComponent.
/// </summary>
public class PauseController : MonoBehaviour
{
    public static PauseController Instance { get; private set; }

    private AudioSource     _audio;
    private GameplayManager _manager;

    private bool _paused;
    private bool _ended;

    public bool IsPaused => _paused;

    // Wired from Initialize() (NOT Awake()) — Awake() fires synchronously the instant
    // GameplayManager.Awake() calls AddComponent<PauseController>(), which is BEFORE
    // Initialize() supplies `_manager`; RefreshVisibility() (called from WireUI) needs it.
    private bool _built;

    public void Initialize(AudioSource audio, GameplayManager manager, PauseView view = null)
    {
        _audio   = audio;
        _manager = manager;

        if (_built) return;
        _built = true;

        // The prefab instance in Runner.unity (UIRegistry) — hand-tunable in the Prefab editor.
        // No procedural fallback: a missing view is logged and the pause UI stays off (Pause/Resume
        // logic itself still works).
        if (view != null) WireUI(view);
        else Debug.LogError("[PauseController] No PauseView wired (UIRegistry in Runner.unity missing/empty) — the pause menu is disabled.", this);
    }

    // ── uGUI refs ────────────────────────────────────────────────────────────
    private RectTransform _pauseButtonRoot;
    private RectTransform _overlayRoot;
    private RectTransform _cameraToggleButtonRoot;
    private Image         _cameraToggleIcon;
    private TextMeshProUGUI _cameraToggleLabel;
    private Sprite        _thirdPersonIcon;
    private Sprite        _firstPersonIcon;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private System.Action<GameEndedEvent>        _onEnd;
    private System.Action<PlayerRespawnedEvent>  _onRespawned;
    private System.Action<CameraViewChangedEvent> _onViewChanged;

    private void OnEnable()
    {
        // Refreshes the toggle button's icon/text regardless of what triggered the change (UI
        // click, the 'V' debug key, or anything else that ever calls CameraFollow.SetView) — the
        // button never talks to CameraFollow directly except to request a toggle.
        _onViewChanged = e => RefreshCameraToggleDisplay(e.Mode);
        EventBus.Subscribe(_onViewChanged);

        // An instance that never went through Initialize() (stray/duplicate component) has no UI
        // wired yet. LOG loudly instead of throwing — an uncaught exception here would abort the
        // REST of the event's subscribers too (multicast delegate invocation stops dead the
        // instant one target throws).
        _onEnd = e =>
        {
            if (!_built) { LogNotBuilt(); return; }
            // The run freezes exactly like a manual Pause the instant it ends — world/camera stop
            // dead instead of coasting to rest behind the end screen (Time.timeScale = 0 zeroes
            // every Time.deltaTime-based movement/smoothing everywhere, not just here). _ended is
            // set FIRST so RefreshVisibility() (called from Pause() below) keeps the "PAUSED" panel
            // and persistent buttons hidden even though _paused is now also true — only the caller
            // ended screen shows. Continue/RestartSong are what actually reset this afterward (see
            // GameplayHUD.OnContinueClicked and RestartSongFromPause/FallRespawnSystem) — Continue
            // leaves for a whole different Mode Scene (Fight), so it must not leave
            // Time.timeScale sitting at 0 for that scene to silently inherit.
            _ended = true;
            if (!_paused) Pause();
            RefreshVisibility();
        };
        // Restarting from the end screen (Restart) always publishes CheckpointIndex==0 — the
        // same "run began again from scratch" signal GameplayHUD uses to un-freeze itself.
        _onRespawned = e =>
        {
            if (!_built) { LogNotBuilt(); return; }
            if (e.CheckpointIndex == 0) { _ended = false; RefreshVisibility(); }
        };
        EventBus.Subscribe(_onEnd);
        EventBus.Subscribe(_onRespawned);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onEnd);
        EventBus.Unsubscribe(_onRespawned);
        EventBus.Unsubscribe(_onViewChanged);
    }

    private bool _loggedNotBuilt;

    private void LogNotBuilt()
    {
        if (_loggedNotBuilt) return;
        _loggedNotBuilt = true;
        Debug.LogError($"[PauseController] '{name}' received an event before Initialize() ran — " +
                       "likely a stray duplicate PauseController in the scene. It will stay inert.", this);
    }

    private void Update()
    {
        if (!_built) { LogNotBuilt(); return; }
        if (_ended) return;

        var kb = Keyboard.current;
        // While the help is open (or on the frame it closed) its own Escape handling wins: never also toggle the pause.
        bool helpOwnsInput = HelpScreenController.IsOpen || HelpScreenController.ClosedThisFrame;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && !helpOwnsInput)
        {
            if (_paused) Resume();
            else if (_manager != null && _manager.IsRunning) Pause();
        }

        RefreshVisibility();
    }

    /// <summary>
    /// Losing focus (Alt+Tab, clicking off the window, etc.) uses the EXACT same Pause() as
    /// ESC — otherwise SongTime keeps advancing via AudioSource while the window isn't even
    /// being rendered/updated normally, which can make it look like the song silently finished.
    /// Regaining focus does NOT auto-resume — Unity calls this on both directions, and
    /// auto-resuming on refocus would fight the player's own explicit Resume click with no way
    /// to tell the two apart; staying paused until they choose is the safe, unsurprising default.
    ///
    /// In the EDITOR specifically, this is disabled entirely: alt-tabbing to/from the Editor
    /// (to read a message, check a log, etc.) is constant during development and testing, and
    /// each focus loss silently pausing the run made it very easy to end up in a confusing
    /// "stuck paused" state without realizing it. Real builds keep the original behavior.
    /// </summary>
    private void OnApplicationFocus(bool hasFocus)
    {
#if UNITY_EDITOR
        return;
#else
        if (_ended || hasFocus) return;
        if (!_paused && _manager != null && _manager.IsRunning) Pause();
#endif
    }

    // ── Pause / Resume ────────────────────────────────────────────────────────

    public void Pause()
    {
        if (_paused) return;
        _paused = true;

        Time.timeScale = 0f;
        if (_audio != null) _audio.Pause();
        MusicClock.Instance?.Pause();
        GameAudio.Instance?.SetAnnouncerPaused(true);   // the countdown's voice freezes with the countdown

        RefreshVisibility();
    }

    public void Resume()
    {
        if (!_paused) return;
        _paused = false;

        if (HelpScreenController.IsOpen) HelpScreenController.Instance.Close();
        Time.timeScale = 1f;
        if (_audio != null) _audio.UnPause();
        MusicClock.Instance?.Resume();
        GameAudio.Instance?.SetAnnouncerPaused(false);

        RefreshVisibility();
    }

    // ── Help (bonuses): from the preRun tip card or from the pause menu — see HelpScreenController ──────

    /// <summary>The tip card's "More info": pause at once (the countdown and the warmup freeze exactly where they
    /// are) and open the bonuses straight away; closing it resumes directly, without the pause menu.</summary>
    public void OpenHelpFromTip()
    {
        if (_ended || _manager == null || !_manager.IsRunning || HelpScreenController.Instance == null) return;
        Pause();
        HelpScreenController.Instance.Open(HelpTopic.Bonuses, HelpOrigin.TipCard, OnHelpClosed);
        RefreshVisibility();
    }

    private void OpenHelpFromPause()
    {
        if (!_paused || HelpScreenController.Instance == null) return;
        HelpScreenController.Instance.Open(HelpTopic.Bonuses, HelpOrigin.PauseMenu, OnHelpClosed);
        RefreshVisibility();
    }

    private void OnHelpClosed(HelpOrigin origin)
    {
        if (origin == HelpOrigin.PauseMenu) { RefreshVisibility(); return; }   // back to the pause menu, still paused
        StartCoroutine(ResumeNextFrame());
    }

    // One frame later, so the key / click that closed the help is never also read as a gameplay input.
    private System.Collections.IEnumerator ResumeNextFrame()
    {
        yield return null;
        if (_paused && !HelpScreenController.IsOpen) Resume();
    }

    /// <summary>
    /// Just forwards to the shared restart request — FallRespawnSystem.RestartSongManuallyRoutine
    /// itself calls PauseController.Instance.Resume() before doing anything else, so unpausing
    /// is guaranteed regardless of which button (this one, or the end screen's) called in here.
    /// One place responsible for "make sure we're unpaused before restarting", not two.
    /// </summary>
    public void RestartSongFromPause() => _manager?.RequestRestartSong();

    /// <summary>
    /// Leaving Runner entirely for a different Mode Scene (Frontend/MainMenu) — same "you're done"
    /// exit Fight's own pause menu already offers (FightController.OnMainMenuClicked). Resume()
    /// first so Time.timeScale/audio/MusicClock don't stay frozen at 0 and silently carry over
    /// into whatever loads next (same reasoning as GameplayHUD.OnContinueClicked's own Resume()
    /// call before leaving to Fight).
    /// </summary>
    private void OnMainMenuClicked()
    {
        Resume();
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
    }

    // ── UI ────────────────────────────────────────────────────────────────────

    // Prefab path — see PauseView's own doc.
    private void WireUI(PauseView view)
    {
        _pauseButtonRoot = view.pauseButtonRoot.GetComponent<RectTransform>();
        _overlayRoot     = view.overlayRoot.GetComponent<RectTransform>();

        view.pauseButton.onClick.AddListener(Pause);
        view.resumeButton.onClick.AddListener(Resume);
        view.restartButton.onClick.AddListener(RestartSongFromPause);
        view.mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        if (view.bonusHelpButton != null) view.bonusHelpButton.onClick.AddListener(OpenHelpFromPause);
        else Debug.LogError("[PauseController] PauseView.bonusHelpButton not assigned — run Tools > MusicGame > UI > Help Placeholders + Prefabs (patches PauseMenu.prefab).", view);

        _cameraToggleButtonRoot = view.cameraToggleButtonRoot != null ? view.cameraToggleButtonRoot.GetComponent<RectTransform>() : null;
        _cameraToggleIcon       = view.cameraToggleIcon;
        _cameraToggleLabel      = view.cameraToggleLabel;
        _thirdPersonIcon        = view.thirdPersonIcon;
        _firstPersonIcon        = view.firstPersonIcon;
        // First/Third Person toggle — OUTSIDE the Runner's 44 px top bar, directly below the Pause button,
        // right-aligned (CameraToggleButton in PauseMenu.prefab). Button and V key share ToggleView().
        if (view.cameraToggleButton != null)
            view.cameraToggleButton.onClick.AddListener(() => CameraFollow.Instance?.ToggleView());
        else
            Debug.LogError("[PauseController] PauseView.cameraToggleButton not assigned — run Tools > MusicGame > UI > Build All UI Prefabs (patches PauseMenu.prefab).", view);
        RefreshCameraToggleDisplay(CameraFollow.Instance != null ? CameraFollow.Instance.ViewMode : CameraViewMode.ThirdPerson);

        // Same reasoning as GameplayHUD's own label re-set — baked once at Editor-bake time, so
        // re-apply from the current locale here instead of trusting the prefab's value.
        if (view.pauseButtonLabel   != null) Loc.Set(view.pauseButtonLabel, "Pause.PauseButton");
        if (view.titleText          != null) Loc.Set(view.titleText, "Pause.Title");
        if (view.resumeButtonLabel  != null) Loc.Set(view.resumeButtonLabel, "Pause.Resume");
        if (view.restartButtonLabel != null) Loc.Set(view.restartButtonLabel, "Pause.RestartSong");
        if (view.mainMenuButtonLabel != null) Loc.Set(view.mainMenuButtonLabel, "Pause.MainMenu");
        if (view.bonusHelpButtonLabel != null) Loc.Set(view.bonusHelpButtonLabel, "Help.Pause.Bonuses");

        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        if (_overlayRoot == null || _pauseButtonRoot == null) return; // no PauseView wired (already logged)
        _overlayRoot.gameObject.SetActive(!_ended && _paused && !HelpScreenController.IsOpen);

        bool showPersistentButtons = !_ended && !_paused && _manager != null && _manager.IsRunning;
        _pauseButtonRoot.gameObject.SetActive(showPersistentButtons);
        if (_cameraToggleButtonRoot != null) _cameraToggleButtonRoot.gameObject.SetActive(showPersistentButtons);
    }

    /// <summary>Purely cosmetic — the toggle button's icon/text name the perspective it SWITCHES TO
    /// (a call to action: "First Person" while playing in third person, "Third Person" while in first
    /// person — same rule as the Fight HUD's toggle). Never touches CameraFollow itself.</summary>
    private void RefreshCameraToggleDisplay(CameraViewMode mode)
    {
        bool isThird = mode == CameraViewMode.ThirdPerson;
        if (_cameraToggleLabel != null) Loc.Set(_cameraToggleLabel, isThird ? "Pause.FirstPerson" : "Pause.ThirdPerson");
        if (_cameraToggleIcon != null)
        {
            var sprite = isThird ? _firstPersonIcon : _thirdPersonIcon;
            _cameraToggleIcon.sprite = sprite;
            _cameraToggleIcon.enabled = sprite != null;
        }
    }
}
