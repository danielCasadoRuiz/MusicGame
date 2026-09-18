using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.UI;

/// <summary>
/// Song Selection screen — lists every audio clip tagged with the "Song" Addressables label (see
/// SongAddressablesSetup — no hand-authored catalog ScriptableObject any more: dropping a new clip
/// into Assets/_Project/Audio/Music and re-running that tool is the whole "add a song" workflow, and
/// removing one is the same in reverse), "PLAY YOUR SONG" (local file, via the existing
/// ILocalSongPicker abstraction), and three disabled placeholder streaming buttons (Spotify/YouTube
/// Music/Amazon Music — Section 9 of the multi-scene refactor plan: no real APIs yet, just a clear
/// "Coming Soon" seam for later).
///
/// IMPORTANT (Section 9): selecting a row or picking a local file only records/highlights the
/// selection — it never starts anything by itself. A separate PLAY button is what actually writes
/// GameSession.SelectedSong and advances the flow to GameFlowState.SongAnalysis.
///
/// Mostly a pure SCREEN CONTROLLER — static children (title, dim, buttons) carry generic receivers
/// (ThemeColorReceiver/ThemeTextReceiver). The one exception is the song-row SELECTION highlight:
/// which row is tinted depends on INTERACTION state (which row is currently selected), not just the
/// theme, so a generic receiver can't express it — this class keeps that one piece directly and
/// re-applies it on ThemeChangedEvent too, so the highlight's accent color never goes stale after a
/// Theme swap.
///
/// Prefers a real SongSelection.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) for the STATIC chrome only — the catalog rows themselves
/// stay dynamic runtime population into the prefab's (initially empty) songListRoot container either
/// way, exactly as before. Falls back to the old fully-procedural build only if no prefab is wired.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// GameFlowStateChangedEvent directly, same pattern as the other Frontend screens. Never touches an
/// AudioSource itself: RunnerSceneBootstrap already re-applies GameSession.SelectedSong.Clip onto
/// its own local AudioSource right before starting analysis, so this screen only ever needs to set
/// GameSession.SelectedSong — no cross-scene AudioSource reference required.
/// </summary>
public class SongSelectionController : MonoBehaviour
{
    private static readonly Color NeutralStatusColor = new(0.75f, 0.75f, 0.75f);
    private static readonly Color ErrorStatusColor   = new(0.85f, 0.35f, 0.3f);

    private RectTransform _root;
    private RectTransform _songListRoot;
    private TextMeshProUGUI _playButtonLabel;
    private Button _playButton;
    private TextMeshProUGUI _statusText;

    private const string SongLabel = "Song";

    private readonly List<IResourceLocation> _entries = new();
    private SongSelectionService _service;

    private readonly List<Image> _rowBackgrounds = new();
    private int _selectedCatalogIndex = -1;
    private SelectedSongInfo? _selectedLocalInfo;
    private bool _isLoading;

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;
    private System.Action<ThemeChangedEvent> _onThemeChanged;

    private void Awake()
    {
        _service = gameObject.AddComponent<SongSelectionService>();

        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.SongSelection != null) WireUI(registry.SongSelection);
        else Build();

