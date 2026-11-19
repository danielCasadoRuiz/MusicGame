using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>Independent of game selection/analysis. Owns every loaded clip and releases it on stop.
/// Full playback follows first-unlock order; free previews never auto-advance.</summary>
public class JukeboxPlayer : MonoBehaviour
{
    private AudioSource _source;
    private AsyncOperationHandle<AudioClip> _handle;
    private AudioClip _local;
    private Coroutine _load;
    private float _end;
    private bool _started, _paused;
    private bool _full;
    private bool _nativePlayback, _desktopBackground;
    private bool _previousRunInBackground;
    private bool _focused = true, _applicationPaused;
    private void OnEnable() => MusicAccess.Changed += AccessChanged;
    private bool CanPlayOutsideWindow => _full && BackgroundEnabled && MusicAccess.Background;
    private void AccessChanged()
    {
        if (_full && !MusicAccess.Jukebox) { Stop(); return; }
        if (BackgroundEnabled && !MusicAccess.Background)
        {
            if (_nativePlayback) { Stop(); return; }
            SetDesktopBackground(false);
        }
        EnforceFocus(); Changed?.Invoke();
    }
    private void EnforceFocus()
    {
        if ((!_focused || _applicationPaused) && !CanPlayOutsideWindow && _started && !_paused) TogglePause();
    }
    private void SetDesktopBackground(bool enabled)
    {
        if (enabled == _desktopBackground) return;
        if (enabled) { _previousRunInBackground = Application.runInBackground; Application.runInBackground = true; }
        else Application.runInBackground = _previousRunInBackground;
        _desktopBackground = enabled; BackgroundEnabled = enabled;
    }
    private float _nativeStarted;
    private float _nextNativePoll, _nativeVolume = -1;
    private int _nativeIndex = -1;
    private Coroutine _backgroundPreparation;
    public event Action<string> Status;
    private List<JukeboxTrack> _queue = new();
    public JukeboxTrack Current { get; private set; }
    public bool Loading => _load != null;
    public bool Paused => _paused;
    public bool Playing => Current != null;
    public bool FullPlayback => _full;
    public float PositionSeconds => _nativePlayback ? NativeMusicPlayer.PositionSeconds :
        _started && _source != null ? _source.time : 0f;
    public void RemoveTrack(string id)
    {
        if (Current?.id == id || BackgroundEnabled) Stop();
        _queue.RemoveAll(t => t.id == id);
    }
    public event Action Changed;
    public event Action<string> Failed;
    public bool BackgroundEnabled { get; private set; }
    private void Awake()
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false; _source.loop = false;
        GameAudio.RouteMusic(_source);
    }
    public void Play(JukeboxTrack track, bool full, List<JukeboxTrack> library)
    {
        if (track == null || !track.Unlocked || (full && !MusicAccess.Jukebox)) return;
        bool keepDesktopBackground = _desktopBackground && full && MusicAccess.Background;
        Stop();
        if (keepDesktopBackground) SetDesktopBackground(true);
        SongPreviewPlayer.Instance?.Stop();
        _queue = library.FindAll(t => t.Unlocked);
        Current = track; _full = full;
        _load = StartCoroutine(Load(track, full));
        Changed?.Invoke();
    }
    private IEnumerator Load(JukeboxTrack track, bool full)
    {
        AudioClip clip = null;
        if (track.Imported)
        {
            // Keep this iterator inline so cancelling this load also cancels the web request.
            yield return LocalAudioClipLoader.Load(track.path, loaded => { _local = loaded; clip = loaded; });
        }
        else
        {
            _handle = Addressables.LoadAssetAsync<AudioClip>(track.address);
            yield return _handle;
            if (_handle.Status == AsyncOperationStatus.Succeeded) clip = _handle.Result;
        }
        if (clip == null) { _load = null; Stop(); Failed?.Invoke("Jukebox.LoadFailed"); yield break; }
        if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        while (clip.loadState == AudioDataLoadState.Loading) yield return null;
        if (clip.loadState == AudioDataLoadState.Failed) { _load = null; Stop(); Failed?.Invoke("Jukebox.LoadFailed"); yield break; }
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var config = app != null ? app.songPreview : null;
        var range = config != null ? config.Resolve(track.id, clip.length) : (Mathf.Max(0, clip.length / 2 - 15), Mathf.Min(30, clip.length));
        float start = full ? 0 : range.Item1;
        _end = full ? clip.length : start + range.Item2;
        _source.clip = clip; _source.volume = 1f; _source.Play(); _source.time = start;
        _started = true; _load = null; EnforceFocus(); Changed?.Invoke();
    }
    private void Update()
    {
        if (_nativePlayback)
        {
            if (Time.realtimeSinceStartup < _nextNativePoll) return;
            _nextNativePoll = Time.realtimeSinceStartup + .2f;
            UpdateNativeVolume();
            int index = NativeMusicPlayer.Index;
            bool paused = !NativeMusicPlayer.Playing;
            if (index >= 0 && index < _queue.Count && (index != _nativeIndex || paused != _paused))
            {
                _nativeIndex = index; Current = _queue[index]; _paused = paused; Changed?.Invoke();
            }
            else if (index < 0 && Time.realtimeSinceStartup - _nativeStarted > 3f)
            {
                bool failed = !string.IsNullOrEmpty(NativeMusicPlayer.Error);
                Stop(); if (failed) Failed?.Invoke("Jukebox.BackgroundFailed");
            }
            return;
        }
        if (!_started || _paused || _source == null) return;
        if (_source.time >= _end - 0.025f || !_source.isPlaying)
        {
            _started = false;
            if (_full && MusicAccess.Jukebox) Skip(1, wrap: false); else Stop();
        }
        else if (!_full) _source.volume = Mathf.Clamp01((_end - _source.time) / 0.6f);
    }
    public void TogglePause()
    {
        if (_nativePlayback) { NativeMusicPlayer.Command("toggle"); return; }
        if (!_started) return;
        _paused = !_paused;
        if (_paused) _source.Pause(); else _source.UnPause();
        Changed?.Invoke();
    }
    public void Skip(int direction, bool wrap = true)
    {
        if (_nativePlayback) { NativeMusicPlayer.Command(direction > 0 ? "next" : "previous"); return; }
        if ((_full && !MusicAccess.Jukebox) || _queue.Count == 0) return;
        int index = _queue.FindIndex(t => t.id == Current?.id) + direction;
        if (wrap) index = (index + _queue.Count) % _queue.Count;
        if (index < 0 || index >= _queue.Count) { Stop(); return; }
        var library = new List<JukeboxTrack>(_queue);
        Play(_queue[index], _full, library);
    }
    public void Stop()
    {
        if (_backgroundPreparation != null)
        {
            StopCoroutine(_backgroundPreparation); _backgroundPreparation = null;
            NativeMusicPlayer.CancelPreparation();
        }
        if (_nativePlayback) NativeMusicPlayer.Command("stop");
        _nativePlayback = false;
        SetDesktopBackground(false);
        BackgroundEnabled = false; _nativeIndex = -1;
        if (_load != null) { StopCoroutine(_load); _load = null; }
        if (_source != null) { _source.Stop(); _source.clip = null; }
        if (_handle.IsValid()) Addressables.Release(_handle);
        _handle = default;
        if (_local != null) Destroy(_local);
        _local = null; Current = null; _started = false; _paused = false;
        Changed?.Invoke();
    }
    public IEnumerator EnableBackground(List<JukeboxTrack> tracks)
    {
        if (!MusicAccess.Background) yield break;
        if (BackgroundEnabled)
        {
            if (_nativePlayback) Stop();
            else { SetDesktopBackground(false); EnforceFocus(); Changed?.Invoke(); }
            yield break;
        }
        if (!NativeMusicPlayer.Supported)
        {
            // Desktop/Editor uses the existing player while allowing Unity to run out of focus.
            if (Application.isMobilePlatform) { Failed?.Invoke("Jukebox.BackgroundUnsupported"); yield break; }
            var queue = tracks.FindAll(t => t.Unlocked);
            if (queue.Count == 0) yield break;
            if (Current == null || !_full) Play(Current ?? queue[0], true, tracks);
            SetDesktopBackground(true);
            if (_paused) TogglePause();
            Status?.Invoke("Jukebox.BackgroundReady"); Changed?.Invoke();
            yield break;
        }
        if (_backgroundPreparation != null) yield break;
        Status?.Invoke("Jukebox.PreparingBackground");
        _backgroundPreparation = StartCoroutine(PrepareBackground(tracks));
        yield return _backgroundPreparation;
    }
    private IEnumerator PrepareBackground(List<JukeboxTrack> tracks)
    {
        var queue = tracks.FindAll(t => t.Unlocked);
        if (queue.Count == 0) { _backgroundPreparation = null; yield break; }
        int index = Mathf.Max(0, queue.FindIndex(t => t.id == Current?.id));
        List<NativeMusicPlayer.Item> items = null;
        yield return NativeMusicPlayer.Prepare(queue, result => items = result);
        _backgroundPreparation = null;
        if (items == null) { Failed?.Invoke("Jukebox.BackgroundFailed"); yield break; }
        Stop();
        _queue = queue; Current = queue[index]; _full = true;
        try
        {
            NativeMusicPlayer.Start(items, index);
            _nativeVolume = -1; _nextNativePoll = 0; UpdateNativeVolume();
            _nativePlayback = true; BackgroundEnabled = true; _nativeStarted = Time.realtimeSinceStartup; _nativeIndex = -1;
            Status?.Invoke("Jukebox.BackgroundReady"); Changed?.Invoke();
        }
        catch (Exception e) { Debug.LogWarning(e.Message); Stop(); Failed?.Invoke("Jukebox.BackgroundFailed"); }
    }
    private void UpdateNativeVolume()
    {
        float volume = AudioListener.volume * (GameAudio.Instance != null ? GameAudio.Instance.GetVolume(GameAudioChannel.Music) : 1f);
        if (Mathf.Abs(volume - _nativeVolume) < .001f) return;
        _nativeVolume = volume; NativeMusicPlayer.SetVolume(volume);
    }
    private void OnApplicationFocus(bool focus) { _focused = focus; EnforceFocus(); }
    private void OnApplicationPause(bool pause) { _applicationPaused = pause; EnforceFocus(); }
    private void OnDisable() { MusicAccess.Changed -= AccessChanged; Stop(); }
    private void OnDestroy() => Stop();
}
