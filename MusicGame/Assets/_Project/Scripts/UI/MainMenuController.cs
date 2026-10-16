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
/// No progress yet: PLAY → Song Selection (the first song is always chosen manually; it becomes the
/// similarity seed). With progress: CONTINUE resumes the pending assignment (same song + same rival)
/// or assigns the next uncompleted similarity-route song, and goes straight to SongAnalysis — no
/// selector. There is deliberately NO "select song" here: manual choice lives after a won fight
/// (MatchResultController's SELECT SONG). When every playable song is completed, CONTINUE shows
/// "catalog complete".
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
    private TextMeshProUGUI _playLabel;     // "PLAY" first launch, "CONTINUE" once a progression save exists
    private TextMeshProUGUI _profileText;   // persistent level / XP / lives
    private TextMeshProUGUI _menuStatusText;   // "catalog complete" / load failure
    private readonly System.Collections.Generic.List<RectTransform> _buttonStack = new(); // top → bottom
    private SongSelectionService _songService;
    private bool _continuing;

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;

    // Language selector (Settings) — built at runtime on BOTH the prefab and fallback panels, like the
    // Rivals button, so regenerating MainMenu.prefab never removes it. See LanguagePreference.
    private CampaignPopup _popup;

    private RectTransform _languageRow;
    private readonly System.Collections.Generic.List<(string mode, Button button, Outline outline)> _languageOptions = new();
    private System.Action _onLanguageRefreshed, _onLanguageModeChanged;

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
        // Language changed: texts tracked by Loc are already rebuilt; refresh the composite profile
        // line / status and the selector's highlight (no rebuild, the panel stays open).
        _onLanguageRefreshed = () => { if (_root != null && _root.gameObject.activeSelf) RefreshProgressUI(); RefreshLanguageSelection(); };
        _onLanguageModeChanged = RefreshLanguageSelection;
        Loc.Refreshed += _onLanguageRefreshed;
        LanguagePreference.ModeChanged += _onLanguageModeChanged;

        // Same UI-Scene-loads-asynchronously race as IntroScreenController — catch up once instead
        // of only reacting to the NEXT change, which might never come.
        if (AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.MainMenu)
            Show();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
        Loc.Refreshed -= _onLanguageRefreshed;
        LanguagePreference.ModeChanged -= _onLanguageModeChanged;
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
        _playLabel = view.playButtonLabel;

        // The prefab predates the Rivals collection: add its button + profile line next to Settings.
        var rivals = Instantiate(view.settingsButton, view.settingsButton.transform.parent);
        rivals.name = "RivalsButton";
        rivals.onClick = new Button.ButtonClickedEvent();
        rivals.onClick.AddListener(OnRivalsClicked);
        var rivalsRt = rivals.GetComponent<RectTransform>();
        rivalsRt.anchoredPosition += new Vector2(0f, rivalsRt.sizeDelta.y + 18f) * 0.5f + new Vector2(rivalsRt.sizeDelta.x * 0.55f, 0f);
        var rivalsLabel = rivals.GetComponentInChildren<TextMeshProUGUI>();
        if (rivalsLabel != null) Loc.Set(rivalsLabel, "MainMenu.Rivals");
        // PROGRESSIÓ, right under COL·LECCIÓ (same runtime-added pattern as the Rivals button).
        var progression = Instantiate(view.settingsButton, view.settingsButton.transform.parent);
        progression.name = "ProgressionButton";
        progression.onClick = new Button.ButtonClickedEvent();
        progression.onClick.AddListener(OnProgressionClicked);
        var progressionLabel = progression.GetComponentInChildren<TextMeshProUGUI>();
        if (progressionLabel != null) Loc.Set(progressionLabel, "MainMenu.Progression");
        BuildProfileText(view.root.GetComponent<RectTransform>());
        BuildMenuStatus(view.root.GetComponent<RectTransform>());
        _buttonStack.AddRange(new[] { view.playButton.GetComponent<RectTransform>(), rivalsRt, progression.GetComponent<RectTransform>(),
                                      view.settingsButton.GetComponent<RectTransform>(), view.quitButton.GetComponent<RectTransform>() });
        view.quitButton.onClick.AddListener(OnQuitClicked);
        view.closeButton.onClick.AddListener(() => _settingsPanel.gameObject.SetActive(false));

        float startVolume = PlayerPrefs.GetFloat(VolumePrefKey, 1f);
        AudioListener.volume = startVolume;
        _volumeSlider.value = startVolume;
        _volumeSlider.onValueChanged.AddListener(OnVolumeChanged);

        // Baked once at Editor-bake time — re-apply from the current locale here instead of
        // trusting the prefab's value, same reasoning as GameplayHUD/PauseController's own labels.
        Loc.Set(view.titleText, "MainMenu.Title");
        Loc.Set(view.playButtonLabel, "MainMenu.Play");
        Loc.Set(view.settingsButtonLabel, "MainMenu.Settings");
        Loc.Set(view.quitButtonLabel, "MainMenu.Quit");
        Loc.Set(view.settingsTitleText, "Settings.Title");
        Loc.Set(view.volumeLabelText, "Settings.Volume");
        Loc.Set(view.closeButtonLabel, "Settings.Close");
        BuildLanguageRow();

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

        var title = Loc.Track(UIFactory.CreateText("Title", _root, Loc.Get("MainMenu.Title"), 36, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "MainMenu.Title");
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

        var playBtn = UIFactory.CreateButton("PlayButton", _root, Loc.Get("MainMenu.Play"), out var playLabel); Loc.Track(playLabel, "MainMenu.Play");
        UIFactory.SetBox(playBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        playBtn.onClick.AddListener(OnPlayClicked);
        playBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        playLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        _playLabel = playLabel;
        y -= btnH + gap;


        var rivalsBtn = UIFactory.CreateButton("RivalsButton", _root, Loc.Get("MainMenu.Rivals"), out var rivalsLabel); Loc.Track(rivalsLabel, "MainMenu.Rivals");
        UIFactory.SetBox(rivalsBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        rivalsBtn.onClick.AddListener(OnRivalsClicked);
        rivalsBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        rivalsLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        y -= btnH + gap;

        var progressionBtn = UIFactory.CreateButton("ProgressionButton", _root, Loc.Get("MainMenu.Progression"), out var progressionLabel); Loc.Track(progressionLabel, "MainMenu.Progression");
        UIFactory.SetBox(progressionBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        progressionBtn.onClick.AddListener(OnProgressionClicked);
        progressionBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        progressionLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        y -= btnH + gap;

        var settingsBtn = UIFactory.CreateButton("SettingsButton", _root, Loc.Get("MainMenu.Settings"), out var settingsLabel); Loc.Track(settingsLabel, "MainMenu.Settings");
        UIFactory.SetBox(settingsBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        settingsBtn.onClick.AddListener(OnSettingsClicked);
        settingsBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        settingsLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        y -= btnH + gap;

        var quitBtn = UIFactory.CreateButton("QuitButton", _root, Loc.Get("MainMenu.Quit"), out var quitLabel); Loc.Track(quitLabel, "MainMenu.Quit");
        UIFactory.SetBox(quitBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(btnW, btnH));
        quitBtn.onClick.AddListener(OnQuitClicked);
        quitBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        quitLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        _quitButton = quitBtn;
        // QUIT is a desktop-only action — never a primary action on mobile (section 7 of the plan).
        _quitButton.gameObject.SetActive(!PlatformService.IsMobile);

        BuildProfileText(_root);
        BuildMenuStatus(_root);
        _buttonStack.AddRange(new[] { playBtn.GetComponent<RectTransform>(), rivalsBtn.GetComponent<RectTransform>(),
                                      settingsBtn.GetComponent<RectTransform>(), quitBtn.GetComponent<RectTransform>() });
        BuildSettingsPanel();

        _root.gameObject.SetActive(false);
    }

    // Persistent progression summary under the title (PlayerProgressService is the only source).
    private void BuildProfileText(RectTransform parent)
    {
        _profileText = UIFactory.CreateText("ProfileText", parent, "", 18, Color.white, TextAlignmentOptions.Center);
        UIFactory.SetBox(_profileText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -160f), new Vector2(900f, 70f));
        _profileText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
    }

    private void BuildMenuStatus(RectTransform parent)
    {
        _menuStatusText = UIFactory.CreateText("MenuStatus", parent, "", 18, new Color(0.45f, 0.85f, 0.5f), TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_menuStatusText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 60f), new Vector2(700f, 32f));
        _menuStatusText.gameObject.SetActive(false);
    }

    private void SetMenuStatus(string text)
    {
        if (_menuStatusText == null) return;
        _menuStatusText.text = text ?? "";
        _menuStatusText.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    // Visible buttons stacked top → bottom from the PLAY slot.
    private void LayoutButtons()
    {
        if (_buttonStack.Count == 0 || _buttonStack[0] == null) return;
        float x = _buttonStack[0].anchoredPosition.x, y = _buttonStack[0].anchoredPosition.y, step = _buttonStack[0].sizeDelta.y + 18f;
        foreach (var rt in _buttonStack)
        {
            if (rt == null || !rt.gameObject.activeSelf) continue;
            rt.anchoredPosition = new Vector2(x, y);
            y -= step;
        }
    }

    private void RefreshProgressUI()
    {
        var progress = PlayerProgressService.Instance;
        bool hasSave = progress != null && progress.HasSave;
        if (_playLabel != null) Loc.Set(_playLabel, hasSave ? "MainMenu.Continue" : "MainMenu.Play");
        LayoutButtons();
        SetMenuStatus(hasSave && SongProgression.CatalogComplete ? Loc.Get("MainMenu.CatalogComplete") : null);
        if (_profileText == null) return;
        _profileText.gameObject.SetActive(hasSave);
        if (!hasSave) return;
        // Musical Mastery is the prominent identity; victories under it; XP / lives stay subtle.
        _profileText.text = $"<size=150%><b>{progress.MasteryRankName}</b></size>\n" +
                            Loc.Get("MainMenu.Victories", progress.FightsWon.ToString()) +
                            $"<size=80%><alpha=#99>   ·   XP {progress.Xp}   ·   {Loc.Get("HUD.Life")} {progress.ExtraLives}</size>";
    }

    private void BuildSettingsPanel()
    {
        _settingsPanel = UIFactory.CreateRect("SettingsPanel", _root);
        UIFactory.SetBox(_settingsPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(420f, 220f));
        var panelBg = _settingsPanel.gameObject.AddComponent<Image>();
        panelBg.color = new Color(0.05f, 0.05f, 0.05f, 0.97f);
        _settingsPanel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        var settingsTitle = Loc.Track(UIFactory.CreateText("Title", _settingsPanel, Loc.Get("Settings.Title"), 20, Color.white), "Settings.Title");
        UIFactory.SetBox(settingsTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -16f), new Vector2(380f, 30f));
        settingsTitle.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);

        var volumeLabel = Loc.Track(UIFactory.CreateText("VolumeLabel", _settingsPanel, Loc.Get("Settings.Volume"), 14, new Color(0.8f, 0.8f, 0.8f), TextAlignmentOptions.Left), "Settings.Volume");
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

        var closeBtn = UIFactory.CreateButton("CloseButton", _settingsPanel, Loc.Get("Settings.Close"), out var closeLabel); Loc.Track(closeLabel, "Settings.Close");
        UIFactory.SetBox(closeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 20f), new Vector2(140f, 40f));
        closeBtn.onClick.AddListener(() => _settingsPanel.gameObject.SetActive(false));
        closeBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        closeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        BuildLanguageRow();
        _settingsPanel.gameObject.SetActive(false);
    }

    // ── Language selector ─────────────────────────────────────────────────────────

    private const float LanguageRowExtraHeight = 110f;

    /// <summary>"Language" label + one button per option (Automatic + every available Locale) under the
    /// volume slider. The panel grows by LanguageRowExtraHeight: centre-anchored items stay put, the
    /// title rises and Close drops with the panel's edges.</summary>
    private void BuildLanguageRow()
    {
        if (_settingsPanel == null || _languageRow != null) return;
        _settingsPanel.sizeDelta += new Vector2(0f, LanguageRowExtraHeight);

        _languageRow = UIFactory.CreateRect("LanguageRow", _settingsPanel);
        UIFactory.SetBox(_languageRow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -62f), new Vector2(380f, 74f));
        var label = Loc.Track(UIFactory.CreateText("LanguageLabel", _languageRow, Loc.Get("Settings.Language"), 14,
                                                   new Color(0.8f, 0.8f, 0.8f), TextAlignmentOptions.Left), "Settings.Language");
        UIFactory.SetBox(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 0f), new Vector2(360f, 24f));
        label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        // The options come from the project's Locales — wait for Localization if it is still starting.
        var init = UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation;
        if (init.IsDone) BuildLanguageButtons();
        else init.Completed += _ => { if (this != null) BuildLanguageButtons(); };
    }

    private void BuildLanguageButtons()
    {
        if (_languageRow == null || _languageOptions.Count > 0) return;
        var modes = new System.Collections.Generic.List<string> { LanguagePreference.Auto };
        modes.AddRange(LanguagePreference.AvailableCodes());
        const float width = 360f, gap = 10f;
        float w = (width - gap * (modes.Count - 1)) / modes.Count;
        for (int i = 0; i < modes.Count; i++)
        {
            string mode = modes[i];
            var btn = UIFactory.CreateButton("Language_" + mode, _languageRow, LanguagePreference.DisplayName(mode), out var btnLabel);
            if (mode == LanguagePreference.Auto) Loc.Track(btnLabel, "Settings.LanguageAuto"); // "Català"/"English" are proper names
            UIFactory.SetBox(btn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                new Vector2(-width * 0.5f + i * (w + gap), 0f), new Vector2(w, 40f));
            btn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
            btnLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
            var outline = btn.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.82f, 0.35f, 1f);
            outline.effectDistance = new Vector2(3f, -3f);
            btn.onClick.AddListener(() => LanguagePreference.SetMode(mode));
            _languageOptions.Add((mode, btn, outline));
        }
        RefreshLanguageSelection();
    }

    /// <summary>Highlights the CURRENT preference (Automatic stays highlighted even though it resolved
    /// to a concrete language).</summary>
    private void RefreshLanguageSelection()
    {
        if (_languageOptions.Count == 0) return;
        string mode = LanguagePreference.Mode;
        foreach (var o in _languageOptions)
            if (o.outline != null) o.outline.enabled = o.mode == mode;
    }

    // ── Show / hide ─────────────────────────────────────────────────────────────

    private void Show()
    {
        RefreshProgressUI();
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _settingsPanel.gameObject.SetActive(false);
        ShowNextNotice(); // after the menu is up, so the popup sits on top
    }

    private void Hide()
    {
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // ── Actions ─────────────────────────────────────────────────────────────────

    // PLAY (first launch) starts a progression save right away, so the next launch says CONTINUE,
    // and opens the selector (the first song is chosen manually). CONTINUE keeps level / XP / lives /
    // rival collection / song history and auto-picks the next song — every run still gets a fresh RunSession.
    private void OnPlayClicked()
    {
        var progress = PlayerProgressService.Instance;
        if (progress != null && !progress.HasSave)
        {
            progress.Save();
            Debug.Log("[PlayerProgress] New progression profile created (Play).");
            AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongSelection);
            return;
        }
        if (progress == null) { AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongSelection); return; }
        if (!_continuing) StartCoroutine(ContinueRoutine());
    }

    // CONTINUE: the pending song (if any) or the next uncompleted route song -> the same load + SongAnalysis path
    // Song Selection uses (SongSelectionService + AddressableSongSource), without showing the selector.
    private System.Collections.IEnumerator ContinueRoutine()
    {
        _continuing = true;
        System.Collections.Generic.List<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation> catalog = null;
        yield return SongProgression.LoadCatalog(list => catalog = list);

        // A valid pending assignment resumes (same song + same rival); otherwise the next route song
        // gets a fresh assignment, persisted before anything starts (RunAssignment).
        var status = RunAssignment.ResolveContinue(SongProgression.Ids(catalog), out string songId);
        var location = status == NextSongStatus.Found ? SongProgression.Find(catalog, songId) : null;
        if (location == null)
        {
            _continuing = false;
            if (status == NextSongStatus.LapFinished)
                ShowLapFinished(); // campaign lap over: celebrate / offer a new lap
            else if (status == NextSongStatus.NoSeed)
                AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongSelection); // no preference yet (old save): choose one
            else
                SetMenuStatus(Loc.Get(status == NextSongStatus.CatalogComplete ? "MainMenu.CatalogComplete" : "SongSelection.LoadFailed"));
            yield break;
        }

        // Same order as the selector PLAY: the Analyzing screen appears at once, the clip loads underneath.
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongAnalysis);
        _songService ??= GetComponent<SongSelectionService>() ?? gameObject.AddComponent<SongSelectionService>();
        yield return _songService.SelectSong(new AddressableSongSource(location), null, ok =>
        {
            if (ok) return;
            Debug.LogWarning($"[MainMenuController] CONTINUE: failed to load '{songId}' - back to Main Menu.");
            AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
            SetMenuStatus(Loc.Get("SongSelection.LoadFailed"));
        });
        _continuing = false;
    }

    private void OnRivalsClicked() => RivalCollectionController.Instance?.Open();
    private void OnProgressionClicked() => ProgressionScreenController.Instance?.Open();

    // ── Campaign moments (a safe point: nothing is playing) ─────────────────────

    private CampaignPopup Popup => _popup != null ? _popup : _popup = CampaignPopup.Create("CampaignPopup");

    /// <summary>The next one-time campaign notice, if any (reveal, unlock, finale, seal). Marked shown when displayed.</summary>
    private void ShowNextNotice()
    {
        if (Popup.IsOpen) return;
        var notice = CampaignService.PendingNotice();
        if (notice == CampaignService.Notice.None) return;
        CampaignService.MarkShown(notice);
        var boss = CampaignService.Boss;
        var silhouette = boss != null ? (boss.silhouette != null ? boss.silhouette : boss.LevelConfig(0)?.portrait) : null;
        switch (notice)
        {
            case CampaignService.Notice.CampaignCompleted:
                Popup.Show(Loc.Get("Campaign.CompletedTitle"), Loc.Get("Campaign.CompletedBody"), silhouette, Color.black,
                           Loc.Get("Campaign.NewLap"), StartNewLapAndContinue, Loc.Get("Campaign.Later"), ShowNextNotice);
                break;
            case CampaignService.Notice.BossUnlocked:
                Popup.Show(Loc.Get("Campaign.BossUnlockedTitle"), Loc.Get("Campaign.BossUnlockedBody"), silhouette, Color.white,
                           Loc.Get("Campaign.Challenge"), OnProgressionClicked, Loc.Get("Campaign.Later"), ShowNextNotice);
                break;
            case CampaignService.Notice.Finale:
                Popup.Show(Loc.Get("Campaign.FinaleTitle"), Loc.Get("Campaign.FinaleBody"), silhouette, Color.white,
                           Loc.Get("Campaign.Ok"), ShowNextNotice);
                break;
            case CampaignService.Notice.Seal:
                Popup.Show(Loc.Get("Campaign.SealName"), Loc.Get("Campaign.SealBody"), silhouette, new Color(1f, 0.85f, 0.4f),
                           Loc.Get("Campaign.Ok"), ShowNextNotice);
                break;
        }
    }

    /// <summary>CONTINUE with the lap over: celebrate and offer a new lap (records and cards are kept).</summary>
    private void ShowLapFinished()
    {
        if (CampaignService.PendingNotice() == CampaignService.Notice.CampaignCompleted) { ShowNextNotice(); return; }
        var data = CampaignService.Data;
        Popup.Show(Loc.Get("Campaign.LapDoneTitle", data != null ? data.lap.ToString() : "1"), Loc.Get("Campaign.LapDoneBody"), null, Color.white,
                   Loc.Get("Campaign.NewLap"), StartNewLapAndContinue, Loc.Get("Campaign.Later"), null);
    }

    private void StartNewLapAndContinue()
    {
        CampaignService.StartNewLap();
        RefreshProgressUI();
        if (!_continuing) StartCoroutine(ContinueRoutine());
    }

    private void OnSettingsClicked()
    {
        _settingsPanel.gameObject.SetActive(true);
        RefreshLanguageSelection();
    }

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
