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
///   Opponent Selection roulette → SelectedOpponent, SelectedOpponentSong, SelectedOpponentLevelConfig,
///                                 SelectedOpponentTier (drawn via PickNextOpponent / OpponentBag)
///   FightMatchController (match won) → Progression (via RegisterCompletedSong())
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
public class GameSession : MonoBehaviour, IAppModule, IConfigurableModule<FightStatsConfig>, IConfigurableModule<ProgressionConfigSO>
{
    public static GameSession Instance { get; private set; }

    /// <summary>What Song Selection chose. Null until Song Selection actually runs (not built yet
    /// — see SelectedSongInfo's own doc).
    /// Every assignment (a new song, or null while a catalog clip is still loading) starts a NEW
    /// selection: SelectionGeneration increments and the previous selection's Profile is dropped, so a
    /// late analysis of an older selection can never be adopted and nothing retains its data.</summary>
    public SelectedSongInfo? SelectedSong
    {
        get => _selectedSong;
        set
        {
            _selectedSong = value;
            SelectionGeneration++;
            Profile = null;
            // The previous song's playback audio is released once ANOTHER song (or an imported file) is the
            // selection. A pending selection (null) keeps it until the new content is adopted — re-selecting
            // the same song then never unloads/reloads its clip (acquire, then release).
            if (SongContent != null && value.HasValue && (!value.Value.IsCatalogSong || value.Value.SongId != SongContent.SongId))
                ReleaseSongContent();
        }
    }
    private SelectedSongInfo? _selectedSong;

    /// <summary>The playback audio lease of the current catalog selection (CatalogSongLoader → AdoptCatalogContent).
    /// GameSession is its ONLY owner: nothing else releases it.</summary>
    public SongContentLease SongContent { get; private set; }

    /// <summary>Hands the prepared content of selection `generation` to the session: its clip becomes
    /// SelectedSong.Clip (same selection — no new generation) and the lease is owned here. False (caller
    /// releases its lease) when the selection moved on meanwhile — a stale load can never adopt.</summary>
    public bool AdoptCatalogContent(int generation, SongContentLease lease)
    {
        if (lease == null || generation != SelectionGeneration || !_selectedSong.HasValue || _selectedSong.Value.SongId != lease.SongId)
            return false;
        var previous = SongContent;
        SongContent = lease;
        var song = _selectedSong.Value;
        song.Clip = lease.Clip;
        _selectedSong = song;
        if (previous != null && previous != lease) previous.Release(); // new one held first, then the old released
        return true;
    }

    private void ReleaseSongContent()
    {
        var lease = SongContent;
        SongContent = null;
        lease?.Release();
    }

    /// <summary>Identity of the current song selection (see SelectedSong). Analysis requests carry it;
    /// SongProfileReadyEvent with another generation is rejected.</summary>
    public int SelectionGeneration { get; private set; }

    /// <summary>This song's analysis — the single copy every downstream system (Gameplay, future
    /// Fight) reads. Filled automatically here whenever SongProfileReadyEvent fires FOR THE CURRENT
    /// SELECTION (SelectionGeneration), in addition to whatever else already subscribes to that same
    /// event directly (e.g. GameplayManager).</summary>
    public SongProfile Profile { get; private set; }

    /// <summary>Written by RunnerSceneBootstrap right after IMusicStyleClassifier resolves this song's
    /// style (see MusicStyleDetectedEvent) — MUSIC information, not Theme (see MusicStyleId's own
    /// doc on why those are kept separate). Unknown until Song Analysis actually classifies a
    /// song.</summary>
    public MusicStyleId DetectedMusicStyleId { get; set; } = MusicStyleId.Unknown;
    /// <summary>The game-facing style (MusicStyleResolver) — DetectedMusicStyleId above is only its
    /// visual-theme key. The raw tags stay untouched on SongProfile.musicTags.</summary>
    public GameMusicStyle DetectedGameStyle { get; set; } = GameMusicStyle.Unknown;

    /// <summary>The song the player CHOSE themselves in Song Selection (catalog id or local file) — its style counts
    /// for the profile once analysed (RecordStyleIfManual). Automatic choices (CONTINUE, next song) never set it.</summary>
    public string ManualChoiceKey { get; private set; }
    public void MarkManualChoice(string key) => ManualChoiceKey = key;
    public static string SongKey(SelectedSongInfo? s) =>
        !s.HasValue ? null : !string.IsNullOrEmpty(s.Value.SongId) ? s.Value.SongId : !string.IsNullOrEmpty(s.Value.LocalFilePath) ? s.Value.LocalFilePath : s.Value.DisplayName;

