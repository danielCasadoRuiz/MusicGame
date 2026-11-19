using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class JukeboxController : MonoBehaviour
{
    public static JukeboxController Instance { get; private set; }
    private JukeboxView _view;
    private JukeboxPlayer _player;
    private List<JukeboxTrack> _tracks = new();
    private readonly List<JukeboxRowView> _rows = new();
    private JukeboxTrack _selected;
    private bool _busy, _opened;
    private Action<GameFlowStateChangedEvent> _onFlow;
    private Coroutine _refresh;
    private int _generation;
    private GameObject _menuRoot;
    private MusicProduct? _purchaseProduct;
    private const string LyricsPreference = "Jukebox.ShowLyrics";
    private bool _lyricsWanted;
    private string _renderedLyricsId;
    private readonly List<TextMeshProUGUI> _lyricLines = new();
    private int _activeLyric = -1;
    private float _nextLyricsUpdate;

    private void Awake()
    {
        Instance = this;
        _player = gameObject.AddComponent<JukeboxPlayer>();
        _lyricsWanted = PlayerPrefs.GetInt(LyricsPreference, 0) == 1;
        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.jukebox : null, UIFactory.RootCanvas(), "Jukebox");
        if (_view == null) { enabled = false; return; }
        UIFactory.Stretch((RectTransform)_view.transform);
        _view.back.onClick.AddListener(Close);
        _view.preview.onClick.AddListener(() => Play(false));
        _view.full.onClick.AddListener(() => Play(true));
        _view.previous.onClick.AddListener(() => Skip(-1));
        _view.next.onClick.AddListener(() => Skip(1));
        _view.playPause.onClick.AddListener(() => _player.TogglePause());
        _view.lyrics.onClick.AddListener(ShowLyrics);
        _view.delete.onClick.AddListener(DeleteSelected);
        _view.background.onClick.AddListener(OnBackground);
        _view.cancel.onClick.AddListener(() => _view.dialog.SetActive(false));
        _player.Changed += OnPlayerChanged;
        _player.Failed += Status;
        _player.Status += Status;
        _view.gameObject.SetActive(false);
    }
    private void OnEnable()
    {
        _onFlow = e => { if (e.Previous == GameFlowState.MainMenu) Close(); };
        EventBus.Subscribe(_onFlow);
        MusicAccess.StoreRequested += ShowStore;
        MusicAccess.Changed += RefreshControls;
        Loc.Refreshed += RefreshLanguage;
    }
    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlow);
        MusicAccess.StoreRequested -= ShowStore;
        MusicAccess.Changed -= RefreshControls;
        Loc.Refreshed -= RefreshLanguage;
        Close();
    }
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_player != null) { _player.Changed -= OnPlayerChanged; _player.Failed -= Status; _player.Status -= Status; Destroy(_player); }
        if (_view != null) Destroy(_view.gameObject);
    }
    public void Open()
    {
        if (_view == null || _opened) return;
        _opened = true;
        _view.gameObject.SetActive(true); _view.transform.SetAsLastSibling();
        // No menu input/animation continues underneath the music library.
        var menu = FindFirstObjectByType<MainMenuView>();
        _menuRoot = menu != null ? menu.root : null;
        if (_menuRoot != null) _menuRoot.SetActive(false);
        _view.dialog.SetActive(false);
        Status("Jukebox.Loading");
        _refresh = StartCoroutine(Refresh());
    }
    public void Close()
    {
        if (!_opened) return;
        _generation++;
        _opened = false; _busy = false;
        if (_refresh != null) { StopCoroutine(_refresh); _refresh = null; }
        _player?.Stop();
        if (_view != null) _view.gameObject.SetActive(false);
        bool inMenu = AppBootstrap.Context?.AppFlow.CurrentState == GameFlowState.MainMenu;
        if (_menuRoot != null && inMenu) _menuRoot.SetActive(true);
    }
    private void Update()
    {
        if (!_opened) return;
        UpdateLyricsHighlight();
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            if (_view.dialog.activeSelf) _view.dialog.SetActive(false);
            else if (_view.lyricsPanel != null && _view.lyricsPanel.activeSelf) ShowLyrics();
            else if (!_busy) Close();
        }
    }
    private IEnumerator Refresh()
    {
        yield return SongCatalogService.EnsureReady();
        if (!_opened) yield break;
        Rebuild(); Status("Jukebox.Hint"); _refresh = null;
    }
    private void Rebuild()
    {
        string selectedId = _selected?.id;
        _tracks = JukeboxTrack.Build();
        foreach (var row in _rows) if (row != null) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
        _rows.Clear();
        var lib = UIPrefabLibrarySO.Instance;
        int unlocked = 0;
        foreach (var track in _tracks)
        {
            var row = UIPrefabLibrarySO.Spawn(lib != null ? lib.jukeboxRow : null, _view.list, "JukeboxRow");
            if (row == null) break;
            bool won = track.Unlocked;
            if (won) unlocked++;
            row.title.text = track.title;
            row.detail.text = Detail(track);
            row.state.text = Loc.Get(won ? "Jukebox.Unlocked" : track.Imported ? "Jukebox.WinToUnlock" : "Jukebox.Locked");
            row.cover.sprite = track.cover; row.cover.gameObject.SetActive(track.cover != null);
            row.button.onClick.AddListener(() => { if (_busy) return; Select(track); Play(false); });
            _rows.Add(row);
        }
        _view.summary.text = Loc.Get("Jukebox.Title") + " · " + unlocked.ToString();
        Select(_tracks.Find(t => t.id == selectedId) ?? (_tracks.Count > 0 ? _tracks[0] : null));
    }
    private static string Detail(JukeboxTrack track)
    {
        string origin = track.Imported ? Loc.Get("Jukebox.Imported") : Loc.Get("Jukebox.Original");
        string style = track.style == GameMusicStyle.Unknown ? Loc.Get("Jukebox.UnknownStyle") : Loc.Get("GameStyle." + track.style);
        return (string.IsNullOrEmpty(track.artist) ? "" : track.artist + "  ·  ") + style + "  ·  " + origin;
    }
    private void Select(JukeboxTrack track)
    {
        _selected = track;
        _view.selected.text = track == null ? Loc.Get("Jukebox.Empty") : track.title + "\n" + Detail(track);
        _view.cover.sprite = track?.cover; _view.cover.gameObject.SetActive(track?.cover != null);
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].background.color = _tracks[i] == track ? new Color(0.12f, 0.5f, 0.62f, 0.8f) : new Color(0.12f, 0.15f, 0.2f, 0.9f);
        RefreshControls();
        if (track == null || track.Imported || track.lyricData != null) return;
        SongLyrics.Load(track.id, data =>
        {
            if (data == null) return;
            track.lyricData = data;
            track.lyrics = data.PlainText;
            if (this != null && _opened && _selected == track)
            {
                _renderedLyricsId = null;
                RefreshControls();
            }
        });
    }
    private void OnPlayerChanged()
    {
        if (_player.Current != null && _player.Current.id != _selected?.id) Select(_player.Current);
        else RefreshControls();
    }
    private void Skip(int direction)
    {
        if (_busy || _tracks.Count < 2) return;
        bool full = _player.FullPlayback && MusicAccess.Jukebox;
        if (_player.Playing && full) { _player.Skip(direction); return; }
        int index = _tracks.FindIndex(t => t.id == _selected?.id);
        index = (index + direction + _tracks.Count) % _tracks.Count;
        Select(_tracks[index]);
        Play(full);
    }
    private void RefreshControls()
    {
        if (_view == null) return;
        bool won = _selected?.Unlocked == true;
        if (_view.nowPlaying != null) _view.nowPlaying.text = _player.Current == null ? "" : Loc.Get("Jukebox.NowPlaying", _player.Current.title);
        _view.preview.interactable = won && !_busy;
        _view.full.interactable = won && !_busy;
        var fullLabel = _view.full.GetComponentInChildren<TextMeshProUGUI>(true);
        Loc.Set(fullLabel, MusicAccess.Jukebox ? "Jukebox.Full" : "Jukebox.FullPremium");
        if (_purchaseProduct.HasValue && _view.dialog.activeSelf)
            _view.confirm.interactable = MusicAccess.Store != null;
        bool hasLyrics = _selected != null && !_selected.Imported && !string.IsNullOrWhiteSpace(_selected.lyrics);
        _view.lyrics.gameObject.SetActive(_selected != null && !_selected.Imported);
        _view.lyrics.interactable = won && !_busy && hasLyrics;
        var lyricsLabel = _view.lyrics.GetComponentInChildren<TextMeshProUGUI>(true);
        bool showingLyrics = hasLyrics && MusicAccess.Jukebox && _lyricsWanted && _player.Current?.id == _selected.id;
        Loc.Set(lyricsLabel, !MusicAccess.Jukebox ? "Jukebox.LyricsPremium" : showingLyrics ? "Jukebox.HideLyrics" : "Jukebox.Lyrics");
        var lyricColors = _view.lyrics.colors;
        lyricColors.normalColor = MusicAccess.Jukebox ? Color.white : new Color(.6f, .6f, .65f);
        _view.lyrics.colors = lyricColors;
        if (_view.lyricsLock != null) _view.lyricsLock.SetActive(!MusicAccess.Jukebox);
        RefreshLyricsPanel(showingLyrics);
        _view.delete.gameObject.SetActive(_selected?.Imported == true);
        _view.delete.interactable = !_busy;
        _view.previous.interactable = _tracks.Count > 1 && !_busy;
        _view.next.interactable = _view.previous.interactable;
        _view.playPause.interactable = _player.Playing && !_player.Loading && !_busy;
        Loc.Set(_view.playLabel, _player.Paused ? "Jukebox.Resume" : "Jukebox.Pause");
        Loc.Set(_view.backgroundLabel, _player.BackgroundEnabled ? "Jukebox.BackgroundOn" : "Jukebox.Background");
    }
    private void Play(bool full)
    {
        if (_selected == null || !_selected.Unlocked || _busy) return;
        if (full && !MusicAccess.Jukebox) { MusicAccess.Request(MusicProduct.Jukebox); return; }
        _player.Play(_selected, full, _tracks);
        Status(full ? "Jukebox.PlayingFull" : "Jukebox.PlayingPreview");
    }
    private void ShowLyrics()
    {
        if (_selected == null || _selected.Imported || !_selected.Unlocked || string.IsNullOrEmpty(_selected.lyrics)) return;
        if (!MusicAccess.Jukebox) { MusicAccess.Request(MusicProduct.Jukebox); return; }
        _lyricsWanted = !_lyricsWanted;
        PlayerPrefs.SetInt(LyricsPreference, _lyricsWanted ? 1 : 0);
        PlayerPrefs.Save();
        RefreshControls();
    }
    private void RefreshLyricsPanel(bool show)
    {
        if (_view.lyricsPanel == null || _view.libraryPanel == null || _view.lyricsLine == null) return;
        _view.lyricsPanel.SetActive(show); _view.libraryPanel.SetActive(!show);
        if (!show || _renderedLyricsId == _selected.id) return;
        foreach (var label in _lyricLines) if (label != null) { label.gameObject.SetActive(false); Destroy(label.gameObject); }
        _lyricLines.Clear(); _activeLyric = -1; _nextLyricsUpdate = 0;
        _renderedLyricsId = _selected.id;
        var data = _selected.lyricData;
        var texts = data?.lines != null ? Array.ConvertAll(data.lines, line => line.text) : _selected.lyrics.Split('\n');
        foreach (string text in texts)
        {
            var label = Instantiate(_view.lyricsLine, _view.lyricsContent);
            label.name = "LyricLine"; label.text = text; label.richText = false;
            label.color = new Color(.7f, .73f, .78f); label.fontStyle = FontStyles.Normal;
            label.gameObject.SetActive(true); _lyricLines.Add(label);
        }
        Canvas.ForceUpdateCanvases(); _view.lyricsScroll.verticalNormalizedPosition = 1;
    }
    private void UpdateLyricsHighlight()
    {
        if (_view.lyricsPanel == null || !_view.lyricsPanel.activeSelf || !MusicAccess.Jukebox ||
            _player.Current?.id != _selected?.id || _player.Loading || Time.unscaledTime < _nextLyricsUpdate) return;
        _nextLyricsUpdate = Time.unscaledTime + .1f;
        int active = _selected.lyricData?.LineAt(_player.PositionSeconds) ?? -1;
        if (active == _activeLyric) return;
        if (_activeLyric >= 0 && _activeLyric < _lyricLines.Count)
        {
            _lyricLines[_activeLyric].color = new Color(.7f, .73f, .78f);
            _lyricLines[_activeLyric].fontStyle = FontStyles.Normal;
        }
        _activeLyric = active;
        if (active < 0 || active >= _lyricLines.Count) return;
        var label = _lyricLines[active]; label.color = new Color(.4f, 1f, .88f); label.fontStyle = FontStyles.Bold;
        Canvas.ForceUpdateCanvases();
        float range = _view.lyricsContent.rect.height - _view.lyricsScroll.viewport.rect.height;
        if (range > 0)
            _view.lyricsScroll.verticalNormalizedPosition = 1 - Mathf.Clamp01((-label.rectTransform.anchoredPosition.y - _view.lyricsScroll.viewport.rect.height * .4f) / range);
    }
    private void DeleteSelected()
    {
        var track = _selected;
        if (track?.Imported != true || _busy) return;
        Dialog(Loc.Get("Jukebox.Delete"), Loc.Get("Jukebox.DeleteBody", track.title), "Jukebox.Delete", () =>
        {
            _player.RemoveTrack(track.id);
            try { ImportedAudioLibrary.Delete(track.id); Rebuild(); Status("Jukebox.Deleted"); }
            catch (Exception e) { Debug.LogWarning(e.Message); Status("Jukebox.DeleteFailed"); }
        });
    }
    private void OnBackground()
    {
        if (!MusicAccess.Background) { MusicAccess.Request(MusicProduct.Jukebox); return; }
        StartCoroutine(EnableBackground());
    }
    private IEnumerator EnableBackground()
    {
        yield return _player.EnableBackground(_tracks);
        RefreshControls();
    }
    private void ShowStore(MusicProduct? product)
    {
        if (_view == null) return;
        // Requests from Song Selection can show this dialog without opening the library.
        bool standalone = !_opened;
        if (standalone) { _view.gameObject.SetActive(true); _view.transform.SetAsLastSibling(); }
        MusicProduct requested = product ?? MusicProduct.Jukebox;
        string price = Loc.Get("Jukebox.PurchasePricePending");
        string body = Product(requested, price) + "\n\n" +
            Loc.Get(MusicAccess.Store != null ? "Jukebox.PurchaseSimulation" : "Jukebox.PurchaseUnavailable") +
            "\n\n" + Loc.Get("Jukebox.PurchaseTwoProductsNote");
        Dialog(Loc.Get(ProductKey(requested)), body,
            MusicAccess.Store != null ? "Jukebox.BuySimulation" : "Jukebox.Buy",
            () => MusicAccess.ConfirmPurchase(requested));
        _purchaseProduct = requested;
        _view.confirm.interactable = MusicAccess.Store != null;
        _view.cancel.onClick.RemoveAllListeners();
        _view.cancel.onClick.AddListener(() => { _view.dialog.SetActive(false); if (standalone) _view.gameObject.SetActive(false); });
    }
    private static string Product(MusicProduct product, string price)
    {
        bool owned = product == MusicProduct.Jukebox ? MusicAccess.Jukebox : MusicAccess.CustomAudio;
        return Loc.Get(ProductKey(product)) + " · " + (owned ? Loc.Get("Jukebox.Owned") : price);
    }
    private static string ProductKey(MusicProduct product) => product == MusicProduct.Jukebox ? "Jukebox.PurchasePremiumProduct" : "Jukebox.Product.CustomAudio";
    private void Dialog(string title, string body, string confirmKey, Action confirm)
    {
        _purchaseProduct = null;
        _view.confirm.interactable = true;
        _view.dialogTitle.text = title; _view.dialogBody.text = body;
        _view.confirm.onClick.RemoveAllListeners();
        _view.confirm.gameObject.SetActive(confirm != null);
        if (confirm != null)
        {
            Loc.Set(_view.confirmLabel, confirmKey);
            _view.confirm.onClick.AddListener(() => { _view.dialog.SetActive(false); confirm(); });
        }
        _view.cancel.onClick.RemoveAllListeners();
        _view.cancel.onClick.AddListener(() => _view.dialog.SetActive(false));
        _view.dialog.SetActive(true); _view.dialog.transform.SetAsLastSibling();
    }
    private void Status(string key) { if (_view != null) Loc.Set(_view.status, key); }
    private void RefreshLanguage() { if (_opened) Rebuild(); }
}
