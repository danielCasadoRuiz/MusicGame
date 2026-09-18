using UnityEngine;

/// <summary>
/// Persistent, app-level session state — survives every scene load (created once by AppBootstrap,
/// DontDestroyOnLoad). Deliberately a plain MonoBehaviour singleton (same Instance pattern as
/// MusicClock/CameraFollow/etc. elsewhere in this project) holding plain data, NOT a
/// ScriptableObject — runtime session state must never risk being accidentally serialized back
/// into a project asset.
///
/// OWNERSHIP (who WRITES each field, per the app-flow/Theme refactor plan):
///   Song Selection      → SelectedSong
///   Song Analysis       → Profile
///   Gameplay (end of run)     → RunnerResults, RunnerFightResources, FighterStats, FightResources
///   Opponent Selection roulette → SelectedOpponent, SelectedOpponentSong, SelectedOpponentLevelConfig
/// Everything else only ever READS these fields. This is the single place that data lives — no
/// parallel copies of "the current song" scattered across other systems.
///
/// This does NOT own CurrentTheme (that belongs to the future Theme system) — it holds enough IDs
/// (e.g. DetectedMusicStyleId) to reconstruct/re-request a session, never a duplicate copy of
/// another module's own runtime state.
///
/// Implements IConfigurableModule&lt;FightStatsConfig&gt; ONLY for that one config — see
/// RunnerFightResourceBuilder/FighterStatsBuilder's own doc for why the Runner→Fight translation
/// lives here rather than as a new parallel system.
/// </summary>
public class GameSession : MonoBehaviour, IAppModule, IConfigurableModule<FightStatsConfig>
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

    /// <summary>The normalized (0..1) combat potential this run earned, one entry per FightStatId
    /// — see RunnerFightResourceBuilder/RunnerFightResources' own doc. Built right alongside
    /// RunnerResults (same GameEndedEvent handler), NOT deferred to when the player presses
    /// Continue — it's a consequence of the run's performance, not of a UI navigation action, and
    /// this timing is also what would let a future Results-screen preview show it before Fight
    /// even loads. Null until a run ends, or if no FightStatsConfig was ever configured (see
    /// Configure).</summary>
    public RunnerFightResources RunnerFightResources { get; private set; }

    /// <summary>The Fighter's final combat numbers for this run — built from RunnerFightResources
    /// via FighterStatsBuilder, same moment as RunnerFightResources above. Null under the same
    /// conditions.</summary>
    public FighterStats FighterStats { get; private set; }

    /// <summary>Consumable/special combat resources (extra lives, revives, shields...) —
    /// deliberately NOT derived from RunnerFightResources/FighterStats/RunnerResults at all (see
    /// FightResources' own doc). Lazily created once (never overwritten by a later run) so a
    /// future source can populate/accumulate it across runs without this class fighting that
    /// design once it exists.</summary>
    public FightResources FightResources { get; private set; }

    /// <summary>The rival OpponentSelectionController's roulette settled on — written once, right
    /// as the roulette's final hold begins (before Versus even shows), so it's already available
    /// for a future Results-screen-style preview too. Null until a roulette actually completes
    /// (e.g. Fight reached directly for debugging, skipping Opponent Selection — every downstream
    /// reader tolerates that and falls back to a generic "unknown rival" display).</summary>
    public OpponentDefinition SelectedOpponent { get; set; }

    /// <summary>The specific song chosen from SelectedOpponent's own roster for this match — the
    /// one FightMusicController locks in and keeps playing, unbroken, through Versus/Round Intro/
    /// Countdown/Fighting. Kept here (not just inside FightMusicController) because it's also
    /// meant to become the base musical material for the whole battle later, at which point
    /// combat-specific systems will want to read it directly rather than reaching into a UI-scene
    /// controller for it.</summary>
    public AudioClip SelectedOpponentSong { get; set; }

    /// <summary>SelectedOpponent.GetConfigForLevel(playerLevel), resolved ONCE at the same moment
    /// as SelectedOpponent/SelectedOpponentSong and cached here — so Fight's future avatar/arena
    /// spawning reads the exact same portrait/fighterPrefab/difficulty the player actually saw
    /// during Opponent Selection/Versus, rather than re-resolving against a Player Level that
    /// could (once that system exists) have changed in between. Null under the same conditions as
    /// SelectedOpponent.</summary>
    public OpponentLevelConfig SelectedOpponentLevelConfig { get; set; }

    private FightStatsConfig _fightStatsConfig;
    private bool             _loggedMissingFightStatsConfig;

    private System.Action<SongProfileReadyEvent> _onProfileReady;
    private System.Action<GameEndedEvent>        _onGameEnded;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Configure(FightStatsConfig config) => _fightStatsConfig = config;

    void IAppModule.Initialize(AppContext context) { /* no cross-module wiring needed yet */ }
    void IAppModule.Shutdown() { }

    private void OnEnable()
    {
        _onProfileReady = e => Profile = e.Profile;
        _onGameEnded = e =>
        {
            RunnerResults = new RunnerResults
            {
                Stats            = e.Stats,
                FallCount        = e.FallCount,
                NormalizedScore  = e.NormalizedScore,
                MaxPossibleScore = e.MaxPossibleScore,
                Performance      = e.Performance,
            };

            if (_fightStatsConfig != null)
            {
                RunnerFightResources = RunnerFightResourceBuilder.Build(RunnerResults, _fightStatsConfig);
                FighterStats         = FighterStatsBuilder.Build(RunnerFightResources, _fightStatsConfig);
            }
            else if (!_loggedMissingFightStatsConfig)
            {
                _loggedMissingFightStatsConfig = true;
                Debug.LogWarning("[GameSession] No FightStatsConfig (AppConfig.fightStats) configured — " +
                                  "RunnerFightResources/FighterStats will stay null after every run.");
            }

            // Never overwritten once created — see FightResources' own doc on why these are
            // independent of run performance (accumulation-across-runs is a future decision, not
            // one this makes for you by resetting it here).
            FightResources ??= new FightResources();
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
