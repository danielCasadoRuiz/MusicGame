using UnityEngine;

/// <summary>
/// Persistent, app-level session state — survives every scene load (created once by AppBootstrap,
/// DontDestroyOnLoad). Deliberately a plain MonoBehaviour singleton (same Instance pattern as
/// MusicClock/CameraFollow/etc. elsewhere in this project) holding plain data, NOT a
/// ScriptableObject — runtime session state must never risk being accidentally serialized back
/// into a project asset. Has no real configuration of its own, so it implements IAppModule only
/// (not IConfigurableModule) — it's pure runtime data, not a system with editable settings.
///
/// OWNERSHIP (who WRITES each field, per the app-flow/Theme refactor plan):
///   Song Selection    → SelectedSong
///   Song Analysis     → Profile
///   Gameplay (end of run) → RunnerResults
/// Everything else only ever READS these fields. This is the single place that data lives — no
/// parallel copies of "the current song" scattered across other systems.
///
/// This does NOT own CurrentTheme (that belongs to the future Theme system) — it holds enough IDs
/// (e.g. DetectedMusicStyleId) to reconstruct/re-request a session, never a duplicate copy of
/// another module's own runtime state.
///
/// Fields for concepts that don't exist yet (future Fight-specific data, beyond what RunnerResults
/// already carries) are added here as their own phases actually land, rather than stubbing out
/// placeholder types before their real shape is designed.
/// </summary>
public class GameSession : MonoBehaviour, IAppModule
{
    public static GameSession Instance { get; private set; }

    /// <summary>What Song Selection chose. Null until Song Selection actually runs (not built yet
    /// — see SelectedSongInfo's own doc).</summary>
    public SelectedSongInfo? SelectedSong { get; set; }

    /// <summary>This song's analysis — the single copy every downstream system (Gameplay, future
    /// Fight) reads. Filled automatically here whenever SongProfileReadyEvent fires, in addition
    /// to whatever else already subscribes to that same event directly (e.g. GameplayManager) —
    /// existing subscribers are left untouched; this only adds one more, authoritative copy.</summary>
    public SongProfile Profile { get; private set; }

    /// <summary>Written by RunnerSceneBootstrap right after IMusicStyleClassifier resolves this song's
    /// style (see MusicStyleDetectedEvent) — MUSIC information, not Theme (see MusicStyleId's own
    /// doc on why those are kept separate). Unknown until Song Analysis actually classifies a
    /// song.</summary>
    public MusicStyleId DetectedMusicStyleId { get; set; } = MusicStyleId.Unknown;

    /// <summary>This run's final performance — the copy Fight (and any future post-Gameplay system)
    /// reads. Filled automatically here whenever GameEndedEvent fires, in addition to whatever else
    /// already subscribes to that same event directly (e.g. GameplayHUD) — existing subscribers are
    /// left untouched; this only adds one more, authoritative copy. Null until a run actually
    /// ends.</summary>
    public RunnerResults RunnerResults { get; private set; }

    private System.Action<SongProfileReadyEvent> _onProfileReady;
    private System.Action<GameEndedEvent>        _onGameEnded;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void IAppModule.Initialize(AppContext context) { /* no cross-module wiring needed yet */ }
    void IAppModule.Shutdown() { }

    private void OnEnable()
    {
        _onProfileReady = e => Profile = e.Profile;
        _onGameEnded = e => RunnerResults = new RunnerResults
        {
            Stats            = e.Stats,
            FallCount        = e.FallCount,
            NormalizedScore  = e.NormalizedScore,
            MaxPossibleScore = e.MaxPossibleScore,
            Performance      = e.Performance,
        };
        EventBus.Subscribe(_onProfileReady);
        EventBus.Subscribe(_onGameEnded);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onProfileReady);
        EventBus.Unsubscribe(_onGameEnded);
    }
}

/// <summary>
/// What Song Selection produced — either a predefined song (an AudioClip already in the project)
/// or a locally-picked audio file (see the future ISongSource/local-file-picker abstraction).
/// Kept as a plain struct (not a class) so GameSession.SelectedSong is never a half-constructed
/// reference — it's either present (HasValue) with both fields meaningful for its kind, or absent.
/// </summary>
public struct SelectedSongInfo
{
    public string    DisplayName;
    public AudioClip Clip;
    /// <summary>Null for a predefined song; set for a locally-picked WAV/MP3.</summary>
    public string    LocalFilePath;
}