    /// <summary>Called when the style of the selected song is known: counts it in the player profile if the player
    /// picked that song themselves (once).</summary>
    public void RecordStyleIfManual(GameMusicStyle style)
    {
        string key = SongKey(SelectedSong);
        if (string.IsNullOrEmpty(key) || key != ManualChoiceKey) return;
        ManualChoiceKey = null;
        PlayerProgressService.Instance?.RecordStylePick(style);
    }

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
    /// FightResources' own doc). Lazily created once (never overwritten by a later run). The
    /// Runner's own collected resources are NOT copied in here: their single canonical copy is
    /// RunnerResults.Resources (the later Fight system decides how to consume them).</summary>
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

    /// <summary>SelectedOpponent.GetConfigForTier(EffectiveOpponentTier), resolved ONCE at the same moment
    /// as SelectedOpponent/SelectedOpponentSong and cached here — so Fight's future avatar/arena
    /// spawning reads the exact same portrait/fighterPrefab/difficulty the player actually saw
    /// during Opponent Selection/Versus, rather than re-resolving against a Player Level that
    /// could (once that system exists) have changed in between. Null under the same conditions as
    /// SelectedOpponent.</summary>
    public OpponentLevelConfig SelectedOpponentLevelConfig { get; set; }

    // ── Progression + opponent selection (persistent: this object is DontDestroyOnLoad, so the
    //    Runner → Results → Fight → next song flow never resets them) ─────────────────────────

    /// <summary>Completed songs (raw fact) — CurrentTier/progress are derived via ProgressionConfigSO.
    /// A completed song = one match WON (RegisterCompletedSong, called ONLY by FightMatchController).
    /// No persistence across app restarts yet; the state is [Serializable] for a future save game.</summary>
    public GameProgression Progression { get; private set; }

    public ProgressionSnapshot ProgressionInfo => Progression.Snapshot;

    /// <summary>Shuffle bag + recent history of opponent ids — see OpponentShuffleBag.</summary>
    public OpponentShuffleBag OpponentBag { get; } = new();

    /// <summary>The tier SelectedOpponentLevelConfig was actually resolved from (after fallback).</summary>
    public int SelectedOpponentTier { get; set; }

    // ── Precomputed assignment (RunAssignment) — the FINAL rival of the current song, decided BEFORE
    //    the Runner (and persisted as PlayerProgress.pendingRun for catalog songs). The Opponent
    //    Selection roulette only REVEALS it; it never re-rolls or modifies it. ──
    public OpponentDefinition  AssignedOpponent { get; private set; }
    public OpponentLevelConfig AssignedOpponentLevelConfig { get; private set; }
    /// <summary>Tier the assignment was made at (the roulette's fakes use it too).</summary>
    public int AssignedOpponentTier { get; private set; }

    public void SetAssignedOpponent(OpponentDefinition opponent, OpponentLevelConfig level, int tier)
    {
        if (!_settingBossFight) ApplyTestForce(ref opponent, ref level, ref tier);
        AssignedOpponent            = opponent;
        AssignedOpponentLevelConfig = level;
        AssignedOpponentTier        = tier;
        CurrentBossStage = -1;       // any normal assignment ends a final-rival setup
        BossSongOverride = null;
        BossArena        = null;
    }

    private bool _settingBossFight;

