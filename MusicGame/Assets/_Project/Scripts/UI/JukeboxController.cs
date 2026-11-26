using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private string _finishedPreviewId;
    private bool _busy, _opened;
    private Action<GameFlowStateChangedEvent> _onFlow;
    private Coroutine _refresh;
    private int _generation;
    private GameObject _menuRoot;
    private MusicProduct? _purchaseProduct;
    private SongPreviewConfigSO _previewConfig;
    private int _activeLyric = -1;
    private float _nextLyricsUpdate;

    private void Awake()
    {
        Instance = this;
        _player = gameObject.AddComponent<JukeboxPlayer>();
        _previewConfig = Resources.Load<AppConfigSO>("AppConfig")?.songPreview;
        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.jukebox : null, UIFactory.RootCanvas(), "Jukebox");
        if (_view == null) { enabled = false; return; }
        UIFactory.Stretch((RectTransform)_view.transform);
        _view.back.onClick.AddListener(Close);
        _view.ConfigurePlaybackHeader();
        _view.BindTimeline(fraction =>
        {
            if (_busy || !_player.Seek(fraction)) return;
            _nextLyricsUpdate = 0;
            UpdateLyricsPhrase();
        });
        _view.full.onClick.AddListener(() => MusicAccess.Request(MusicProduct.Jukebox));
        _view.previous.onClick.AddListener(() => Skip(-1));
        _view.next.onClick.AddListener(() => Skip(1));
        _view.playPause.onClick.AddListener(TogglePlayback);
        _view.lyrics.gameObject.SetActive(false);
        _view.delete.onClick.AddListener(DeleteSelected);
        _view.background.gameObject.SetActive(false);
        _view.cancel.onClick.AddListener(() => _view.dialog.SetActive(false));
        _player.Changed += OnPlayerChanged;
        _player.PreviewFinished += OnPreviewFinished;
        _player.Failed += Status;
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
        if (_player != null) { _player.Changed -= OnPlayerChanged; _player.PreviewFinished -= OnPreviewFinished; _player.Failed -= Status; Destroy(_player); }
        if (_view != null) Destroy(_view.gameObject);
    }
    public void Open()
    {
        if (_view == null || _opened) return;
        _opened = true;
        _view.gameObject.SetActive(true); _view.transform.SetAsLastSibling();
        _view.ownerPlaque?.ChooseOwner(Resources.Load<AppConfigSO>("AppConfig")?.opponentRoster);
        // No menu input/animation continues underneath the music library.
        var menu = FindFirstObjectByType<MainMenuView>();
        _view.ApplyBrand(menu?.logo != null ? menu.logo.Shown : GameMusicStyle.Unknown);
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
        _finishedPreviewId = null;
        if (_refresh != null) { StopCoroutine(_refresh); _refresh = null; }
        _player?.Stop();
        if (_view != null) _view.gameObject.SetActive(false);
        bool inMenu = AppBootstrap.Context?.AppFlow.CurrentState == GameFlowState.MainMenu;
        if (_menuRoot != null && inMenu) _menuRoot.SetActive(true);
    }
    private void Update()
    {
        if (!_opened) return;
        RefreshTimeline();
        UpdateLyricsPhrase();
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            if (_view.dialog.activeSelf) _view.dialog.SetActive(false);
            else if (!_busy) Close();
        }
    }
    private void RefreshTimeline()
    {
        bool currentSelection = _selected != null && _player.Current?.id == _selected.id;
        float loadedDuration = currentSelection ? _player.DurationSeconds : 0;
        float total = loadedDuration;
        if (total <= 0 && _selected != null)
        {
            if (MusicAccess.Jukebox) total = _selected.durationSeconds;
            else
            {
                total = _previewConfig != null ? _previewConfig.Resolve(_selected.id, _selected.durationSeconds).duration :
                    _selected.durationSeconds > 0 ? Mathf.Min(30, _selected.durationSeconds) : 30;
            }
        }
        _view.SetProgress(currentSelection ? _player.ElapsedSeconds : 0, total, _player.CanSeek && loadedDuration > 0);
    }

    private IEnumerator Refresh()
    {
        yield return SongCatalogService.EnsureReady();
        if (!_opened) yield break;
        foreach (var record in ImportedAudioLibrary.Tracks.ToArray())
        {
            if (record.durationSeconds > 0 || PlayerProgressService.Instance?.IsJukeboxUnlocked(record.id) != true)
                continue;

            // Migrate older imports before their rows become available for selection.
            AudioClip clip = null;
            yield return LocalAudioClipLoader.Load(ImportedAudioLibrary.PathFor(record), loaded => clip = loaded);
            if (clip != null)
            {
                ImportedAudioLibrary.RecordDuration(record, clip.length);
                Destroy(clip);
            }
        }
        if (!_opened) yield break;
        Rebuild();
        RefreshMedallions();
        Status(null); _refresh = null;
    }
    private void Rebuild()
    {
        string selectedId = _selected?.id;
        _tracks = JukeboxTrack.Build();
        foreach (var row in _rows) if (row != null) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
        _rows.Clear();
        var lib = UIPrefabLibrarySO.Instance;
        int originalCount = SongCatalogService.Entries.Count();
        if (originalCount == 0) originalCount = Resources.Load<AppConfigSO>("AppConfig")?.songCatalog?.songs.Count ?? 0;
        int unlockedOriginals = _tracks.Count(track => !track.Imported && track.Unlocked);
        for (int i = 0; i < JukeboxCarousel.SlotCount; i++)
        {
            var row = UIPrefabLibrarySO.Spawn(lib != null ? lib.jukeboxRow : null, _view.list, "JukeboxRow");
            if (row == null) break;
            _rows.Add(row);
        }
        _view.carousel.Setup(_rows.ToArray(), _tracks, PlayWheelSelection, PlayWheelSelection);
        _view.summary.text = $"{unlockedOriginals} / {originalCount}";
        Select(_tracks.Find(t => t.id == selectedId) ?? (_tracks.Count > 0 ? _tracks[0] : null));
    }
    private static string Detail(JukeboxTrack track, bool includeOrigin = true)
    {
        string origin = track.Imported ? Loc.Get("Jukebox.Imported") : Loc.Get("Jukebox.Original");
        string style = track.style == GameMusicStyle.Unknown ? Loc.Get("Jukebox.UnknownStyle") : Loc.Get("GameStyle." + track.style);
        return (string.IsNullOrEmpty(track.artist) ? "" : track.artist + "  ·  ") + style + (includeOrigin ? "  ·  " + origin : "");
    }
    private void RefreshMedallions()
    {
        if (_view.medallions == null || _view.styleSleeves == null) return;
        var art = _tracks.Select(track => _view.SleeveFor(track))
            .Concat(_view.styleSleeves).Where(sprite => sprite != null).Distinct().ToList();
        foreach (var medallion in _view.medallions)
        {
            if (medallion == null || art.Count == 0) continue;
            int index = UnityEngine.Random.Range(0, art.Count);
            medallion.sprite = art[index];
            medallion.GetComponent<JukeboxMedallionArtwork>()?.Refresh();
            art.RemoveAt(index);
        }
    }
    private void PlayWheelSelection(int index)
    {
        if (!_opened || _busy || index < 0 || index >= _tracks.Count) return;
        var track = _tracks[index];
        if (_selected != track) Select(track, moveWheel: false);
        PlaySelected();
    }
    private void Select(JukeboxTrack track, bool moveWheel = true)
    {
        if (_selected != track) _view.CancelTimelineScrub();
        _selected = track;
        _activeLyric = int.MinValue;
        _nextLyricsUpdate = 0;
        _view.selected.text = track == null ? Loc.Get("Jukebox.Empty") : track.title;
        if (_view.detail != null) _view.detail.text = track == null ? "" : Detail(track, false);
        else if (track != null) _view.selected.text += "\n" + Detail(track);
        _view.cover.sprite = _view.SleeveFor(track);
        RefreshTimeline();
        if (moveWheel) _view.carousel.Focus(_tracks.FindIndex(entry => entry.id == track?.id));
        RefreshControls();
        if (track == null || track.Imported || track.lyricData != null) return;
        SongLyrics.Load(track.id, data =>
        {
            if (data == null) return;
            track.lyricData = data;
            track.lyrics = data.PlainText;
            if (this != null && _opened && _selected == track)
            {
                _activeLyric = int.MinValue;
                _nextLyricsUpdate = 0;
                RefreshControls();
            }
        });
    }
    private void OnPreviewFinished(JukeboxTrack track)
    {
        if (!_opened || _selected?.id != track.id || MusicAccess.Jukebox) return;
        _finishedPreviewId = track.id;
        Status(null);
        RefreshControls();
    }
    private void OnPlayerChanged()
    {
        if (!_player.Playing) _view.CancelTimelineScrub();
        if (_player.Current != null && _player.Current.id != _selected?.id) Select(_player.Current);
        else RefreshControls();
    }
    private void Skip(int direction)
    {
        if (_busy || _tracks.Count < 2) return;
        bool full = MusicAccess.Jukebox;
        if (_player.Playing && _player.FullPlayback && full) { _player.Skip(direction); return; }
        int index = _tracks.FindIndex(t => t.id == _selected?.id);
        index = (index + direction + _tracks.Count) % _tracks.Count;
        Select(_tracks[index]);
        PlaySelected();
    }
    private void RefreshControls()
    {
        if (_view == null) return;
        bool won = _selected?.Unlocked == true;
        if (MusicAccess.Jukebox) _finishedPreviewId = null;
        bool offerPremium = _finishedPreviewId != null && _finishedPreviewId == _selected?.id && !_player.Playing;
        _view.selected.gameObject.SetActive(!offerPremium);
        _view.nowPlaying.gameObject.SetActive(!offerPremium);
        _view.cover.gameObject.SetActive(!offerPremium && _selected != null && _view.cover.sprite != null);
        if (_view.detail != null) _view.detail.gameObject.SetActive(!offerPremium);
        _view.nowPlaying.text = _player.Current == null ? "" :
            Loc.Get("Jukebox.NowPlaying", _view.detail == null ? _player.Current.title : "").TrimEnd(' ', ':');
        if (_view.transportIcon != null)
            _view.transportIcon.sprite = _player.Playing && !_player.Paused ? _view.pauseIcon : _view.playIcon;
        _view.ShowPremiumOffer(offerPremium);
        if (_purchaseProduct.HasValue && _view.dialog.activeSelf)
            _view.confirm.interactable = MusicAccess.Store != null;
        bool hasLyrics = _selected != null && !_selected.Imported && _selected.lyricData != null;
        RefreshLyricsPanel(!offerPremium && hasLyrics && MusicAccess.Jukebox && _player.Current?.id == _selected.id);
        _view.delete.gameObject.SetActive(!offerPremium && _selected?.Imported == true);
        _view.delete.interactable = !_busy;
        _view.previous.interactable = _tracks.Count > 1 && !_busy;
        _view.next.interactable = _view.previous.interactable;
        _view.playPause.interactable = won && !_player.Loading && !_busy;
        Loc.Set(_view.playLabel, !_player.Playing ? "Jukebox.Play" : _player.Paused ? "Jukebox.Resume" : "Jukebox.Pause");
    }
    private void TogglePlayback()
    {
        if (_busy || _player.Loading) return;
        if (_player.Playing && _player.Current?.id == _selected?.id) _player.TogglePause();
        else PlaySelected();
    }
    private void PlaySelected()
    {
        if (_selected == null || !_selected.Unlocked || _busy) return;
        _finishedPreviewId = null;
        bool full = MusicAccess.Jukebox;
        _player.Play(_selected, full, _tracks);
        Status(null);
    }
    private void RefreshLyricsPanel(bool show)
    {
        if (_view.lyricsPanel == null || _view.lyricsLine == null) return;
        if (_view.lyricsPanel.activeSelf != show)
        {
            _view.lyricsPanel.SetActive(show);
            _activeLyric = int.MinValue;
            _nextLyricsUpdate = 0;
        }
        if (!show)
        {
            _view.lyricsLine.text = "";
            return;
        }
        UpdateLyricsPhrase();
    }

    private void UpdateLyricsPhrase()
    {
        if (_view.lyricsPanel == null || !_view.lyricsPanel.activeSelf || !MusicAccess.Jukebox ||
            _player.Current?.id != _selected?.id || Time.unscaledTime < _nextLyricsUpdate) return;
        _nextLyricsUpdate = Time.unscaledTime + .05f;
        float? preview = _view.TimelinePreview;
        SetLyricAt(_player.Loading ? float.NaN :
            preview.HasValue ? preview.Value * _player.DurationSeconds : _player.PositionSeconds);
    }

    private void SetLyricAt(float seconds)
    {
        var lyrics = _selected?.lyricData;
        int index = lyrics?.LineAt(seconds) ?? -1;
        if (index == _activeLyric) return;
        _activeLyric = index;
        _view.lyricsLine.text = index >= 0 ? lyrics.lines[index].text : "";
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
    private void Status(string key)
    {
        if (_view == null) return;
        if (string.IsNullOrEmpty(key)) _view.status.text = "";
        else Loc.Set(_view.status, key);
    }
    private void RefreshLanguage() { if (_opened) Rebuild(); }
}
