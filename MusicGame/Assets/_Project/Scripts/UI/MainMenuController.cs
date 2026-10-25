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
/// Structure and look: Prefabs/UI/MainMenu.prefab (MainMenuView, UIPrefabLibrarySO.mainMenu, generated
/// by FrontendScreenPrefabs and edited by hand) — every button, the profile/status lines and the settings
/// panel with its nested LanguageSelector. This controller spawns it and only wires/fills it — no UI is
/// built in code; a missing prefab is logged and the screen stays disabled.
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
    private TextMeshProUGUI _profileText;   // campaign position + extra lives
    private TextMeshProUGUI _menuStatusText;   // "catalog complete" / load failure
    private readonly System.Collections.Generic.List<RectTransform> _buttonStack = new(); // top → bottom
    private SongSelectionService _songService;
    private bool _continuing;

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;

    private MainMenuView _view;
    private float _buttonStackGap = 18f;
    private CampaignPopup _popup;

    // Language selector (Settings) — nested LanguageSelector prefab in the settings panel. See LanguagePreference.
    private LanguageSelectorView _languageSelector;
    private System.Action _onLanguageRefreshed, _onLanguageModeChanged;

    private void Awake()
    {
        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.mainMenu : null, UIFactory.RootCanvas(), "MainMenu");
        if (_view == null) { enabled = false; return; }
        WireUI(_view);
    }

    private void OnDestroy()
    {
        if (_view != null) Destroy(_view.gameObject);
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

    // ── Prefab wiring — see MainMenuView's own doc ───────────────────────────────

    private void WireUI(MainMenuView view)
    {
        _root          = view.root.GetComponent<RectTransform>();
        UIFactory.Stretch(_root);
        _root.SetAsLastSibling();
        _settingsPanel = view.settingsPanel.GetComponent<RectTransform>();
        _quitButton    = view.quitButton;
        _volumeSlider  = view.volumeSlider;
        _playLabel     = view.playButtonLabel;
        _profileText   = view.profileText;
        _menuStatusText = view.menuStatusText;
        _buttonStackGap = view.buttonStackGap;

        view.playButton.onClick.AddListener(OnPlayClicked);
        view.rivalsButton.onClick.AddListener(OnRivalsClicked);
        view.progressionButton.onClick.AddListener(OnProgressionClicked);
        if (view.profileButton != null) view.profileButton.onClick.AddListener(() => ProfileScreenController.Instance?.Open());
        view.settingsButton.onClick.AddListener(OnSettingsClicked);
        view.quitButton.onClick.AddListener(OnQuitClicked);
        view.closeButton.onClick.AddListener(() => _settingsPanel.gameObject.SetActive(false));
        _buttonStack.AddRange(new[] { view.playButton.GetComponent<RectTransform>(), view.rivalsButton.GetComponent<RectTransform>(),
                                      view.progressionButton.GetComponent<RectTransform>(), view.settingsButton.GetComponent<RectTransform>(),
                                      view.quitButton.GetComponent<RectTransform>() });

        float startVolume = PlayerPrefs.GetFloat(VolumePrefKey, 1f);
        AudioListener.volume = startVolume;
        _volumeSlider.value = startVolume;
        _volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        // Channel volumes (GameAudio persists them; master stays the global AudioListener volume above).
        BindChannel(view.musicVolumeSlider, GameAudioChannel.Music);
        BindChannel(view.sfxVolumeSlider, GameAudioChannel.Sfx);
        BindChannel(view.voiceVolumeSlider, GameAudioChannel.Voice);

        // Data-dependent (PLAY / CONTINUE): set here and in RefreshProgressUI; static labels are LocalizedText.
        Loc.Set(_playLabel, "MainMenu.Play");
        BuildLanguageRow(view.languageSelector);

        // QUIT is a desktop-only action — never a primary action on mobile (section 7 of the plan).
        _quitButton.gameObject.SetActive(!PlatformService.IsMobile);

        SetMenuStatus(null);
        _settingsPanel.gameObject.SetActive(false);
        _root.gameObject.SetActive(false);
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
        float x = _buttonStack[0].anchoredPosition.x, y = _buttonStack[0].anchoredPosition.y, step = _buttonStack[0].sizeDelta.y + _buttonStackGap;
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
        // The campaign IS the progression: where the next match is (big), then lap · mastered matches ·
        // extra lives (the Fight Again inventory). No XP / level / rank (retired — see PlayerProgressService).
        _profileText.text = BuildProfileText(progress);
    }

    private static string BuildProfileText(PlayerProgressService progress)
    {
        string lives = Loc.Get("MainMenu.ExtraLives", progress.ExtraLives.ToString());
        if (!CampaignService.Available) return lives;
        var data = CampaignService.Data;
        var (mastered, total) = CampaignModel.MasteryCount(data, CampaignService.Config);
        string headline = CampaignService.LapFinished ? Loc.Get("MainMenu.LapDone", data.lap.ToString()) : CampaignService.CurrentPositionLabel;
        return $"<size=150%><b>{headline}</b></size>\n" +
               $"<size=85%><alpha=#BB>{Loc.Get("MainMenu.CampaignStatus", data.lap.ToString(), mastered.ToString(), total.ToString())}   ·   {lives}</size>";
    }

    // ── Language selector ─────────────────────────────────────────────────────────

    /// <summary>Fills the settings panel's nested LanguageSelector prefab (the panel is already sized for it
    /// in MainMenu.prefab). Options come from the project's Locales.</summary>
    private void BuildLanguageRow(LanguageSelectorView selector)
    {
        _languageSelector = selector;
        if (_languageSelector == null)
        {
            Debug.LogError("[MainMenuController] MainMenu.prefab has no LanguageSelector in its settings panel (MainMenuView.languageSelector) — rebuild the prefab.");
            return;
        }
        var init = UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation;
        if (init.IsDone) BuildLanguageButtons();
        else init.Completed += _ => { if (this != null) BuildLanguageButtons(); };
    }

    private void BuildLanguageButtons()
    {
        if (_languageSelector == null || _languageSelector.OptionCount > 0) return;
        var modes = new System.Collections.Generic.List<string> { LanguagePreference.Auto };
        modes.AddRange(LanguagePreference.AvailableCodes());
        _languageSelector.Build(modes, LanguagePreference.DisplayName, LanguagePreference.SetMode);
        RefreshLanguageSelection();
    }

    /// <summary>Highlights the CURRENT preference (Automatic stays highlighted even though it resolved
    /// to a concrete language).</summary>
    private void RefreshLanguageSelection()
    {
        if (_languageSelector != null) _languageSelector.SetSelected(LanguagePreference.Mode);
    }

    // ── Show / hide ─────────────────────────────────────────────────────────────

    private void Show()
    {
        RefreshProgressUI();
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _settingsPanel.gameObject.SetActive(false);
        ShowNextNotice(); // after the menu is up, so the popup sits on top
        if (CampaignService.LapPromptPending) ShowLapFinished(); // a just-finished lap: offered once
    }

    private void Hide()
    {
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // ── Actions ─────────────────────────────────────────────────────────────────

    // PLAY (first launch) starts a progression save right away, so the next launch says CONTINUE,
    // and opens the selector (the first song is chosen manually). CONTINUE keeps campaign / lives /
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
    // Song Selection uses (SongSelectionService + CatalogSongSource), without showing the selector.
    private System.Collections.IEnumerator ContinueRoutine()
    {
        // Lap finished: CONTINUE starts the next lap directly (its prompt was already offered once).
        // New lap waiting for its first song: CONTINUE opens Song Selection (BACK returns here, still pending).
        if (CampaignService.LapFinished) { StartNewLapAndChoose(); yield break; }
        if (CampaignService.FirstSongPending) { AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongSelection); yield break; }
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

        // The NEW selection is recorded FIRST (a catalog selection loads nothing — the readiness barrier does),
        // and only then SongAnalysis is entered: entering it while the previous song was still the selection
        // made the preparation start on that old song, which the new selection then superseded.
        _songService ??= GetComponent<SongSelectionService>() ?? gameObject.AddComponent<SongSelectionService>();
        bool selected = false;
        yield return _songService.SelectSong(new CatalogSongSource(songId, SongCatalog.DisplayNameFor(songId)), null, ok => selected = ok);
        _continuing = false;
        if (!selected)
        {
            Debug.LogWarning($"[MainMenuController] CONTINUE: '{songId}' could not be selected - staying on the Main Menu.");
            SetMenuStatus(Loc.Get("SongSelection.LoadFailed"));
            yield break;
        }
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongAnalysis);
    }

    private void OnRivalsClicked() => RivalCollectionController.Instance?.Open();
    private void OnProgressionClicked() => ProgressionScreenController.Instance?.Open();

    // ── Campaign moments (a safe point: nothing is playing) ─────────────────────

    private CampaignPopup Popup => _popup != null ? _popup : _popup = CampaignPopup.Spawn("CampaignPopup");

    /// <summary>The next one-time campaign notice, if any (reveal, unlock, finale, seal). Marked shown when displayed.</summary>
    private void ShowNextNotice()
    {
        if (Popup == null || Popup.IsOpen) return; // no prefab: logged once by CampaignPopup.Spawn
        var notice = CampaignService.PendingNotice();
        if (notice == CampaignService.Notice.None) return;
        CampaignService.MarkShown(notice);
        var boss = CampaignService.Boss;
        var silhouette = boss != null ? (boss.silhouette != null ? boss.silhouette : boss.LevelConfig(0)?.portrait) : null;
        switch (notice)
        {
            case CampaignService.Notice.CampaignCompleted:
                CampaignService.MarkLapPromptShown(); // this notice IS the finished lap's prompt
                Popup.Show(Loc.Get("Campaign.CompletedTitle"), Loc.Get("Campaign.CompletedBody"), silhouette, Color.black,
                           Loc.Get("Campaign.NewLap"), StartNewLapAndChoose, Loc.Get("Campaign.Later"), ShowNextNotice);
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

    /// <summary>A finished lap, shown ONCE (on the menu after the lap's last win): next lap or later. "Later" leaves the
    /// campaign pending; CONTINUE then starts the next lap without asking again (CampaignService.LapPromptPending).</summary>
    private void ShowLapFinished()
    {
        if (Popup == null || Popup.IsOpen) return;
        if (CampaignService.PendingNotice() == CampaignService.Notice.CampaignCompleted) { ShowNextNotice(); return; }
        CampaignService.MarkLapPromptShown();
        var data = CampaignService.Data;
        Popup.Show(Loc.Get("Campaign.LapDoneTitle", data != null ? data.lap.ToString() : "1"), Loc.Get("Campaign.LapDoneBody"), null, Color.white,
                   Loc.Get("Campaign.NewLap"), StartNewLapAndChoose, Loc.Get("Campaign.Later"), null);
    }

    /// <summary>Starts the next lap (once — guarded in CampaignService) and opens Song Selection for its first song.</summary>
    private void StartNewLapAndChoose()
    {
        CampaignService.StartNewLap();
        RefreshProgressUI();
        if (CampaignService.FirstSongPending) AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongSelection);
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

    private static void BindChannel(UnityEngine.UI.Slider slider, GameAudioChannel channel)
    {
        if (slider == null || GameAudio.Instance == null) return;
        slider.value = GameAudio.Instance.GetVolume(channel);
        slider.onValueChanged.AddListener(v => GameAudio.Instance?.SetVolume(channel, v));
    }

    private void OnVolumeChanged(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat(VolumePrefKey, value);
    }
}