        StartCoroutine(LoadCatalogAndPopulate());
    }

    private void OnEnable()
    {
        _onFlowStateChanged = e =>
        {
            if (e.Current == GameFlowState.SongSelection) Show();
            else if (e.Previous == GameFlowState.SongSelection) Hide();
        };
        EventBus.Subscribe(_onFlowStateChanged);

        // The row-selection highlight is interaction state, not pure theming, so it isn't a generic
        // receiver — but its color IS a theme token, so it still needs to react when Theme changes
        // (Section 15: never go stale) — a plain re-tint, no interpolation needed for this secondary
        // highlight refresh.
        _onThemeChanged = _ => RefreshRowHighlight(CurrentAccentColor());
        EventBus.Subscribe(_onThemeChanged);

        // Same UI-Scene-loads-asynchronously race as the other Frontend screens.
        if (AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.SongSelection)
            Show();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
        EventBus.Unsubscribe(_onThemeChanged);
    }

    // ── Prefab path — see SongSelectionView's own doc ────────────────────────────

    private void WireUI(SongSelectionView view)
    {
        _root            = view.root.GetComponent<RectTransform>();
        _songListRoot    = view.songListRoot;
        _playButton      = view.playButton;
        _playButtonLabel = view.playButtonLabel;
        _statusText      = view.statusText;

        view.backButton.onClick.AddListener(() => AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu));
        view.playButton.onClick.AddListener(OnPlayClicked);

        // Baked once at Editor-bake time — re-apply from the current locale here, same reasoning as
        // GameplayHUD/PauseController's own labels.
        view.titleText.text         = Loc.Get("SongSelection.Title");
        view.backButtonLabel.text   = Loc.Get("SongSelection.Back");
        view.playButtonLabel.text   = Loc.Get("SongSelection.Play");
        view.spotifyLabel.text      = Loc.Get("SongSelection.Spotify") + " (" + Loc.Get("SongSelection.ComingSoon") + ")";
        view.youtubeMusicLabel.text = Loc.Get("SongSelection.YouTubeMusic") + " (" + Loc.Get("SongSelection.ComingSoon") + ")";
        view.amazonMusicLabel.text  = Loc.Get("SongSelection.AmazonMusic") + " (" + Loc.Get("SongSelection.ComingSoon") + ")";
        _statusText.text = "";

        // The catalog rows stay dynamic runtime population either way (Section 9 of the plan) — the
        // prefab only supplies the empty container they get added into.
        BuildSongList();

        _root.gameObject.SetActive(false);
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("SongSelectionScreen", canvas);
        UIFactory.Stretch(_root);
        _root.SetAsLastSibling();

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var title = UIFactory.CreateText("Title", _root, Loc.Get("SongSelection.Title"), 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -50f), new Vector2(900f, 50f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        BuildSongList();
        BuildStreamingRow();
        BuildBottomBar();

        _root.gameObject.SetActive(false);
    }

    // Ensures _songListRoot exists — dynamic runtime population either way (Section 9 of the plan),
    // so this runs identically whether _songListRoot came from the prefab (WireUI) or needs
    // creating here (procedural Build fallback). Row population itself happens later, once the
    // Addressables catalog query resolves — see LoadCatalogAndPopulate/PopulateSongRows.
    private void BuildSongList()
    {
        if (_songListRoot == null)
        {
            _songListRoot = UIFactory.CreateRect("SongList", _root);
            UIFactory.SetBox(_songListRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -120f), new Vector2(560f, 260f));
        }
    }

    // Queries every asset tagged with the "Song" Addressables label — this IS the catalog now,
    // there is no hand-authored list to fall back to. Resolves near-instantly (it's a local catalog
    // lookup, not a real download) — the screen is still hidden at this point in virtually every
    // real case (reaching SongSelection requires the player to get through Intro/MainMenu first),
    // so the brief async gap is never actually visible.
    private IEnumerator LoadCatalogAndPopulate()
    {
        AsyncOperationHandle<IList<IResourceLocation>> handle =
            Addressables.LoadResourceLocationsAsync(SongLabel, typeof(AudioClip));
        yield return handle;

        _entries.Clear();
        if (handle.Status == AsyncOperationStatus.Succeeded)
            _entries.AddRange(handle.Result);
        else
            Debug.LogWarning("[SongSelectionController] Failed to query the 'Song' Addressables label.");
        Addressables.Release(handle);

        PopulateSongRows();
        RefreshPlayButtonInteractable();
    }

    // Builds one row per catalog entry, in the order Addressables returned them, followed by the
    // always-present "PLAY YOUR SONG" row — never called more than once per screen lifetime (this
    // controller doesn't support the catalog changing while the screen is already up).
    private void PopulateSongRows()
    {
        float rowH = 56f, gap = 10f, y = 0f;

        for (int i = 0; i < _entries.Count; i++)
        {
            int index = i; // capture
            var location = _entries[i];

            var row = UIFactory.CreateButton("SongRow_" + i, _songListRoot, "", out var label);
            UIFactory.SetBox(row.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(560f, rowH));
            label.text = location.PrimaryKey; // the Addressable's own address — set once, in the Editor tool, to a friendly display name
            label.alignment = TextAlignmentOptions.Left;
            UIFactory.SetBox(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-24f, 0f));
            label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

            // Row BACKGROUND is deliberately NOT a ThemeColorReceiver — its color depends on
            // selection state (see RefreshRowHighlight), not just the theme.
            var background = row.GetComponent<Image>();
            _rowBackgrounds.Add(background);
            row.onClick.AddListener(() => SelectCatalogSong(index));

            y -= rowH + gap;
        }

        var playYourSongBtn = UIFactory.CreateButton("PlayYourSongButton", _songListRoot, Loc.Get("SongSelection.PlayYourSong"), out var playYourSongLabel);
        UIFactory.SetBox(playYourSongBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(560f, rowH));
        playYourSongBtn.onClick.AddListener(OnPlayYourSongClicked);
        playYourSongLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        _rowBackgrounds.Add(playYourSongBtn.GetComponent<Image>());
    }

    private void BuildStreamingRow()
    {
        var row = UIFactory.CreateRect("StreamingRow", _root);
        UIFactory.SetBox(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 150f), new Vector2(560f, 44f));

        string[] keys = { "SongSelection.Spotify", "SongSelection.YouTubeMusic", "SongSelection.AmazonMusic" };
        float w = (560f - 2f * 10f) / 3f;
        for (int i = 0; i < keys.Length; i++)
        {
            var btn = UIFactory.CreateButton("Streaming_" + keys[i], row, Loc.Get(keys[i]) + " (" + Loc.Get("SongSelection.ComingSoon") + ")", out var label);
            label.fontSize = 11;
            UIFactory.SetBox(btn.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(i * (w + 10f), 0f), new Vector2(w, 0f));
            btn.interactable = false;
            btn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
            label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        }
    }

    private void BuildBottomBar()
    {
        var backBtn = UIFactory.CreateButton("BackButton", _root, Loc.Get("SongSelection.Back"), out var backLabel);
        UIFactory.SetBox(backBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-150f, 40f), new Vector2(180f, 44f));
        backBtn.onClick.AddListener(() => AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu));
        backBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        backLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        var playBtn = UIFactory.CreateButton("PlayButton", _root, Loc.Get("SongSelection.Play"), out _playButtonLabel);
        UIFactory.SetBox(playBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(150f, 40f), new Vector2(180f, 44f));
        playBtn.onClick.AddListener(OnPlayClicked);
        playBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        _playButtonLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        _playButton = playBtn;

        _statusText = UIFactory.CreateText("Status", _root, "", 12, NeutralStatusColor);
        UIFactory.SetBox(_statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 90f), new Vector2(500f, 24f));
    }

    // ── Show / hide ─────────────────────────────────────────────────────────────

    private void Show()
    {
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        RefreshPlayButtonInteractable();
    }

    private void Hide()
    {
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // ── Selection ───────────────────────────────────────────────────────────────

    private void SelectCatalogSong(int index)
    {
        _selectedCatalogIndex = index;
        _selectedLocalInfo = null;
        RefreshRowHighlight(CurrentAccentColor());
        RefreshPlayButtonInteractable();
        SetStatus(null);
    }

    private void OnPlayYourSongClicked()
    {
        var source = new LocalFileSongSource(LocalSongPickerFactory.Create());
        StartCoroutine(source.Load(info =>
        {
            if (info == null)
            {
                SetStatus(Loc.Get("SongSelection.LocalFileCancelled"), ErrorStatusColor);
                return;
            }
            _selectedLocalInfo = info;
            _selectedCatalogIndex = -1;
            RefreshRowHighlight(CurrentAccentColor());
            RefreshPlayButtonInteractable();
            SetStatus(Loc.Get("SongSelection.LocalFileSelected", info.Value.DisplayName), NeutralStatusColor);
        }));
    }

    private void RefreshRowHighlight(Color accent)
    {
        // Local-file "row" is the last one in _rowBackgrounds (PlayYourSongButton); catalog rows
        // come first, in the same order as _entries.
        for (int i = 0; i < _rowBackgrounds.Count; i++)
        {
            bool isLocalFileRow = i == _rowBackgrounds.Count - 1;
            bool selected = isLocalFileRow ? _selectedLocalInfo.HasValue : i == _selectedCatalogIndex;
            _rowBackgrounds[i].color = selected ? new Color(accent.r, accent.g, accent.b, 0.35f) : new Color(1f, 1f, 1f, 0.12f);
        }
    }

    private Color CurrentAccentColor() =>
        ThemeManager.Instance != null && ThemeManager.Instance.CurrentTheme != null && ThemeManager.Instance.CurrentTheme.UI != null
            ? ThemeManager.Instance.CurrentTheme.UI.accentColor
            : new Color(0.18f, 0.75f, 0.95f);

    private bool HasSelection => _selectedCatalogIndex >= 0 || _selectedLocalInfo.HasValue;

    private void RefreshPlayButtonInteractable()
    {
        if (_playButton != null) _playButton.interactable = HasSelection && !_isLoading;
    }

    private void SetStatus(string text, Color? color = null)
    {
        if (_statusText == null) return;
        _statusText.text  = text ?? "";
        _statusText.color = color ?? NeutralStatusColor;
    }

    // ── PLAY (the only thing that actually advances the flow) ───────────────────

    private void OnPlayClicked()
    {
        if (!HasSelection || _isLoading) return;

        if (_selectedLocalInfo.HasValue)
        {
            if (GameSession.Instance != null) GameSession.Instance.SelectedSong = _selectedLocalInfo;
            AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongAnalysis);
            return;
        }

        var location = _entries[_selectedCatalogIndex];
        _isLoading = true;
        RefreshPlayButtonInteractable();
        SetStatus(Loc.Get("SongSelection.Loading"));

        StartCoroutine(_service.SelectSong(new AddressableSongSource(location), null, success =>
        {
            _isLoading = false;
            if (success)
            {
                AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongAnalysis);
            }
            else
            {
                RefreshPlayButtonInteractable();
                SetStatus(Loc.Get("SongSelection.LoadFailed"), ErrorStatusColor);
            }
        }));
    }
}
