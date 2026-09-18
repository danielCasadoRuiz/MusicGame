using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main Menu — PLAY, SETTINGS, a Profile icon placeholder, and QUIT (desktop only — see section 7
/// of the multi-scene refactor plan: hidden on mobile, never a primary mobile action). No art final:
/// plain buttons/text placeholders.
///
/// Pure SCREEN CONTROLLER now (navigation/interaction only) — theming lives entirely on generic
/// receivers attached to each themed child (ThemeColorReceiver/ThemeTextReceiver), not in this
/// class. No special visual behavior of its own, so no PrefabThemeController either.
///
/// Prefers a real MainMenu.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to the old procedural build only if that
/// hasn't been run yet, same pattern as GameplayHUD/PauseController.
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
public class MainMenuController : MonoBehaviour
{
    private const string VolumePrefKey = "MasterVolume";

    private RectTransform _root;
    private RectTransform _settingsPanel;
    private Button        _quitButton;
    private Slider        _volumeSlider;

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;

    private void Awake()
    {
        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.MainMenu != null) WireUI(registry.MainMenu);
        else Build();
    }

    private void OnEnable()
    {
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

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
    }

    // ── Prefab path — see MainMenuView's own doc ─────────────────────────────────

    private void WireUI(MainMenuView view)
    {
        _root          = view.root.GetComponent<RectTransform>();
        _settingsPanel = view.settingsPanel.GetComponent<RectTransform>();
        _quitButton    = view.quitButton;
        _volumeSlider  = view.volumeSlider;

        view.playButton.onClick.AddListener(OnPlayClicked);
        view.settingsButton.onClick.AddListener(OnSettingsClicked);
        view.quitButton.onClick.AddListener(OnQuitClicked);
        view.closeButton.onClick.AddListener(() => _settingsPanel.gameObject.SetActive(false));

        float startVolume = PlayerPrefs.GetFloat(VolumePrefKey, 1f);
        AudioListener.volume = startVolume;
        _volumeSlider.value = startVolume;
        _volumeSlider.onValueChanged.AddListener(OnVolumeChanged);

        // Baked once at Editor-bake time — re-apply from the current locale here instead of
        // trusting the prefab's value, same reasoning as GameplayHUD/PauseController's own labels.
        view.titleText.text          = Loc.Get("MainMenu.Title");
        view.playButtonLabel.text    = Loc.Get("MainMenu.Play");
        view.settingsButtonLabel.text = Loc.Get("MainMenu.Settings");
        view.quitButtonLabel.text    = Loc.Get("MainMenu.Quit");
        view.settingsTitleText.text  = Loc.Get("Settings.Title");
        view.volumeLabelText.text    = Loc.Get("Settings.Volume");
        view.closeButtonLabel.text   = Loc.Get("Settings.Close");

        // QUIT is a desktop-only action — never a primary action on mobile (section 7 of the plan).
        _quitButton.gameObject.SetActive(!PlatformService.IsMobile);

        _settingsPanel.gameObject.SetActive(false);
        _root.gameObject.SetActive(false);
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("MainMenuScreen", canvas);
        UIFactory.Stretch(_root);
        _root.SetAsLastSibling();

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var title = UIFactory.CreateText("Title", _root, Loc.Get("MainMenu.Title"), 36, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -100f), new Vector2(900f, 60f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        // Profile icon placeholder, top-right — no behavior yet.
        var profileIcon = UIFactory.CreatePanel("ProfileIcon", _root, Color.gray);
        UIFactory.SetBox(profileIcon.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -24f), new Vector2(48f, 48f));
        profileIcon.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Secondary);

        float btnW = 260f, btnH = 54f, gap = 18f;
        float y = -20f;

        var playBtn = UIFactory.CreateButton("PlayButton", _root, Loc.Get("MainMenu.Play"), out var playLabel);
        UIFactory.SetBox(playBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        playBtn.onClick.AddListener(OnPlayClicked);
        playBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        playLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        y -= btnH + gap;

        var settingsBtn = UIFactory.CreateButton("SettingsButton", _root, Loc.Get("MainMenu.Settings"), out var settingsLabel);
        UIFactory.SetBox(settingsBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        settingsBtn.onClick.AddListener(OnSettingsClicked);
        settingsBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        settingsLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        y -= btnH + gap;

        var quitBtn = UIFactory.CreateButton("QuitButton", _root, Loc.Get("MainMenu.Quit"), out var quitLabel);
        UIFactory.SetBox(quitBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        quitBtn.onClick.AddListener(OnQuitClicked);
        quitBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        quitLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        _quitButton = quitBtn;
        // QUIT is a desktop-only action — never a primary action on mobile (section 7 of the plan).
        _quitButton.gameObject.SetActive(!PlatformService.IsMobile);

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
        _settingsPanel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        var settingsTitle = UIFactory.CreateText("Title", _settingsPanel, Loc.Get("Settings.Title"), 20, Color.white);
        UIFactory.SetBox(settingsTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -16f), new Vector2(380f, 30f));
        settingsTitle.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);

        var volumeLabel = UIFactory.CreateText("VolumeLabel", _settingsPanel, Loc.Get("Settings.Volume"), 14, new Color(0.8f, 0.8f, 0.8f), TextAlignmentOptions.Left);
        UIFactory.SetBox(volumeLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 20f), new Vector2(360f, 24f));
        volumeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        float startVolume = PlayerPrefs.GetFloat(VolumePrefKey, 1f);
        AudioListener.volume = startVolume;
        _volumeSlider = UIFactory.CreateSlider("VolumeSlider", _settingsPanel, startVolume, new Color(0.18f, 0.75f, 0.95f));
        UIFactory.SetBox(_volumeSlider.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -15f), new Vector2(360f, 16f));
        _volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        _volumeSlider.fillRect.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Accent);

        var closeBtn = UIFactory.CreateButton("CloseButton", _settingsPanel, Loc.Get("Settings.Close"), out var closeLabel);
        UIFactory.SetBox(closeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 20f), new Vector2(140f, 40f));
        closeBtn.onClick.AddListener(() => _settingsPanel.gameObject.SetActive(false));
        closeBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        closeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

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
