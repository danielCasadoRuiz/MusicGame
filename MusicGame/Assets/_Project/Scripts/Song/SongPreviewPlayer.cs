using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// The ONE song-preview player (Song Selection's Preview button). A single AudioSource with an
/// explicit lifecycle: one preview at a time (a new one replaces the old), stops by itself after
/// SongPreviewConfigSO's duration (with a short fade), and is stopped by Song Selection whenever the
/// highlighted song changes, the screen is left, or a run starts — plus a safety stop whenever the
/// game leaves the SongSelection state. Separate from FightMusicController (fight/roulette music),
/// which is never playing while the selector is open.
/// Catalog songs are loaded through Addressables only while previewed and released afterwards.
/// </summary>
public class SongPreviewPlayer : MonoBehaviour
{
    public static SongPreviewPlayer Instance { get; private set; }

    private const float FadeSeconds = 0.6f;

    private AudioSource _source;
    private SongPreviewConfigSO _config;
    private Coroutine _routine;
    private AsyncOperationHandle<AudioClip> _handle;
    private System.Action<GameFlowStateChangedEvent> _onFlow;

    /// <summary>Address of the song being previewed (null when idle).</summary>
    public string PlayingId { get; private set; }
    public bool IsPlaying => PlayingId != null;
    /// <summary>Start/duration (s) of the preview now playing (valid once loaded; duration 0 before).</summary>
    public (float start, float duration) PlayingRange { get; private set; }
    public event System.Action StateChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;
        var app = Resources.Load<AppConfigSO>("AppConfig");
        _config = app != null ? app.songPreview : null;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnEnable()
    {
        _onFlow = e => { if (e.Previous == GameFlowState.SongSelection) Stop(); };
        EventBus.Subscribe(_onFlow);
    }

    private void OnDisable() => EventBus.Unsubscribe(_onFlow);

    /// <summary>Starts the preview of a catalog song (its Addressable location).</summary>
    public void Play(IResourceLocation location)
    {
        Stop();
        if (location == null) return;
        PlayingId = location.PrimaryKey;
        StateChanged?.Invoke();
        _routine = StartCoroutine(LoadAndPlay(location));
    }

    public void Stop()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        if (_source != null) { _source.Stop(); _source.clip = null; _source.volume = 1f; }
        if (_handle.IsValid()) Addressables.Release(_handle);
        _handle = default;
        bool wasPlaying = PlayingId != null;
        PlayingId = null;
        PlayingRange = default;
        if (wasPlaying) StateChanged?.Invoke();
    }

    private IEnumerator LoadAndPlay(IResourceLocation location)
    {
        _handle = Addressables.LoadAssetAsync<AudioClip>(location);
        yield return _handle;
        var clip = _handle.Status == AsyncOperationStatus.Succeeded ? _handle.Result : null;
        if (clip == null) { Debug.LogWarning($"[SongPreview] Could not load '{location.PrimaryKey}'."); _routine = null; Stop(); yield break; }
        clip.LoadAudioData();
        while (clip.loadState == AudioDataLoadState.Loading) yield return null;

        var (start, duration) = _config != null ? _config.Resolve(location.PrimaryKey, clip.length)
                                                : (Mathf.Max(0f, clip.length * 0.5f - 15f), Mathf.Min(30f, clip.length));
        PlayingRange = (start, duration);
        _source.clip = clip;
        _source.volume = 1f;
        _source.Play();
        _source.time = start; // seek after Play: reliable once the data is loaded
        Debug.Log($"[SongPreview] '{location.PrimaryKey}' from {start:0.0}s for {duration:0.0}s");
        StateChanged?.Invoke(); // the range is known now

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float left = duration - t;
            _source.volume = left < FadeSeconds ? left / FadeSeconds : 1f;
            yield return null;
        }
        _routine = null;
        Stop();
    }
}