    /// <summary>AppConfig.testForceOpponentId / testForceOpponentLevel (TEST ONLY): replaces the assigned rival
    /// (new match, resumed pending match, free run alike) for the session — saved progress is not rewritten.</summary>
    private static void ApplyTestForce(ref OpponentDefinition opponent, ref OpponentLevelConfig level, ref int tier)
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        if (app == null || string.IsNullOrEmpty(app.testForceOpponentId) || app.opponentRoster == null) return;
        OpponentDefinition forced = null;
        foreach (var o in app.opponentRoster.opponents) if (o != null && o.id == app.testForceOpponentId) { forced = o; break; }
        if (forced == null) { Debug.LogWarning($"[GameSession] TEST FORCE: no roster rival with id '{app.testForceOpponentId}' — ignored."); return; }
        int wanted = app.testForceOpponentLevel > 0 ? app.testForceOpponentLevel : (level != null ? level.level : tier);
        OpponentLevelConfig forcedLevel = null;
        if (forced.levels != null) foreach (var l in forced.levels) if (l != null && l.level == wanted) { forcedLevel = l; break; }
        if (forcedLevel == null) forcedLevel = forced.GetConfigForTier(Mathf.Max(1, wanted));
        Debug.LogWarning($"[GameSession] TEST FORCE active (AppConfig.testForceOpponentId): rival {forced.displayName} v{(forcedLevel != null ? forcedLevel.level : 0)} " +
                         $"instead of {(opponent != null ? opponent.displayName : "none")} — clear it for the real game.");
        opponent = forced; level = forcedLevel; tier = forcedLevel != null ? forcedLevel.level : tier;
    }

    // ── Final rival (CampaignService.PrepareBossStage) — combat-only stages ──
    /// <summary>≥ 0 while the current fight is a final-rival stage; -1 for normal campaign matches.</summary>
    public int CurrentBossStage { get; private set; } = -1;
    /// <summary>The stage's special song (null = the version's own songs).</summary>
    public AudioClip BossSongOverride { get; private set; }
    /// <summary>Arena of the current final-rival stage (null for normal fights: the composer's own arenas).</summary>
    public UnityEngine.AddressableAssets.AssetReferenceGameObject BossArena { get; private set; }

    public void SetBossFight(int stage, OpponentDefinition opponent, OpponentLevelConfig level, AudioClip song,
                             UnityEngine.AddressableAssets.AssetReferenceGameObject arena)
    {
        _settingBossFight = true;
        try { SetAssignedOpponent(opponent, level, level != null ? level.level : 0); }
        finally { _settingBossFight = false; }
        CurrentBossStage = stage;
        BossSongOverride = song;
        BossArena        = arena;
        // A boss stage is fought directly (no roulette, no runner): commit the pick now.
        SelectedOpponent            = opponent;
        SelectedOpponentLevelConfig = level;
        SelectedOpponentTier        = level != null ? level.level : 0;
        SelectedOpponentSong        = song != null ? song : (level != null ? level.GetRandomSong(null) : null);
    }

    // ── Debug override (never touches progression, bag or history) ──
    public string DebugForcedOpponentId { get; private set; }
    public int    DebugForcedTier { get; private set; }

    private ProgressionConfigSO _progressionConfig;
    private readonly System.Random _selectionRng = new(System.Environment.TickCount);

    private FightStatsConfig _fightStatsConfig;
    private readonly GameProgressionState _progressionState = new();
    private bool             _loggedMissingFightStatsConfig;

    private System.Action<SongProfileReadyEvent> _onProfileReady;
    private System.Action<GameEndedEvent>        _onGameEnded;
    private System.Action<GameStartedEvent>      _onGameStarted;
    private int _runCounter;

    /// <summary>The CURRENT run (one Runner song + its fight) — temporary state that resets when the
    /// next run starts (see RunSession). Persistent progression lives in PlayerProgressService.</summary>
    public RunSession Run { get; private set; } = new RunSession(0, null);

    // The tier state is the persistent one when the progression service exists (it is created first).
    private GameProgressionState TierState => PlayerProgressService.Instance != null ? PlayerProgressService.Instance.TierProgress : _progressionState;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Progression ??= new GameProgression(TierState, _progressionConfig);
    }

    public void Configure(ProgressionConfigSO config)
    {
        _progressionConfig = config;
        Progression ??= new GameProgression(TierState, config);
        Progression.SetConfig(config);
        if (config == null)
            Debug.LogWarning("[GameSession] No ProgressionConfigSO (AppConfig.progression) — using songsPerTier 5 / cooldown 2 defaults.");
    }

    /// <summary>The tier opponent content is resolved at: the debug-forced tier if set, else the
    /// real CurrentTier.</summary>
    public int EffectiveOpponentTier => DebugForcedTier > 0 ? DebugForcedTier : Progression.CurrentTier;

    /// <summary>Next opponent for a fight: the debug-forced one if set (bag/history untouched), else
    /// the shuffle bag (see OpponentShuffleBag). Null only for an empty roster.</summary>
    public OpponentDefinition PickNextOpponent(OpponentRosterSO roster)
    {
        if (roster == null || roster.opponents == null) return null;
        if (!string.IsNullOrEmpty(DebugForcedOpponentId))
            foreach (var o in roster.opponents)
                if (o != null && o.id == DebugForcedOpponentId) return o;
        int cooldown = _progressionConfig != null ? _progressionConfig.recentOpponentCooldown : 2;
        return OpponentBag.Next(roster.opponents, cooldown, _selectionRng);
    }

    // Last match song per opponent id — so a rival with 2+ songs never repeats the previous one.
    private readonly System.Collections.Generic.Dictionary<string, AudioClip> _lastOpponentSong = new();

    /// <summary>Random song from the opponent's level config, avoiding the one used the last time
    /// this same opponent was fought (when it has another). Null if the level has no songs.</summary>
    public AudioClip PickOpponentSong(OpponentDefinition opponent, OpponentLevelConfig level)
    {
        if (level == null) return null;
        string key = opponent != null ? opponent.id : "";
        _lastOpponentSong.TryGetValue(key, out var previous);
        var song = level.GetRandomSong(previous);
        _lastOpponentSong[key] = song;
        return song;
    }

    /// <summary>Called ONLY by FightMatchController the instant a match is decisively WON — the song
    /// has been DEFENDED: tier progression +1, the song joins completedSongIds, and its pending
    /// assignment is cleared. `save` false = FightMatchController batches this into RecordVictory's
    /// single save. Returns the current tier afterwards.</summary>
    public int RegisterCompletedSong(bool save = true)
    {
        int tier = Progression.RegisterCompletedSong();
        var progress = PlayerProgressService.Instance;
        // Song history: only PLAYABLE CATALOG songs (a local file has LocalFilePath and no stable id).
        var song = SelectedSong;
        if (progress != null && song.HasValue && string.IsNullOrEmpty(song.Value.LocalFilePath))
        {
            // (songId resolved below — always the stable catalog id, never the address)
            string songId = !string.IsNullOrEmpty(song.Value.SongId) ? song.Value.SongId : SongCatalog.IdForAddress(song.Value.DisplayName);
            progress.MarkSongCompleted(songId, save: false);
            // Only the assignment of THIS song is cleared (a local-file side run never touches it).
            if (progress.PendingRun.IsSet && progress.PendingRun.songId == songId) progress.ClearPendingRun(save: false);
        }
        SetAssignedOpponent(null, null, 0);
        if (save) progress?.NotifyTierProgressChanged(); // persisted immediately (tier + song history + pending)
        return tier;
    }

    // LIFE pickups already granted by runs OUTSIDE the campaign assignment (local files), per song, for
    // this app session — the same once-per-match rule the assignment persists (PendingRunData.livesGranted).
    private readonly System.Collections.Generic.Dictionary<string, int> _freeRunLivesGranted = new();

    /// <summary>The `nthThisRun`-th LIFE pickup of the current run: +1 extra life only when no earlier attempt
    /// of the same match / song already earned it (RunRewardRules.GrantsLife). True when granted.</summary>
    public bool GrantRunLife(int nthThisRun)
    {
        var progress = PlayerProgressService.Instance;
        if (progress == null) return false;
        string songId = Run.SongId ?? "";
        if (progress.IsPendingSong(songId)) return progress.GrantRunLife(songId, nthThisRun, "Runner LIFE pickup");
        _freeRunLivesGranted.TryGetValue(songId, out int already);
        if (!RunRewardRules.GrantsLife(nthThisRun, already)) return false;
        _freeRunLivesGranted[songId] = nthThisRun;
        return progress.GrantRunLife(null, nthThisRun, "Runner LIFE pickup (free run)");
    }

    /// <summary>DEBUG ONLY — force the next fights' opponent (by id, null = none) and/or the tier its
    /// content resolves at (0 = real tier). Production progression/bag/history are untouched.</summary>
    public void DebugForce(string opponentId, int tier)
    {
        DebugForcedOpponentId = string.IsNullOrEmpty(opponentId) ? null : opponentId;
        DebugForcedTier       = Mathf.Max(0, tier);
        Debug.Log($"[GameSession] Debug force: opponent {(DebugForcedOpponentId ?? "(bag)")}, tier {(DebugForcedTier > 0 ? DebugForcedTier.ToString() : "(real)")}");
    }

    public void DebugClearForce() => DebugForce(null, 0);

    public void Configure(FightStatsConfig config) => _fightStatsConfig = config;



    void IAppModule.Initialize(AppContext context) { /* no cross-module wiring needed yet */ }
    void IAppModule.Shutdown() { }

    private void OnEnable()
    {
        _onProfileReady = e =>
        {
            if (e.SelectionGeneration != SelectionGeneration)
            {
                Debug.LogWarning($"[GameSession] Ignored a SongProfileReadyEvent of selection #{e.SelectionGeneration} " +
                                 $"(current #{SelectionGeneration}) — stale profile not adopted.");
                return;
            }
            if (ReferenceEquals(Profile, e.Profile)) return; // RunnerSceneBootstrap's re-publish of the same profile
            Profile = e.Profile;
            // A full analysis is the moment to (re)cache this catalog song's similarity vector.
            var sel = SelectedSong;
            if (sel.HasValue && string.IsNullOrEmpty(sel.Value.LocalFilePath) && !string.IsNullOrEmpty(sel.Value.SongId))
                SongSimilarityCache.Store(sel.Value.SongId, e.Profile);
        };
        _onGameEnded = e =>
        {
            RunnerResults = RunnerResultsBuilder.Build(e, _fightStatsConfig);

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
            // one this makes for you by resetting it here). Extra lives are NOT here any more: they
            // are persistent (PlayerProgressService), saved the moment a LIFE is collected.
            FightResources ??= new FightResources();

            // This run's results + the combos it earned go into the run's own wallet (SPECIALs were
            // already added at pickup time). The score's only role is the run's stars (below) — it is
            // never accumulated into any other progression (see RunRewardRules).
            Run.Score = e.Stats.Score;
            Run.NormalizedScore = e.NormalizedScore;
            Run.Wallet.Add(CombatResourceType.TripleCombo, e.Resources.TripleCombos);
            Run.Wallet.Add(CombatResourceType.QuadCombo, e.Resources.QuadCombos);

            // Evaluation (stars) of this runner phase for the campaign position it belongs to — recorded
            // at once (it stands whatever the fight does; only a better attempt replaces the best).
            var song = SelectedSong;
            CampaignService.OnRunnerFinished(RunnerResults, song.HasValue && string.IsNullOrEmpty(song.Value.LocalFilePath) ? song.Value.SongId : null);

            // Player profile: this run's CONVERTED build (what the fight receives) joins the long-term average — once per
            // run (Fight Again replays the same run and never counts twice).
            var cb = RunnerResults.CombatBuild;
            if (cb != null) PlayerProgressService.Instance?.RecordRunBuild(cb.PunchPower, cb.KickPower, cb.Resistance, cb.Agility);

            EventBus.Publish(new RunnerResultsReadyEvent { Results = RunnerResults });
        };
        // A Runner run starting = a NEW run session (new song, Replay Song or restart): SPECIAL,
        // combos and run statistics start from zero; Runner → Fight never triggers this.
        _onGameStarted = _ =>
        {
            var s = SelectedSong;
            Run = new RunSession(++_runCounter, s.HasValue && !string.IsNullOrEmpty(s.Value.SongId) ? s.Value.SongId : s?.DisplayName);
            // The previous run's preparation is discarded with its charges: this run builds its own when it ends.
            RunnerResults = null;
            Debug.Log($"[GameSession] New run session #{Run.Index} — song '{Run.SongId}'");
        };
        EventBus.Subscribe(_onProfileReady);
        EventBus.Subscribe(_onGameEnded);
        EventBus.Subscribe(_onGameStarted);
    }

    private void OnDestroy()
    {
        if (Instance == this) ReleaseSongContent();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onProfileReady);
        EventBus.Unsubscribe(_onGameEnded);
        EventBus.Unsubscribe(_onGameStarted);
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
    /// <summary>Stable playable-song id (PlayableSongCatalogSO, e.g. "song_007") — the persistent
    /// identity; null for a local file. DisplayName/address is only how it was loaded/shown.</summary>
    public string    SongId;
    public AudioClip Clip;
    /// <summary>Null for a predefined song; set for a locally-picked WAV/MP3.</summary>
    public string    LocalFilePath;

    /// <summary>A published catalog song (prepared from its remote content, never analysed on the device).</summary>
    public bool IsCatalogSong => !string.IsNullOrEmpty(SongId) && string.IsNullOrEmpty(LocalFilePath);
}
