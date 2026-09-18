using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Basic pause menu: RESUME and RESTART SONG only. Built with uGUI (Canvas/Button via
/// UIFactory) — real Button components instead of IMGUI's GUI.Button, which is what let the
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

    // Built from Initialize() (NOT Awake()) — Awake() fires synchronously the instant
    // GameplayManager.Awake() calls AddComponent<PauseController>(), which is BEFORE
    // Initialize() supplies `_manager`; RefreshVisibility() (called from BuildUI) needs it.
    private bool _built;

    public void Initialize(AudioSource audio, GameplayManager manager, PauseView view = null)
    {
        _audio   = audio;
        _manager = manager;

        if (_built) return;
        _built = true;

        // Prefer the prefab instance (Tools > MusicGame > Build UI Prefabs) — hand-tunable in the
        // Prefab editor. Fall back to the old procedural build only if that tool hasn't been run
        // yet — LOUDLY, so a broken/missing wiring is never silently invisible.
        if (view != null) WireUI(view);
        else { Debug.LogWarning("[PauseController] No PauseView wired (UIRegistry missing/empty) — building the pause menu procedurally instead."); BuildUI(); }
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
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
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
        if (_manager != null) _manager.SuppressSongEnd = true;

        RefreshVisibility();
    }

    public void Resume()
    {
        if (!_paused) return;
        _paused = false;

        Time.timeScale = 1f;
        if (_audio != null) _audio.UnPause();
        MusicClock.Instance?.Resume();
        if (_manager != null) _manager.SuppressSongEnd = false;

        RefreshVisibility();
    }

    /// <summary>
    /// Just forwards to the shared restart request — FallRespawnSystem.RestartSongManuallyRoutine
    /// itself calls PauseController.Instance.Resume() before doing anything else, so unpausing
    /// is guaranteed regardless of which button (this one, or the end screen's) called in here.
    /// One place responsible for "make sure we're unpaused before restarting", not two.
    /// </summary>
    public void RestartSongFromPause() => _manager?.RequestRestartSong();

    // ── UI ────────────────────────────────────────────────────────────────────

    // Prefab path — see PauseView's own doc.
    private void WireUI(PauseView view)
    {
        _pauseButtonRoot = view.pauseButtonRoot.GetComponent<RectTransform>();
        _overlayRoot     = view.overlayRoot.GetComponent<RectTransform>();

        view.pauseButton.onClick.AddListener(Pause);
        view.resumeButton.onClick.AddListener(Resume);
        view.restartButton.onClick.AddListener(RestartSongFromPause);

        _cameraToggleButtonRoot = view.cameraToggleButtonRoot != null ? view.cameraToggleButtonRoot.GetComponent<RectTransform>() : null;
        _cameraToggleIcon       = view.cameraToggleIcon;
        _cameraToggleLabel      = view.cameraToggleLabel;
        _thirdPersonIcon        = view.thirdPersonIcon;
        _firstPersonIcon        = view.firstPersonIcon;
        if (view.cameraToggleButton != null)
            view.cameraToggleButton.onClick.AddListener(() => CameraFollow.Instance?.ToggleView());
        RefreshCameraToggleDisplay(CameraFollow.Instance != null ? CameraFollow.Instance.ViewMode : CameraViewMode.ThirdPerson);

        // Same reasoning as GameplayHUD's own label re-set — baked once at Editor-bake time, so
        // re-apply from the current locale here instead of trusting the prefab's value.
        if (view.pauseButtonLabel   != null) view.pauseButtonLabel.text   = Loc.Get("Pause.PauseButton");
        if (view.titleText          != null) view.titleText.text          = Loc.Get("Pause.Title");
        if (view.resumeButtonLabel  != null) view.resumeButtonLabel.text  = Loc.Get("Pause.Resume");
        if (view.restartButtonLabel != null) view.restartButtonLabel.text = Loc.Get("Pause.RestartSong");

        RefreshVisibility();
    }

    // Procedural fallback — no UIRegistry in the scene yet.
    private void BuildUI()
    {
        var canvas = UIFactory.RootCanvas();

        var pauseBtn = UIFactory.CreateButton("PauseButton", canvas, Loc.Get("Pause.PauseButton"), out var pauseLabel);
        UIFactory.SetBox(pauseBtn.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-12f, -12f), new Vector2(84f, 28f));
        pauseBtn.onClick.AddListener(Pause);
        _pauseButtonRoot = pauseBtn.GetComponent<RectTransform>();
        pauseBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        pauseLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        var cameraToggleBtn = UIFactory.CreateButton("CameraToggleButton", canvas, "", out _cameraToggleLabel);
        UIFactory.SetBox(cameraToggleBtn.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-12f, -46f), new Vector2(120f, 28f));
        cameraToggleBtn.onClick.AddListener(() => CameraFollow.Instance?.ToggleView());
        _cameraToggleButtonRoot = cameraToggleBtn.GetComponent<RectTransform>();
        RefreshCameraToggleDisplay(CameraFollow.Instance != null ? CameraFollow.Instance.ViewMode : CameraViewMode.ThirdPerson);
        cameraToggleBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        _cameraToggleLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        _overlayRoot = UIFactory.CreateRect("PausedOverlay", canvas);
        UIFactory.Stretch(_overlayRoot);

        var dim = UIFactory.CreatePanel("Dim", _overlayRoot, new Color(0f, 0f, 0f, 0.6f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        float pw = 240f, ph = 170f;
        var panel = UIFactory.CreatePanel("Panel", _overlayRoot, new Color(0.04f, 0.04f, 0.04f, 0.97f));
        UIFactory.SetBox(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(pw, ph));
        var border = panel.gameObject.AddComponent<Outline>();
        border.effectColor    = new Color(0.15f, 0.15f, 0.15f, 1f);
        border.effectDistance = new Vector2(3f, -3f);
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        var content = panel.rectTransform;
        float y = -14f;

        var title = UIFactory.CreateText("Title", content, Loc.Get("Pause.Title"), 18, Color.white);
        UIFactory.StackTop(title.rectTransform, ref y, 28f, 0f);
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        y = -60f;
        float btnW = pw - 40f, btnH = 34f;
        var resumeBtn = UIFactory.CreateButton("ResumeButton", content, Loc.Get("Pause.Resume"), out var resumeLabel);
        UIFactory.SetBox(resumeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        resumeBtn.onClick.AddListener(Resume);
        resumeBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        resumeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        y -= btnH + 12f;

        var restartBtn = UIFactory.CreateButton("RestartButton", content, Loc.Get("Pause.RestartSong"), out var restartLabel);
        UIFactory.SetBox(restartBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        restartBtn.onClick.AddListener(RestartSongFromPause);
        restartBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        restartLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        _overlayRoot.gameObject.SetActive(!_ended && _paused);

        bool showPersistentButtons = !_ended && !_paused && _manager != null && _manager.IsRunning;
        _pauseButtonRoot.gameObject.SetActive(showPersistentButtons);
        if (_cameraToggleButtonRoot != null) _cameraToggleButtonRoot.gameObject.SetActive(showPersistentButtons);
    }

    /// <summary>Purely cosmetic — swaps the toggle button's icon/text to reflect the CURRENT
    /// mode (i.e. "Third Person" is shown while playing in third person, not as a call to
    /// action). Never touches CameraFollow itself.</summary>
    private void RefreshCameraToggleDisplay(CameraViewMode mode)
    {
        bool isThird = mode == CameraViewMode.ThirdPerson;
        if (_cameraToggleLabel != null) _cameraToggleLabel.text = Loc.Get(isThird ? "Pause.ThirdPerson" : "Pause.FirstPerson");
        if (_cameraToggleIcon != null)
        {
            var sprite = isThird ? _thirdPersonIcon : _firstPersonIcon;
            _cameraToggleIcon.sprite = sprite;
            _cameraToggleIcon.enabled = sprite != null;
        }
    }
}
