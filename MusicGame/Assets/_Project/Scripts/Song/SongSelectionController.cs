using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Song Selection screen — lists SongCatalogSO's entries, "PLAY YOUR SONG" (local file, via the
/// existing ILocalSongPicker abstraction), and three disabled placeholder streaming buttons
/// (Spotify/YouTube Music/Amazon Music — Section 9 of the multi-scene refactor plan: no real APIs
/// yet, just a clear "Coming Soon" seam for later).
///
/// IMPORTANT (Section 9): selecting a row or picking a local file only records/highlights the
/// selection — it never starts anything by itself. A separate PLAY button is what actually writes
/// GameSession.SelectedSong and advances the flow to GameFlowState.SongAnalysis.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// GameFlowStateChangedEvent directly, same pattern as the other Frontend screens. Never touches an
/// AudioSource itself: RunnerSceneBootstrap already re-applies GameSession.SelectedSong.Clip onto
/// its own local AudioSource right before starting analysis, so this screen only ever needs to set
/// GameSession.SelectedSong — no cross-scene AudioSource reference required.
/// </summary>
public class SongSelectionController : ThemeReceiverBehaviour
{
    private static readonly Color NeutralStatusColor = new(0.75f, 0.75f, 0.75f);
    private static readonly Color ErrorStatusColor   = new(0.85f, 0.35f, 0.3f);

    private RectTransform _root;
    private Image _dim;
    private Text  _titleText;
    private RectTransform _songListRoot;
    private Text  _playButtonLabel;
    private Button _playButton;
    private Text  _statusText;

    private SongCatalogSO _catalog;
    private SongSelectionService _service;

    private readonly List<Image> _rowBackgrounds = new();
    private int _selectedCatalogIndex = -1;
    private SelectedSongInfo? _selectedLocalInfo;
    private bool _isLoading;

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _catalog = appConfig != null && appConfig.song != null ? appConfig.song.catalog : null;
        _service = gameObject.AddComponent<SongSelectionService>();

        Build();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _onFlowStateChanged = e =>
        {
            if (e.Current == GameFlowState.SongSelection) Show();
            else if (e.Previous == GameFlowState.SongSelection) Hide();
        };
        EventBus.Subscribe(_onFlowStateChanged);

        // Same UI-Scene-loads-asynchronously race as the other Frontend screens.
        if (AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.SongSelection)
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
        RefreshRowHighlight(ui.accentColor);
    }

    // ── Build (runtime-only, no prefab) ────────────────────────────────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("SongSelectionScreen", canvas);
        UIFactory.Stretch(_root);
        _root.SetAsLastSibling();

        _dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(_dim.rectTransform);

        _titleText = UIFactory.CreateText("Title", _root, Loc.Get("SongSelection.Title"), 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIFactory.SetBox(_titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -50f), new Vector2(900f, 50f));

        BuildSongList();
        BuildStreamingRow();
        BuildBottomBar();

        _root.gameObject.SetActive(false);
    }

    private void BuildSongList()
    {
        _songListRoot = UIFactory.CreateRect("SongList", _root);
        UIFactory.SetBox(_songListRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -120f), new Vector2(560f, 260f));

        float rowH = 56f, gap = 10f, y = 0f;

        if (_catalog != null)
        {
            for (int i = 0; i < _catalog.songs.Length; i++)
            {
                int index = i; // capture
                var def = _catalog.songs[i];

                var row = UIFactory.CreateButton("SongRow_" + def.id, _songListRoot, "", out var label);
                UIFactory.SetBox(row.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(560f, rowH));
                label.text = $"{def.title}  —  {def.artist}";
                label.alignment = TextAnchor.MiddleLeft;
                UIFactory.SetBox(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-24f, 0f));

                var background = row.GetComponent<Image>();
                _rowBackgrounds.Add(background);
                row.onClick.AddListener(() => SelectCatalogSong(index));

                y -= rowH + gap;
            }
        }

        var playYourSongBtn = UIFactory.CreateButton("PlayYourSongButton", _songListRoot, Loc.Get("SongSelection.PlayYourSong"), out _);
        UIFactory.SetBox(playYourSongBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(560f, rowH));
        playYourSongBtn.onClick.AddListener(OnPlayYourSongClicked);
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
        }
    }

    private void BuildBottomBar()
    {
        var backBtn = UIFactory.CreateButton("BackButton", _root, Loc.Get("SongSelection.Back"), out _);
        UIFactory.SetBox(backBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-150f, 40f), new Vector2(180f, 44f));
        backBtn.onClick.AddListener(() => AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu));

        var playBtn = UIFactory.CreateButton("PlayButton", _root, Loc.Get("SongSelection.Play"), out _playButtonLabel);
        UIFactory.SetBox(playBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(150f, 40f), new Vector2(180f, 44f));
        playBtn.onClick.AddListener(OnPlayClicked);
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
        // come first, in the same order as _catalog.songs.
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

        var definition = _catalog.songs[_selectedCatalogIndex];
        _isLoading = true;
        RefreshPlayButtonInteractable();
        SetStatus(Loc.Get("SongSelection.Loading"));

        StartCoroutine(_service.SelectSong(new SongCatalogSource(definition), null, success =>
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
