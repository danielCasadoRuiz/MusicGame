using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main Menu — PLAY, SETTINGS, a Profile icon placeholder, and QUIT (desktop only — see section 7
/// of the multi-scene refactor plan: hidden on mobile, never a primary mobile action). No art final:
/// plain buttons/text placeholders, themed via CurrentTheme.UI like every other screen.
///
/// PLAY goes to GameFlowState.SongSelection — SongSelectionController's own PLAY button is the one
/// that actually advances to SongAnalysis (section 9 of the plan: selecting a song must never
/// auto-start analysis by itself, a separate PLAY button does that).
///
/// Settings is a simple volume-only panel for now (section 7: "volum per ara") — bound to the real
/// global AudioListener.volume, persisted via PlayerPrefs so it survives between sessions; not a
/// fake control wired to nothing.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// GameFlowStateChangedEvent directly, same pattern as FightController/IntroScreenController.
/// </summary>
public class MainMenuController : ThemeReceiverBehaviour
{
    private const string VolumePrefKey = "MasterVolume";

    private RectTransform _root;
    private RectTransform _settingsPanel;
    private Image         _dim;
    private Text          _titleText;
    private Text          _playLabel, _settingsLabel, _quitLabel, _settingsTitleLabel, _volumeLabel, _closeLabel;
    private Image         _profileIcon;
    private Button        _quitButton;
    private Slider        _volumeSlider;

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;

    private void Awake() => Build();

    protected override void OnEnable()
    {
        base.OnEnable();
        _onFlowStateChanged = e =>
        {
            if (e.Current == GameFlowState.MainMenu) Show();
            else if (e.Previous == GameFlowState.MainMenu) Hide();
        };
        EventBus.Subscribe(_onFlowStateChanged);

        // Same UI-Scene-loads-asynchronously race as IntroScreenController — catch up once instead
        // of only reacting to the NEXT change, which might never come.
        if (AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.MainMenu)
            Show();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        EventBus.Unsubscribe(_onFlowStateChanged);
    }

    public override void ApplyUITheme(UIStyleSO ui)
    {
        if (ui == null) return;
        if (_dim != null) _dim.color = ui.backgroundColor;
        if (_titleText != null) _titleText.color = ui.primaryColor;
        if (_profileIcon != null) _profileIcon.color = ui.secondaryColor;

        Color accent = ui.accentColor;
        if (_playLabel != null) _playLabel.color = accent;
    }

    // ── Build (runtime-only, no prefab) ────────────────────────────────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("MainMenuScreen", canvas);
        UIFactory.Stretch(_root);
        _root.SetAsLastSibling();

        _dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(_dim.rectTransform);

        _titleText = UIFactory.CreateText("Title", _root, Loc.Get("MainMenu.Title"), 36, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIFactory.SetBox(_titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -100f), new Vector2(900f, 60f));

        // Profile icon placeholder, top-right — no behavior yet.
        _profileIcon = UIFactory.CreatePanel("ProfileIcon", _root, Color.gray);
        UIFactory.SetBox(_profileIcon.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -24f), new Vector2(48f, 48f));

        float btnW = 260f, btnH = 54f, gap = 18f;
        float y = -20f;

        var playBtn = UIFactory.CreateButton("PlayButton", _root, Loc.Get("MainMenu.Play"), out _playLabel);
        UIFactory.SetBox(playBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        playBtn.onClick.AddListener(OnPlayClicked);
        y -= btnH + gap;

        var settingsBtn = UIFactory.CreateButton("SettingsButton", _root, Loc.Get("MainMenu.Settings"), out _settingsLabel);
        UIFactory.SetBox(settingsBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        settingsBtn.onClick.AddListener(OnSettingsClicked);
        y -= btnH + gap;

        var quitBtn = UIFactory.CreateButton("QuitButton", _root, Loc.Get("MainMenu.Quit"), out _quitLabel);
        UIFactory.SetBox(quitBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        quitBtn.onClick.AddListener(OnQuitClicked);
        _quitButton = quitBtn;
        // QUIT is a desktop-only action — never a primary action on mobile (section 7 of the plan).
        _quitButton.gameObject.SetActive(!Application.isMobilePlatform);

        BuildSettingsPanel();

        _root.gameObject.SetActive(false);
    }

    private void BuildSettingsPanel()
    {
        _settingsPanel = UIFactory.CreateRect("SettingsPanel", _root);
        UIFactory.SetBox(_settingsPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(420f, 220f));
        var panelBg = _settingsPanel.gameObject.AddComponent<Image>();
        panelBg.color = new Color(0.05f, 0.05f, 0.05f, 0.97f);

        _settingsTitleLabel = UIFactory.CreateText("Title", _settingsPanel, Loc.Get("Settings.Title"), 20, Color.white);
        UIFactory.SetBox(_settingsTitleLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -16f), new Vector2(380f, 30f));

        _volumeLabel = UIFactory.CreateText("VolumeLabel", _settingsPanel, Loc.Get("Settings.Volume"), 14, new Color(0.8f, 0.8f, 0.8f), TextAnchor.MiddleLeft);
        UIFactory.SetBox(_volumeLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 20f), new Vector2(360f, 24f));

        float startVolume = PlayerPrefs.GetFloat(VolumePrefKey, 1f);
        AudioListener.volume = startVolume;
        _volumeSlider = UIFactory.CreateSlider("VolumeSlider", _settingsPanel, startVolume, new Color(0.18f, 0.75f, 0.95f));
        UIFactory.SetBox(_volumeSlider.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -15f), new Vector2(360f, 16f));
        _volumeSlider.onValueChanged.AddListener(OnVolumeChanged);

        var closeBtn = UIFactory.CreateButton("CloseButton", _settingsPanel, Loc.Get("Settings.Close"), out _closeLabel);
        UIFactory.SetBox(closeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 20f), new Vector2(140f, 40f));
        closeBtn.onClick.AddListener(() => _settingsPanel.gameObject.SetActive(false));

        _settingsPanel.gameObject.SetActive(false);
    }

    // ── Show / hide ─────────────────────────────────────────────────────────────

    private void Show()
    {
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _settingsPanel.gameObject.SetActive(false);
    }

    private void Hide()
    {
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // ── Actions ─────────────────────────────────────────────────────────────────

    private void OnPlayClicked()
    {
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongSelection);
    }

    private void OnSettingsClicked() => _settingsPanel.gameObject.SetActive(true);

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnVolumeChanged(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat(VolumePrefKey, value);
    }
}
