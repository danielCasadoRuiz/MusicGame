using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameplayManager : MonoBehaviour
{
    [SerializeField] private AudioSource      audioSource;
    [SerializeField] private MusicRunnerGameplayConfig config;
    [SerializeField] private PlayerController playerController;

    // ── Runtime state ─────────────────────────────────────────────────────────
    private CollectionStats  _stats;
    // Combos + Life/Special — everything the run earns that is NOT score (see RunnerResourceTracker).
    private RunnerResourceTracker _resources;
    private bool             _running;
    private bool             _audioPlaying;
    private MusicClock       _clock;
    private GameplayTimeline _timeline;
    private int              _nextEventIdx;
    private int              _nextPulseIdx;
    private int              _nextHitIdx;    // musical cubes: small → full size exactly at eventTime (no lead)
    private int              _nextRevealIdx; // distance-based EARLY visual reveal — see RevealEvent()
    private int              _nextMacroIdx;
    private SongProfile      _profile;
    private int              _fallCount;
    private int              _maxPossibleScore;

    // ── Manual play range (every song source, until a real algorithm exists — see
    // MusicRunnerCoreConfig.useManualPlayRange's own doc) ───────────────────────────────────────
    // The song is always analyzed in full (SongProfile/GameplayTimeline cover the WHOLE clip) —
    // only PLAYBACK is limited to [SongPlayStart, SongPlayEnd]: audioSource starts there instead
    // of at 0, the player spawns at the path distance that position corresponds to (not path
    // distance 0). Nothing about MusicClock/GameplayTimeline/checkpoints needs to know a range is
    // even active — they all key off SongTime/audioSource.time, which already reflects the
    // correct absolute position within the fully-analyzed song regardless of where playback
    // started.
    //
    // The end isn't an abrupt cut: over the LAST MusicRunnerCoreConfig.fadeOutSeconds before
    // SongPlayEnd, the music fades to silence AND the terrain flattens to 0 height together (see
    // MusicWorldManager.EndingFadeMultiplier, driven by this same SongPlayEnd/fadeOutSeconds) —
    // then the player keeps running for farewellSeconds more (MusicClock switched to manual,
    // Time.deltaTime-driven advancement — see MusicClock.BeginManualAdvance) before GameEndedEvent
    // actually fires (see Update()'s own ending-sequence block).
    public float SongPlayStart { get; private set; }
    public float SongPlayEnd   { get; private set; }

    // The song's own logical end point (SongPlayEnd), in SongTime/distance terms. Nothing musical
    // (bonus spawns/reveals/pulses/macro events, see the activation loops in Update()) should
    // still be triggering past this point — the song has genuinely finished by here, even though
    // the farewell stretch (silent, flat) continues a little longer before the run actually ends.
    private float _songEndSongTime; // warmupTime + SongPlayEnd
    private float _songEndDistance; // _songEndSongTime * playerSpeed — same threshold, distance terms
    private bool  _songFinishedAnnounced; // one-shot guard for SongFinishedEvent, see Update()

    // Farewell — a REAL elapsed-time countdown (Time.deltaTime), started the instant
    // SongFinishedEvent fires. See Update()'s own ending-sequence doc.
    private float _farewellElapsed;

    // ── Per-type performance tracking ─────────────────────────────────────────
    // "Available"/per-type max are the actual PLAYABLE timeline for this song (computed once at
    // generation), never the raw SongProfile. Collected/earned/timing sums are monotonic —
    // UNLIKE _stats' display counters, they are NEVER decremented by a fall penalty: a fall is a
    // score mechanic, not an "did you actually reach it" mechanic, and GamePerformance answers
    // the latter question.
    private readonly Dictionary<RingType, int>   _availableByType      = new();
    private readonly Dictionary<RingType, int>   _maxScoreByType       = new();
    private readonly Dictionary<RingType, int>   _collectedByType      = new();
    private readonly Dictionary<RingType, int>   _earnedScoreByType    = new();
    private readonly Dictionary<RingType, float> _timingErrorSumByType = new();
    private readonly Dictionary<RingType, float> _timingMultSumByType  = new();

    // Every individual pickup this run, keyed by type, in collection order, each with the
    // songTime it was collected at. NEVER cleared mid-run (only by ResetRunState) — there are no
    // discrete checkpoints anymore, so instead of clearing a "since last checkpoint" list, a
    // fall partitions this full history by protectedSongTime (see ApplyFallPenalty): everything
    // collected before it is permanently safe, everything after it is "at risk" and subject to
    // the same "trim from the end, most recently collected first" removal the old checkpoint
    // system used.
    private struct ScoredPickup { public float songTime; public int points; }
    private readonly Dictionary<RingType, List<ScoredPickup>> _pickupHistory = new();

    // ── Pool infrastructure ───────────────────────────────────────────────────
    private readonly Dictionary<RingType, ObjectPool> _ringPools = new();
    private Transform                                  _poolParent;
    private readonly Dictionary<RingType, Material>    _materials = new();

    // ── Active events ─────────────────────────────────────────────────────────
    private readonly List<ActiveEvent> _activeEvents = new();

    private struct ActiveEvent
    {
        public TimelineEvent evt;
        public GameObject    go;
        public ObjectPool    pool;
        public int           index; // index into _timeline.Events — links back for pulse lookup
    }

    // ── Subsystems ────────────────────────────────────────────────────────────
    private CheckpointSystem   _checkpoints;
    private FallRespawnSystem  _fallRespawn;

    // ── Public debug info ─────────────────────────────────────────────────────
    public int              ActiveEventCount  => _activeEvents.Count;
    public int              NextEventIndex    => _nextEventIdx;
    public GameplayTimeline Timeline          => _timeline;
    public IReadOnlyDictionary<RingType, ObjectPool> RingPools => _ringPools;
    public MusicRunnerGameplayConfig Config    => config;
    public int              FallCount         => _fallCount;
    public bool              IsRunning        => _running;
    public CollectionStats   Stats             => _stats;
    public int               MaxPossibleScore => _maxPossibleScore;
    public RunnerResourceCounts ResourceCounts => _resources != null ? _resources.Counts : default;
    /// <summary>Pickups of `type` the player can actually meet this run (inside the played window).</summary>
    public int AvailableCount(RingType type) => _availableByType.TryGetValue(type, out int c) ? c : 0;
    // 0..1, earned/maxPossible for THIS song's actual generated timeline — see ComputeMaxPossibleScore.
    public float              NormalizedScore  => _maxPossibleScore > 0 ? Mathf.Clamp01((float)_stats.Score / _maxPossibleScore) : 0f;

    // Debug-only: the same maxJumpHeight * bonusMaxJumpHeightFactor GameplayTimeline uses as the
    // bonus vertical ceiling — exposed here (not duplicated) purely so the debug HUD can show it.
    public float MaxReachableJumpHeight => config.core.gravity < 0f
        ? (config.core.jumpForce * config.core.jumpForce) / (2f * -config.core.gravity) * config.collectibles.bonusMaxJumpHeightFactor
        : 0f;

    // Captured ONCE, before anything ever fades it — see the ending sequence in Update()/
    // GenerateAndStart. FallRespawnSystem's manual-restart fade-in reads THIS (not audioSource's
    // own, possibly still-faded-to-0 volume) as its target, so restarting after a run that ended
    // (and faded out) doesn't silently restart the music at zero volume.
    public float OriginalVolume { get; private set; }

    // ── Subscriptions ─────────────────────────────────────────────────────────
    private Action<SongProfileReadyEvent>  _onProfile;
    private Coroutine _startRoutine;

    /// <summary>Upper bound of the Runner-side technical waits before the countdown (world build, clip data).
    /// Exceeding it is a FAILURE (back to Song Selection), never a start with partial preparation.</summary>
    private const float ReadinessTimeoutSeconds = 30f;
    private Action<RingCollectedEvent>     _onRing;
    private Action<PlayerFellEvent>        _onFall;

    private void Awake()
    {
        OriginalVolume = audioSource.volume;
        GameAudio.RouteMusic(audioSource); // the song plays on the Music channel
        _stats      = new CollectionStats();
        _resources  = new RunnerResourceTracker(config.scoring);
        _clock      = MusicClock.GetOrCreate(gameObject);
        _poolParent = new GameObject("[Pools]").transform;

        // MusicWorldManager must be created before GameplayManager subscribes to SongProfileReadyEvent
        // so its OnEnable() fires first and its subscription runs first (path ready before GameplayManager's coroutine checks it)
        var world = MusicWorldManager.GetOrCreate(gameObject);
        world.Initialize(config, ResolveTrackMaterial());

        _checkpoints = GetComponent<CheckpointSystem>()  ?? gameObject.AddComponent<CheckpointSystem>();

        _fallRespawn = GetComponent<FallRespawnSystem>()  ?? gameObject.AddComponent<FallRespawnSystem>();
        _fallRespawn.Initialize(audioSource, playerController, this, config.core);

        // The Runner scene's UI prefab instances (LiveHud/EndScreen/PauseMenu) — there is no procedural
        // fallback: GameplayHUD/PauseController log an error and leave that part off if one is missing.
        var uiRegistry = FindFirstObjectByType<UIRegistry>();

        var hud = GetComponent<GameplayHUD>() ?? gameObject.AddComponent<GameplayHUD>();
        hud.Initialize(audioSource, config, this, uiRegistry?.LiveHud, uiRegistry?.EndScreen);

        var pause = GetComponent<PauseController>() ?? gameObject.AddComponent<PauseController>();
        pause.Initialize(audioSource, this, uiRegistry?.Pause);

        var environment = GetComponent<MusicEnvironmentController>() ?? gameObject.AddComponent<MusicEnvironmentController>();
        environment.Initialize(config.environment);

        var fog = GetComponent<GameplayFogController>() ?? gameObject.AddComponent<GameplayFogController>();
        fog.Initialize(config.environment);

        var horizonWorld = HorizonWorld.GetOrCreate(gameObject);
        horizonWorld.Initialize(ResolveWorldHorizonConfig());
    }

    // ── Theme resolution (World/Track — see the app-flow/Theme refactor's "World Theme"/"Track
    // Visual Theme" phases) ─────────────────────────────────────────────────────────────────────
    // Theme-first, with a graceful fallback to the gameplay config's own reference — the game must
    // keep working even if ThemeManager hasn't resolved anything yet (or its World/Track category
    // is still null, e.g. before the first Addressable content is ever authored). Never a
    // NullReferenceException: every step here is null-conditional on purpose.

    private HorizonConfig ResolveWorldHorizonConfig()
    {
        var themed = ThemeManager.Instance != null ? ThemeManager.Instance.CurrentTheme?.World?.horizon : null;
        return themed != null ? themed : config.environment.horizon;
    }

    // Null is a legitimate, already-handled result — MusicWorldManager.Initialize falls back to
    // its own default shader/material when pathMaterial is null, so no further fallback needed here.
    private Material ResolveTrackMaterial() =>
        ThemeManager.Instance != null ? ThemeManager.Instance.CurrentTheme?.Track?.trackMaterial : null;

    private void OnEnable()
    {
        _onProfile = e =>
        {
            // One start per scene life: a later publish (another selection) supersedes the pending one —
            // never two GenerateAndStart racing to publish GameStartedEvent.
            if (_startRoutine != null) StopCoroutine(_startRoutine);
            _startRoutine = StartCoroutine(GenerateAndStart(e.Profile, e.SelectionGeneration));
        };
        _onRing    = e =>
        {
            // Life/Special are combat resources: no score, no musical counters, no combos.
            if (RingTypes.IsResource(e.Type))
            {
                _resources.OnResourcePickup(e.Type);
                return;
            }

            int points = ScoreFor(e.Type, e.TimingError, e.ScoreMultiplier);
            _stats.Register(e.Type);
            _stats.AddScore(points);

            // Monotonic performance tracking — deliberately separate from _stats/_pickupHistory
            // above, and never touched by ApplyFallPenalty. A fall penalizes SCORE; it doesn't
            // retroactively mean the player never reached these pickups.
            _collectedByType.TryGetValue(e.Type, out int cc); _collectedByType[e.Type] = cc + 1;
            _earnedScoreByType.TryGetValue(e.Type, out int es); _earnedScoreByType[e.Type] = es + points;
            _timingErrorSumByType.TryGetValue(e.Type, out float te); _timingErrorSumByType[e.Type] = te + e.TimingError;
            _timingMultSumByType.TryGetValue(e.Type, out float tm); _timingMultSumByType[e.Type] = tm + TimingMultiplier(e.TimingError);

            if (!_pickupHistory.TryGetValue(e.Type, out var list))
                _pickupHistory[e.Type] = list = new List<ScoredPickup>();
            list.Add(new ScoredPickup { songTime = _clock.SongTime, points = points });

            _resources.OnMusicalPickup(e.ActualTime);
            PublishStatsChanged();
        };
        _onFall = e => ApplyFallPenalty(e);
        EventBus.Subscribe(_onProfile);
        EventBus.Subscribe(_onRing);
        EventBus.Subscribe(_onFall);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onProfile);
        EventBus.Unsubscribe(_onRing);
        EventBus.Unsubscribe(_onFall);
    }

    // ── Session startup ───────────────────────────────────────────────────────

    /// <summary>
    /// The RUNNER half of the readiness barrier (the content half is SongAnalysisController / CatalogSongLoader).
    /// Explicit completions, in order, each re-checking that this is still the current selection:
    ///   PreparingWorld     MusicWorldManager built the path from this profile (IsReady);
    ///   PreparingConsumers timeline, pools, checkpoints, environment modules loaded (RunnerEnvironmentController
    ///                      .IsBuilt), player placed on the real surface;
    ///   PreparingAudio     the AudioSource holds THIS selection's clip and its data is Loaded (decompressed /
    ///                      compressed-in-memory ready to decode / streaming opened) — a technical wait that
    ///                      happens HERE, before the clock is even initialised, never inside the warmup.
    /// Only then Ready: MusicClock.Initialize + GameStartedEvent (countdown) — the warmup is a game phase,
    /// not a margin to finish loads — and StartPlayback exactly warmupTime later.
    /// </summary>
    private IEnumerator GenerateAndStart(SongProfile profile, int selectionGeneration)
    {
        _profile = profile;
        // Still this selection AND still in Gameplay: leaving (back to Song Selection while the Runner scene was
        // still loading) must never let a late-finishing preparation start the countdown.
        bool Current() => (GameSession.Instance == null || GameSession.Instance.SelectionGeneration == selectionGeneration)
                          && (AppBootstrap.Context == null || AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.Gameplay);
        float deadline = Time.unscaledTime + ReadinessTimeoutSeconds;

        if (!Current()) yield break; // e.g. the Runner scene finished loading after the player left

        // Wait for MusicWorldManager to finish generating the path
        SongPreparation.Enter(selectionGeneration, SongPreparationStage.PreparingWorld);
        while (MusicWorldManager.Instance?.IsReady != true)
        {
            if (!Current()) yield break;
            if (Time.unscaledTime > deadline) { FailStart(selectionGeneration, "world not built in time"); yield break; }
            yield return null;
        }
        if (!Current()) yield break;
        SongPreparation.Enter(selectionGeneration, SongPreparationStage.PreparingConsumers);
        var path = MusicWorldManager.Instance.Path;

        // The bonus layout of THIS combination (song/profile revision × campaign position × difficulty ×
        // generator × config): loaded when it was played before, else generated once from its stable seed
        // and saved (BonusLayoutProvider). Completes here, before the countdown.
        var layout = BonusLayoutProvider.Default.GetOrCreateForCurrentRun(profile, config, path);
        _timeline  = layout.Timeline;
        Debug.Log($"[GameplayManager] Bonus layout {layout.Outcome} in {layout.Milliseconds:F1} ms: seed {layout.Record.seed}, " +
                  $"{_timeline.Events.Length} events, position {layout.Record.key.campaignPosition}, difficulty {layout.Record.key.Difficulty:F3}" +
                  (config.collectibles.diagnostics.logSummary ? $"\n  {_timeline.Stats}" : ""));
        BonusDiagnostics.Bind(gameObject, _timeline, profile, audioSource, config);
        // The played window must be known BEFORE counting what's available: pickups outside it
        // (manual play range) can never be met, so they must not exist for scoring/performance.
        (SongPlayStart, SongPlayEnd) = ResolvePlayRange(profile.duration);
        _maxPossibleScore = ComputePerTypePotential();

        // Pools
        InitializePools();

        // Checkpoints
        _checkpoints.Initialize(profile, config.core, path, _timeline);

        // Environment modules (horizon, music-reactive bars) may be Addressable quality variants still loading:
        // they react from the first musical instant, so they must exist before the countdown.
        var environment = RunnerEnvironmentController.Instance;
        while (environment != null && !environment.IsBuilt)
        {
            if (!Current()) yield break;
            if (Time.unscaledTime > deadline) { FailStart(selectionGeneration, $"environment modules not loaded in time ({environment.PendingLoads} pending)"); yield break; }
            yield return null;
        }
        if (!Current()) yield break;

        // The countdown / hazard sounds are preloaded at boot: never start the countdown before they are.
        while (GameAudio.Instance != null && !GameAudio.Instance.IsReady)
        {
            if (!Current()) yield break;
            if (Time.unscaledTime > deadline) { Debug.LogWarning("[GameplayManager] Sound effects still loading — starting without waiting longer."); break; }
            yield return null;
        }

        _songEndSongTime       = MusicTime.ClipToSong(SongPlayEnd, config.core.warmupTime);
        _songEndDistance       = MusicTime.SongToDistance(_songEndSongTime, config.core.playerSpeed);

        // Hazards (obstacles + chaser): deterministic from THIS layout's seed + difficulty, fitted between its
        // bonuses (which they never move); not stored and not part of the bonus layout key.
        var hazards = GetComponent<RunnerHazardSystem>() ?? gameObject.AddComponent<RunnerHazardSystem>();
        hazards.Initialize(config.hazards, playerController, this, _timeline, path, layout.Record.seed, layout.Record.key.Difficulty,
                           MusicTime.ClipToSong(SongPlayStart, config.core.warmupTime), _songEndSongTime, config.core.playerSpeed);
        _songFinishedAnnounced = false;
        _farewellElapsed       = 0f;

        // The clip that will play must be THIS selection's, with its data ready, BEFORE the countdown — and
        // before the clock is even initialised, so no technical wait can ever advance (or cap) MusicClock.
        SongPreparation.Enter(selectionGeneration, SongPreparationStage.PreparingAudio, audioSource.clip != null ? audioSource.clip.loadType.ToString() : "no clip");
        var expectedClip = GameSession.Instance != null && GameSession.Instance.SelectedSong.HasValue ? GameSession.Instance.SelectedSong.Value.Clip : audioSource.clip;
        if (audioSource.clip == null || (expectedClip != null && audioSource.clip != expectedClip))
        { FailStart(selectionGeneration, "the Runner AudioSource does not hold the selected song's clip"); yield break; }
        if (audioSource.clip.loadState == AudioDataLoadState.Unloaded) audioSource.clip.LoadAudioData();
        while (audioSource.clip.loadState != AudioDataLoadState.Loaded)
        {
            if (!Current()) yield break;
            if (audioSource.clip.loadState == AudioDataLoadState.Failed) { FailStart(selectionGeneration, "audio data failed to load"); yield break; }
            if (Time.unscaledTime > deadline) { FailStart(selectionGeneration, "audio data not ready in time"); yield break; }
            yield return null;
        }
        if (!Current()) yield break;

        // The clock starts AT the play range's start: the warmup countdown runs SongTime from
        // SongPlayStart up to warmupTime + SongPlayStart, exactly where StartPlayback below takes
        // over — so a mid-song start never jumps (see MusicClock.Initialize).
        _clock.Initialize(audioSource, config.core.warmupTime, config.core.playerSpeed, SongPlayStart);

        // Cursors fast-forwarded to the audio's start: nothing before the played window may spawn,
        // pulse or fire a macro (a mid-song start used to burst every earlier event in one frame).
        ResyncCursors(_clock.PlaybackStartSongTime);

        // Place the player where the clock says it belongs right now (its canonical distance),
        // ON the real musical surface, not the MusicPath centerline (the surface sits above it by
        // the frequency-driven relief; using the centerline directly can start the player metres
        // below the actual ground mesh). A start away from distance 0 also needs the ground built
        // there before the first frame.
        // The player starts at its reference distance (the musical front minus the trail); the ground
        // window follows the front, as in MusicWorldManager.Update.
        float   startDistance = playerController.CanonicalDistance;
        if (_clock.MusicDistance > 0f) MusicWorldManager.Instance?.RebuildNow(_clock.MusicDistance);
        var     startSample = path.GetSample(startDistance);
        Vector3 startPos    = MusicWorldManager.Instance != null
            ? MusicWorldManager.Instance.SampleSurface(startDistance, 0f).position
            : startSample.position;
        playerController.transform.position = startPos;
        playerController.transform.rotation = Quaternion.LookRotation(startSample.tangent, Vector3.up);

        EventBus.Publish(new LevelGeneratedEvent
        {
            RingCount       = MusicalAvailableCount(), // musical pickups the player can meet — never Life/Special
            AvailableByType = new Dictionary<RingType, int>(_availableByType),
        });

        // READY: every mandatory dependency completed. Only now may the countdown (and later the music) start.
        SongPreparation.Enter(selectionGeneration, SongPreparationStage.Ready);
        PulseTimingDiagnostics.BeginRun();
        _running = true;
        playerController.StartRunning();
        _fallRespawn.Activate();
        EventBus.Publish(new GameStartedEvent { WarmupTime = config.core.warmupTime });

        yield return new WaitForSeconds(config.core.warmupTime);

        // Should never block: the barrier above required Loaded before the countdown. Kept as an assertion
        // (logged if it ever waits) — MusicClock holds at warmup + SongPlayStart meanwhile (warmup is capped).
        if (audioSource.clip != null && audioSource.clip.loadState == AudioDataLoadState.Loading)
        {
            Debug.LogWarning("[GameplayManager] Clip data still loading after the countdown — the readiness barrier was bypassed.");
            yield return new WaitUntil(() => audioSource.clip.loadState != AudioDataLoadState.Loading);
        }

        _clock.StartPlayback(SongPlayStart, MusicTimeDiscontinuityReason.PlaybackStarted);
        _audioPlaying = true;
        PulseTimingDiagnostics.MarkPlaybackStarted();
        _startRoutine = null;
    }

    // A technical failure BEFORE the countdown: nothing was started (clock never initialised). Back to
    // Song Selection with the reason — never a partially prepared run.
    private void FailStart(int selectionGeneration, string reason)
    {
        _startRoutine = null;
        Debug.LogError($"[GameplayManager] Run #{selectionGeneration} cannot start: {reason} — back to Song Selection.");
        SongPreparation.Enter(selectionGeneration, SongPreparationStage.Failed, reason, -1f, 0, SongContentError.LoadFailed);
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongSelection);
    }

    // A RUNNER concern (see MusicRunnerCoreConfig.useManualPlayRange's own doc) — applies to every
    // song source today (catalog or uploaded alike) until a real "interesting chunk" algorithm
    // exists for catalog/automatic songs. Returns (0, duration) — i.e. "no range" — whenever the
    // manual range doesn't apply.
    private (float start, float end) ResolvePlayRange(float duration)
    {
        var range = PlayRangeResolver.Resolve(config.core, duration);
        return (range.Start, range.End);
    }

    // ── Main loop ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_running || _timeline == null) return;

        // Closes a pending Triple once its window passes (a Quad closes on its own pickup).
        _resources.Tick(_clock.SongTime);

        // The MUSICAL FRONT (== MusicClock.MusicDistance, purely SongTime * speed) — NEVER includes
        // the surge's forwardOffset nor the player's intentional trail. Spawn/reveal timing must
        // track "has the song actually reached this moment yet", not "where is the player standing
        // right now": a surging player (W/↑) getting physically ahead of the beat must never pull a
        // bonus into existence early just because they're now spatially closer to it. Everything on
        // the song's own fixed schedule (activation lookahead, reveal) reads from this.
        float frontDist  = playerController.MusicFrontDistance;
        float lookAheadD = frontDist + config.collectibles.spawnLookAhead * config.core.playerSpeed;

        // The player's ACTUAL distance (CanonicalDistance + surge; CanonicalDistance already trails
        // the front by playerTrailSeconds) — only for things that
        // legitimately care where the player really is in space (recycling something once it's
        // genuinely behind them), never for anything gated on the song's own progress.
        float playerDist    = playerController.ActualDistance;

        // Activate upcoming events — this only makes them visible/spawned ahead of time so the
        // player can see them coming; it is NOT the synced moment. Never past _songEndDistance
        // (the played window's own end, see GenerateAndStart) — once the song has actually
        // finished, no more bonuses should spawn onto what is, by then, flat/silent farewell
        // ground; the ones already active just recycle normally as the player passes them.
        while (_nextEventIdx < _timeline.Events.Length &&
               _timeline.Events[_nextEventIdx].eventDistance <= lookAheadD &&
               _timeline.Events[_nextEventIdx].eventDistance < _songEndDistance)
        {
            ActivateEvent(_timeline.Events[_nextEventIdx], _nextEventIdx);
            _nextEventIdx++;
        }

        // EARLY visual reveal — purely spatial, separate from the musical trigger below. RESOURCES
        // (Life / Special) only: it makes them visibly react AHEAD of the player; musical cubes are
        // skipped (RevealEvent) and stay small until the hit loop below. The beat-synced reaction
        // (BeatPulseEvent/camera kick) still fires on the music's own schedule via the pulse loop
        // right after this one. Distance depends on the CURRENT camera view (Third/
        // First Person read differently) — CameraFollow is the single authority for both "which
        // view is active" and "what that maps to" (MusicRunnerCollectiblesConfig.GetBonusVisualActivationDistance),
        // so this changes immediately on a view toggle with zero branching here.
        float revealDistance = CameraFollow.Instance != null
            ? CameraFollow.Instance.EffectiveBonusVisualActivationDistance
            : config.collectibles.bonusVisualActivationDistanceThirdPerson;
        while (_nextRevealIdx < _timeline.Events.Length &&
               _timeline.Events[_nextRevealIdx].eventDistance - frontDist <= revealDistance &&
               _timeline.Events[_nextRevealIdx].eventDistance < _songEndDistance)
        {
            RevealEvent(_nextRevealIdx);
            _nextRevealIdx++;
        }

        // Beat pulses (BeatPulseEvent/camera kick + the resources' pulse) at eventTime − pulseLeadTime
        // (default 0 = exactly on the musical moment). Musical cubes' size change never uses this
        // lead — see the hit loop below.
        while (_nextPulseIdx < _timeline.Events.Length &&
               _clock.SongTime >= _timeline.Events[_nextPulseIdx].eventTime - config.collectibles.pulseLeadTime &&
               _timeline.Events[_nextPulseIdx].eventTime < _songEndSongTime)
        {
            // Diagnostics only (open 128 ms late-pulse issue): expected vs actual musical time, frame, context.
            PulseTimingDiagnostics.Record(_nextPulseIdx, _timeline.Events[_nextPulseIdx].eventTime - config.collectibles.pulseLeadTime, _clock);
            FirePulse(_nextPulseIdx);
            _nextPulseIdx++;
        }

        // Musical cubes: small → full size EXACTLY at their musical moment (SongTime ≥ eventTime) — not by
        // proximity, not with pulseLeadTime. A cube already collected is not brought back.
        while (_nextHitIdx < _timeline.Events.Length &&
               _clock.SongTime >= _timeline.Events[_nextHitIdx].eventTime &&
               _timeline.Events[_nextHitIdx].eventTime < _songEndSongTime)
        {
            if (RingTypes.IsMusical(_timeline.Events[_nextHitIdx].ringType)) FindActiveRing(_nextHitIdx)?.Hit();
            _nextHitIdx++;
        }

        // Fire Macro moments (Impact/Drop/BuildupStart/Peak) exactly at their eventTime —
        // independent of whether the moment also spawned a collectible. Consumed by things
        // like MusicEnvironmentController without those systems needing to know about pooling.
        // Same _songEndSongTime cutoff as the loops above — nothing "musical" should still be
        // reacting once the song itself has finished.
        if (_timeline.MacroEvents != null)
        {
            while (_nextMacroIdx < _timeline.MacroEvents.Length &&
                   _clock.SongTime >= _timeline.MacroEvents[_nextMacroIdx].eventTime &&
                   _timeline.MacroEvents[_nextMacroIdx].eventTime < _songEndSongTime)
            {
                var m = _timeline.MacroEvents[_nextMacroIdx];
                EventBus.Publish(new MacroEventOccurredEvent { Type = m.type, Strength = m.strength, IsClimax = m.isClimax });
                _nextMacroIdx++;
            }
        }

        // Recycle passed events — only once genuinely unreachable behind the player's actual
        // position, never just because the music's own minimum has advanced past them.
        float recycleThreshold = playerDist - config.collectibles.recycleGrace;
        for (int i = _activeEvents.Count - 1; i >= 0; i--)
        {
            var ae = _activeEvents[i];
            if (!ae.go.activeSelf)
            {
                _activeEvents.RemoveAt(i);
                continue;
            }
            // A musical cube the player has passed without collecting shrinks away, then recycles itself.
            var rcMiss = ae.go.GetComponent<RingController>();
            if (rcMiss != null && rcMiss.IsMusical && !rcMiss.IsMissing && ae.evt.eventDistance < playerDist - rcMiss.MissedBehindDistance)
            {
                rcMiss.Miss();
                continue;
            }
            if (ae.evt.eventDistance < recycleThreshold)
            {
                ae.pool.Return(ae.go);
                _activeEvents.RemoveAt(i);
            }
        }

        // Checkpoint tracking
        _checkpoints.UpdateCurrent(_clock.SongTime);

        // Ending sequence. Volume fades to silence over the LAST fadeOutSeconds before
        // SongPlayEnd (terrain does the matching visual fade — see MusicWorldManager's own
        // EndingFadeMultiplier, driven by the SAME SongPlayEnd/fadeOutSeconds).
        if (_audioPlaying && audioSource.isPlaying && SongPlayEnd > 0f && config.core.fadeOutSeconds > 0f)
        {
            float fadeStart = SongPlayEnd - config.core.fadeOutSeconds;
            float clipTime  = _clock.AudioClipTime; // raw timeSamples-based position, same as the end test below
            if (clipTime >= fadeStart)
                audioSource.volume = Mathf.Lerp(OriginalVolume, 0f, Mathf.Clamp01((clipTime - fadeStart) / config.core.fadeOutSeconds));
        }

        // The song has genuinely ended: fade is already complete (volume is 0 by construction —
        // audioSource.time just reached SongPlayEnd itself), so there's nothing left to keep the
        // AudioSource "playing" for — stop it right here, and switch MusicClock over to manual,
        // Time.deltaTime-driven advancement for the whole farewell that follows (see
        // MusicClock.BeginManualAdvance's own doc on why: a played range ending at/near the
        // clip's own natural end has no spare content to silently draw on, and reactively patching
        // things only once THAT ran out used to leave a gap — this way the player's forward
        // motion never depends on how much of the clip happened to be left AT ALL). This is also
        // when the activation loops above stop spawning anything new, and
        // FinishBannerController shows its "FINISH!" banner.
        //
        // "Reached the end" is MusicClock's explicit evidence (ReachedPlaybackEnd): the raw clip
        // position got to SongPlayEnd while playing, or — when SongPlayEnd is the clip's own end,
        // where Unity stops the source and resets its position — the clock's natural-end detection.
        // Never "!isPlaying" on its own (pause/hold/stop/transition look the same).
        if (!_songFinishedAnnounced && _audioPlaying && SongPlayEnd > 0f && _clock.ReachedPlaybackEnd(SongPlayEnd))
        {
            _songFinishedAnnounced = true;
            audioSource.volume = OriginalVolume; // undo the fade now — nothing left to hide it under
            audioSource.Stop();
            MusicClock.Instance?.BeginManualAdvance();
            // Nothing left to fall INTO reacting to (flat, event-free farewell ground) and the
            // AudioSource was just stopped for good above — a fall-respawn triggered from here on
            // would try to seek/replay audio well past the song's own real content, audibly
            // restarting the track during what's supposed to be a silent victory lap. Turning fall
            // detection off here removes that whole class of bug outright.
            _fallRespawn.Deactivate();
            // Belt-and-suspenders alongside the line above: deactivating fall detection stops the
            // automatic RESPONSE to a fall, but the player could still physically run/strafe off
            // the edge or jump during farewell without it — which used to leave the run silently
            // stuck (MusicClock/farewell timer still counting down toward GameEndedEvent while the
            // player sat off-track with nothing left to catch or resync it). EnterFarewellMode
            // removes the CAUSE instead: input stops steering the player at all from here on, and
            // any lateral offset eases back to dead center every frame, so falling off becomes
            // structurally impossible for the rest of the farewell.
            playerController.EnterFarewellMode();
            EventBus.Publish(new SongFinishedEvent());
        }

        // Farewell — a real elapsed-time countdown (Time.deltaTime).
        if (_audioPlaying && _songFinishedAnnounced)
        {
            _farewellElapsed += Time.deltaTime;

            if (_farewellElapsed >= config.core.farewellSeconds)
            {
                _audioPlaying = false;
                _running      = false;
                _clock.Stop();
                playerController.StopRunning();

                // No-Fall Bonus — only for a run that reached the end with zero falls. Applied
                // once, here, right before publishing the final stats.
                if (_fallCount == 0 && config.scoring.noFallScoreMultiplier > 1f)
                    _stats.MultiplyScore(config.scoring.noFallScoreMultiplier);
                PublishStatsChanged();
                _resources.Flush();

                EventBus.Publish(new GameEndedEvent
                {
                    Stats            = _stats,
                    FallCount        = _fallCount,
                    NormalizedScore  = NormalizedScore,
                    MaxPossibleScore = _maxPossibleScore,
                    Performance      = BuildGamePerformance(),
                    Resources        = _resources.Counts,
                });
            }
        }
    }

    // ── Ring scoring ───────────────────────────────────────────────────────────

    /// <summary>
    /// Kick/Snare/HiHat/Beat/Onset: baseScorePerRing × this song's rarity multiplier for that
    /// type × timing quality. Peak/Impact: their own fixed bonus × timing quality only —
    /// they're structurally guaranteed (alwaysKeep) moments, not statistically rare ones, so
    /// they don't participate in the rarity distribution (see GameplayTimeline).
    /// </summary>
    private int ScoreFor(RingType type, float timingError, float difficultyMultiplier)
    {
        if (RingTypes.IsResource(type)) return 0; // combat resources never score
        float timing = TimingMultiplier(timingError);

        float raw = type switch
        {
            RingType.Peak   => config.scoring.peakBonusPoints   * timing,
            RingType.Impact => config.scoring.impactBonusPoints * timing,
            _               => config.scoring.baseScorePerRing * _timeline.RarityMultiplier(type) * timing,
        };
        // Difficulty reward decided at generation (GameplayTimeline.DifficultyMultiplier): off-track
        // x2 as its own category, otherwise the height bonus x1..x1.5 — never both.
        if (difficultyMultiplier > 0f) raw *= difficultyMultiplier;
        return Mathf.RoundToInt(raw);
    }

    private float TimingMultiplier(float timingError)
    {
        float window = Mathf.Max(0.0001f, config.scoring.maxUsefulTimingWindow);
        float f      = Mathf.Clamp01(timingError / window);
        return config.scoring.timingQualityCurve.Evaluate(f);
    }

    /// <summary>
    /// The denominator for NormalizedScore: what a PERFECT run of THIS song's actual generated
    /// timeline would score — every generated event collected at perfect timing (timingError=0),
    /// zero falls (so the no-fall bonus applies, same as a real perfect run would get). Computed
    /// once, right after Generate(), from the same ScoreFor/rounding the real scoring path uses,
    /// so an actual perfect run reproduces this number exactly rather than approaching it.
    ///
    /// Deliberately NOT "collected / spawned count" — it's earned/possible WEIGHTED score, so it
    /// already accounts for rarity weighting and different collectible types' point values
    /// without needing a separate correction. Independent of how many events a song happens to
    /// generate: a sparse song's max and a dense song's max are each computed from that song's
    /// own set, so 100%-at-perfect-timing always normalizes to 1.0 regardless of count.
    ///
    /// Also fills the per-type Available/MaxPossibleScore breakdown GamePerformance needs, in
    /// the SAME pass — one source of truth instead of two separate tallies that could drift apart.
    /// </summary>
    private int ComputePerTypePotential()
    {
        _availableByType.Clear();
        _maxScoreByType.Clear();

        int sum = 0;
        foreach (var e in _timeline.Events)
        {
            if (!IsInPlayedWindow(e)) continue; // never reachable this run — doesn't exist for scoring
            int perfectScore = ScoreFor(e.ringType, 0f, e.scoreMultiplier);
            sum += perfectScore;

            _availableByType.TryGetValue(e.ringType, out int c); _availableByType[e.ringType] = c + 1;
            _maxScoreByType.TryGetValue(e.ringType, out int s);  _maxScoreByType[e.ringType]  = s + perfectScore;
        }
        return Mathf.RoundToInt(sum * Mathf.Max(1f, config.scoring.noFallScoreMultiplier));
    }

    // The played window in SongTime (see ResolvePlayRange/MusicClock): activation stops at
    // _songEndSongTime and the player spawns at warmup + SongPlayStart, so nothing outside it can
    // ever be collected. Small tolerance so an event exactly on the boundary still counts.
    private bool IsInPlayedWindow(in TimelineEvent e)
    {
        float start = MusicTime.ClipToSong(SongPlayStart, config.core.warmupTime) - 0.05f;
        float end   = SongPlayEnd > 0f ? MusicTime.ClipToSong(SongPlayEnd, config.core.warmupTime) : float.MaxValue / 4f;
        return e.eventTime >= start && e.eventTime < end;
    }

    private int MusicalAvailableCount()
    {
        int n = 0;
        foreach (var kv in _availableByType) if (RingTypes.IsMusical(kv.Key)) n += kv.Value;
        return n;
    }

    private void PublishStatsChanged() => EventBus.Publish(new RunnerStatsChangedEvent { Stats = _stats });

    /// <summary>Builds the per-type performance profile — published in GameEndedEvent at song
    /// end, but safe to call anytime (e.g. live from the debug HUD) since it only reads
    /// already-tracked state. See GamePerformance's own doc comment for what HasData/
    /// CollectionRate/NormalizedScore mean.</summary>
    public GamePerformance BuildGamePerformance()
    {
        var perf = new GamePerformance
        {
            OverallNormalizedScore  = NormalizedScore,
            OverallEarnedScore      = _stats.Score,
            OverallMaxPossibleScore = _maxPossibleScore,
        };

        foreach (var kv in _availableByType)
        {
            var type      = kv.Key;
            int available = kv.Value;
            if (available <= 0) continue; // omit types this song never generated — see GamePerformance doc
            if (RingTypes.IsResource(type)) continue; // Life/Special are resources, not musical performance

            _collectedByType.TryGetValue(type, out int collected);
            _earnedScoreByType.TryGetValue(type, out int earned);
            _maxScoreByType.TryGetValue(type, out int maxScore);
            _timingErrorSumByType.TryGetValue(type, out float errSum);
            _timingMultSumByType.TryGetValue(type, out float multSum);

            perf.ByType[type] = new TypePerformance
            {
                Type                = type,
                Available           = available,
                Collected           = collected,
                CollectionRate      = available > 0 ? (float)collected / available : 0f,
                EarnedScore         = earned,
                MaxPossibleScore    = maxScore,
                NormalizedScore     = maxScore > 0 ? Mathf.Clamp01((float)earned / maxScore) : 0f,
                AverageTimingError  = collected > 0 ? errSum / collected : 0f,
                TimingAccuracy      = collected > 0 ? Mathf.Clamp01(multSum / collected) : 0f,
            };
        }

        return perf;
    }

    // ── Fall penalty ──────────────────────────────────────────────────────────

    /// <summary>0..1 fraction of the song's own duration (NOT including warmup) that `songTime`
    /// represents — the same basis fallPenaltyCurve/fallProtectionCurve are evaluated on.</summary>
    private float SongTimeToProgress(float songTime)
    {
        if (_profile == null || _profile.duration <= 0f) return 0f;
        return Mathf.Clamp01(MusicTime.SongToClip(songTime, config.core.warmupTime) / _profile.duration);
    }

    /// <summary>Inverse of SongTimeToProgress — turns a protectedProgress fraction back into an
    /// absolute songTime, so it can be compared against _pickupHistory's own songTime stamps.</summary>
    private float ProgressToSongTime(float progress)
    {
        return MusicTime.ClipToSong(Mathf.Clamp01(progress) * (_profile != null ? _profile.duration : 0f), config.core.warmupTime);
    }

    /// <summary>
    /// The penalty takes actual COLLECTED BONUSES away, per type, instead of subtracting an
    /// abstract fraction of a score number — so the visible per-type counters (KICK, SNARE...)
    /// drop along with the score, which is what a "you lost some of what you picked up" penalty
    /// should look like.
    ///
    /// No discrete checkpoints anymore — "at risk" is everything in _pickupHistory collected
    /// AFTER protectedSongTime, a CONTINUOUS boundary derived from fallProtectionCurve:
    ///   fallProgress      = SongTimeToProgress(fallSongTime)
    ///   protectedProgress = config.scoring.fallProtectionCurve.Evaluate(fallProgress)   — always <= fallProgress
    ///   protectedSongTime = ProgressToSongTime(protectedProgress)
    /// Everything collected before protectedSongTime is now permanently safe; only pickups
    /// collected between protectedSongTime and the fall itself can be penalized. This is why a
    /// fall late in the song can't wipe out disproportionately more of the run than one early on
    /// — the protected boundary itself creeps forward with fallProgress, so only a bounded
    /// recent slice is ever actually at risk, never "everything since some old fixed point".
    ///
    /// For each type: removedCount = Ceil(atRiskCount × fraction) — ceiling, not floor, so the
    /// penalty never rounds down to "lose nothing" on small counts. The most recently collected
    /// ones of that type are the ones removed. Each removed pickup's score contribution is
    /// exactly what it was worth when collected (its own timing quality and rarity weighting at
    /// that moment) — never a fresh/re-rolled value.
    ///
    /// fraction itself shrinks as the song progresses (config.scoring.fallPenaltyCurve, UNCHANGED), so
    /// the same mistake costs less late in an otherwise-good run.
    /// </summary>
    // Every respawn penalty (fall / chaser contact / trapped) — for now all equivalent to a fall, scaled per
    // cause by RunnerHazardsConfig.PenaltyScale (1 = the fall's own penalty), counted as a fall in the run's
    // evaluation. RunnerPenaltyEvent reports what was really lost and where.
    private void ApplyFallPenalty(PlayerFellEvent e)
    {
        _fallCount++;
        _resources.OnFall(config.scoring.fallCancelsCombo);
        float scale = config.hazards != null ? config.hazards.PenaltyScale(e.Cause) : 1f;
        var (score, pickups) = ApplyFallPenaltyToPickups(e.FallSongTime, scale);
        EventBus.Publish(new RunnerPenaltyEvent { Cause = e.Cause, Position = e.Position, SongTime = e.FallSongTime, ScoreLost = score, PickupsLost = pickups });
        PublishStatsChanged();
    }

    private (int score, int pickups) ApplyFallPenaltyToPickups(float fallSongTime, float scale = 1f)
    {
        int totalScore = 0, totalPickups = 0;
        if (_pickupHistory.Count == 0) return (0, 0); // nothing collected yet — nothing at risk

        float fallProgress      = SongTimeToProgress(fallSongTime);
        float protectedProgress = Mathf.Clamp01(config.scoring.fallProtectionCurve.Evaluate(fallProgress));
        float protectedSongTime = ProgressToSongTime(protectedProgress);

        float fraction = Mathf.Clamp01(config.scoring.fallPenaltyCurve.Evaluate(fallProgress) * Mathf.Max(0f, scale));
        if (fraction <= 0f) return (0, 0);

        foreach (var kv in _pickupHistory)
        {
            var list = kv.Value;
            if (list.Count == 0) continue;

            // list is sorted ascending by songTime (append-only, collection order) — find the
            // first entry collected AFTER protectedSongTime; everything from there on is at risk.
            int riskStart = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].songTime > protectedSongTime) { riskStart = i; break; }
            }
            int atRiskCount = list.Count - riskStart;
            if (atRiskCount <= 0) continue;

            int removeCount = Mathf.Clamp(Mathf.CeilToInt(atRiskCount * fraction), 0, atRiskCount);
            if (removeCount <= 0) continue;

            int lostScore = 0;
            for (int i = 0; i < removeCount; i++)
                lostScore += list[list.Count - 1 - i].points; // most recently collected first
            list.RemoveRange(list.Count - removeCount, removeCount);

            _stats.SubtractScore(lostScore);
            _stats.Unregister(kv.Key, removeCount);
            totalScore += lostScore;
            totalPickups += removeCount;
        }
        return (totalScore, totalPickups);
    }

    // ── Pause / Restart Song API (called by PauseController / FallRespawnSystem) ──────────────

    /// <summary>
    /// Resets score/fallCount/pickup-history for a fresh run of the same song. Does NOT touch
    /// pools/timeline/clock/player position — those are reset by FallRespawnSystem.RestartSong
    /// itself, which calls this exactly once via RequestRestartSong(). Keeping this split avoids
    /// duplicating the pool/clock reset logic that already exists for the fall-respawn path.
    /// </summary>
    public void ResetRunState()
    {
        _stats.Reset();
        _fallCount = 0;
        _pickupHistory.Clear();
        _songFinishedAnnounced = false; // so the "FINISH!" banner correctly re-announces on this fresh run
        _farewellElapsed       = 0f;

        // _availableByType/_maxScoreByType are NOT cleared here — they describe the timeline
        // itself (unchanged by a restart), not this run's performance.
        _collectedByType.Clear();
        _earnedScoreByType.Clear();
        _timingErrorSumByType.Clear();
        _timingMultSumByType.Clear();

        _resources.Reset();
        PublishStatsChanged();
    }

    public void RequestRestartSong() => _fallRespawn.RestartSongManually();

    /// <summary>
    /// Re-arms the pieces a natural song-end stopped (main Update() loop, PlayerController,
    /// FallRespawnSystem) — called by FallRespawnSystem.RestartSong() itself, right at the start
    /// of a manual restart (before its own countdown wait), so restarting from the END SCREEN
    /// doesn't leave gameplay frozen with _running still false. Harmless to call when already
    /// running.
    ///
    /// audioAlreadyPlaying should be false when the caller still has a warmup/countdown wait
    /// ahead of it (RestartSong's own countdown — see MarkAudioPlaying, called once the audio
    /// actually starts): setting _audioPlaying true here while audioSource.isPlaying is still
    /// false would make the very next Update() think the song had already ended.
    /// </summary>
    public void ResumeRunning(bool audioAlreadyPlaying = true)
    {
        _running      = true;
        _audioPlaying = audioAlreadyPlaying;
        playerController.StartRunning();
        _fallRespawn.Activate();
    }

    /// <summary>Called by FallRespawnSystem.RestartSong() the instant it actually calls
    /// audioSource.Play(), once its countdown wait finishes — see ResumeRunning's own doc.</summary>
    public void MarkAudioPlaying() => _audioPlaying = true;

    // ── Session (persists across Restart/Continue within this play session) ──────────────────

    private readonly SessionProgress _session = new();
    public SessionProgress Session => _session;

    /// <summary>
    /// "Continue" — banks THIS run's finished performance into the session total, then starts a
    /// fresh run (same reset mechanism as Restart). Deliberately separate from "Restart", which
    /// discards the run without touching the session: Restart = "this attempt didn't count, try
    /// again"; Continue = "keep this result, and try for more."
    /// </summary>
    public void AccumulateSessionAndRestart()
    {
        _session.AddRun(BuildGamePerformance());
        RequestRestartSong();
    }

    // ── Pool event activation ─────────────────────────────────────────────────

    private void ActivateEvent(in TimelineEvent evt, int index)
    {
        if (evt.eventType != EventType.Ring) return;

        var path = MusicWorldManager.Instance?.Path;
        if (path == null) return;

        if (!_ringPools.TryGetValue(evt.ringType, out var pool))
        {
            Debug.LogWarning($"[GameplayManager] No pool for ring type {evt.ringType}");
            return;
        }

        var go = pool.Get();
        if (go == null) return;

        // World position from the event's own precomputed lateral/vertical offsets (decided once in
        // GameplayTimeline.Generate — never re-rolled here): verticalOffset metres straight above the
        // REAL local ground at the pickup's exact distance + lateral offset (the ground mesh/collider the
        // player stands on — slopes, relief and the bent path included), never sinking into the ground
        // under its footprint. One shared query (MusicWorldManager.PickupPosition) — the debug HUD and
        // PickupGroundReachDiagnostic call the same one.
        var     sample  = path.GetSample(evt.eventDistance);
        Vector3 pos     = PickupWorldPosition(evt, config);

        go.transform.position = pos;
        // Upright (world up, like the player's capsule) so the trigger's vertical extent is exactly its half height.
        Vector3 flatForward = Vector3.ProjectOnPlane(sample.tangent, Vector3.up);
        go.transform.rotation = Quaternion.LookRotation(flatForward.sqrMagnitude > 1e-6f ? flatForward : Vector3.forward, Vector3.up);
        go.SetActive(true);

        var rc = go.GetComponent<RingController>();
        if (rc != null)
        {
            rc.Activate(evt, () => pool.Return(go), playerController.TrailSeconds);
            // Spawned after its musical moment already happened (e.g. after a respawn): shown full size at once.
            if (RingTypes.IsMusical(evt.ringType) && _clock != null && _clock.SongTime >= evt.eventTime) rc.Hit();
        }

        _activeEvents.Add(new ActiveEvent { evt = evt, go = go, pool = pool, index = index });
    }

    /// <summary>Final world position of a generated pickup (see ActivateEvent) — public + static so
    /// debug tools verify the exact same placement. Its bottom keeps collectibleSurfaceClearance above
    /// the ground under its whole (rotating) footprint.</summary>
    public static Vector3 PickupWorldPosition(in TimelineEvent evt, MusicRunnerGameplayConfig cfg)
    {
        var world = MusicWorldManager.Instance;
        if (world == null || world.Path == null) return Vector3.zero;
        float half = GameplayTimeline.CollectibleMaxHalfHeight(evt.ringType, cfg);
        return world.PickupPosition(evt.eventDistance, evt.lateralOffset, evt.verticalOffset,
                                    half + cfg.collectibles.collectibleSurfaceClearance, half * 1.42f);
    }

    // ── Visual reveal (purely spatial, no musical meaning — see Update()'s reveal loop) ───────

    private void RevealEvent(int index)
    {
        var evt = _timeline.Events[index];
        if (evt.eventType != EventType.Ring) return;

        // Resources (Life / Special) only: their mild "notice me" grow is unchanged. MUSICAL cubes never grow
        // by proximity — they stay small until their musical moment (Hit, see Update's hit loop).
        if (RingTypes.IsMusical(evt.ringType)) return;
        FindActiveRing(index)?.Pulse(0f);
    }

    // ── Beat pulse (fires exactly on the musical moment) ──────────────────────

    private void FirePulse(int index)
    {
        var evt = _timeline.Events[index];
        if (evt.eventType != EventType.Ring) return;

        var     rc  = FindActiveRing(index);
        Vector3 pos;
        if (rc != null)
        {
            // Musical cubes change size in Hit() at the exact eventTime (never with this lead);
            // resources keep their pulse animation here.
            if (!RingTypes.IsMusical(evt.ringType)) rc.Pulse(evt.strength);
            pos = rc.transform.position;
        }
        else
        {
            // Ring was already collected/recycled before its own beat arrived (e.g. the
            // player surged ahead of schedule) — the beat still happened, just fall back
            // to its path position for camera/FX purposes.
            var path = MusicWorldManager.Instance?.Path;
            pos = path != null ? path.GetSample(evt.eventDistance).position : default;
        }

        // Life/Special are combat resources, not musical moments — they pulse visually but never
        // drive beat-synced world feedback.
        // EventIndex/EventTime = the pulse's musical identity (see BeatPulseEvent) — what consumers
        // deduplicate on, never the frame it happened to be processed in.
        if (RingTypes.IsMusical(evt.ringType))
            EventBus.Publish(new BeatPulseEvent
            {
                Type       = evt.ringType,
                Strength   = evt.strength,
                Position   = pos,
                EventIndex = index,
                EventTime  = evt.eventTime,
            });
    }

    /// <summary>The currently-active RingController for a given timeline index, or null if it's
    /// not spawned/already recycled — shared lookup for RevealEvent and FirePulse.</summary>
    private RingController FindActiveRing(int index)
    {
        for (int i = 0; i < _activeEvents.Count; i++)
        {
            var ae = _activeEvents[i];
            if (ae.index != index || !ae.go.activeSelf) continue;
            var rc = ae.go.GetComponent<RingController>();
            // A collected cube returns to the pool at once; if the pool already handed the same object to
            // another event this frame, it no longer represents this one.
            if (rc != null && rc.SourceEventTime == ae.evt.eventTime) return rc;
        }
        return null;
    }

    // ── Respawn API (called by FallRespawnSystem) ─────────────────────────────

    public void ReturnAllActiveToPool()
    {
        for (int i = _activeEvents.Count - 1; i >= 0; i--)
        {
            if (_activeEvents[i].go.activeSelf)
                _activeEvents[i].pool.Return(_activeEvents[i].go);
        }
        _activeEvents.Clear();
    }

    /// <summary>
    /// Places every cursor (spawn, reveal, pulse, macro) on the first moment at or after songTime —
    /// the single resync for a run start (mid-song included), fall-respawn and restart. Moments
    /// before songTime are skipped WITHOUT firing (the activation loops only move forward, so this
    /// is what prevents a burst of every earlier event in one frame); moments still ahead are kept.
    /// </summary>
    public void ResyncCursors(float songTime)
    {
        _nextEventIdx  = MusicTime.FirstEventAtOrAfter(_timeline?.Events, songTime);
        _nextPulseIdx  = _nextEventIdx;
        _nextHitIdx    = _nextEventIdx;
        _nextRevealIdx = _nextEventIdx;
        _nextMacroIdx  = MusicTime.FirstMacroAtOrAfter(_timeline?.MacroEvents, songTime);
    }

    /// <summary>
    /// warmupTime 0 for a manual restart (Pause / End Screen) — the player is teleported
    /// straight to distance 0 and audio plays back immediately, with no count-in wait; passing
    /// the normal config.core.warmupTime here would make SongTime hard-snap to that offset the
    /// instant audio starts, leaving the just-teleported player behind where the music/ground
    /// think they should be. The original level-generation warmup (GenerateAndStart) is
    /// unaffected — this only changes what a RESTART's clock re-init does.
    /// </summary>
    public void ReinitializeClock(float warmupTime = 0f)
    {
        // Same start offset as GenerateAndStart: the countdown leads into SongPlayStart, not clip 0.
        _clock.Initialize(audioSource, warmupTime, config.core.playerSpeed, SongPlayStart);
    }

    // ── Pool initialization ───────────────────────────────────────────────────

    private void InitializePools()
    {
        _ringPools.Clear();
        int init = config.collectibles.poolInitialSize;
        int max  = config.collectibles.poolMaxSize;

        foreach (RingType rt in System.Enum.GetValues(typeof(RingType)))
        {
            var captured = rt;
            var prefab   = TryGetThemedRingVisual(captured, out var themedPrefabVisual) && themedPrefabVisual.prefab != null
                ? themedPrefabVisual.prefab
                : config.collectibles.ResolvePrefab(captured);
            _ringPools[captured] = new ObjectPool(
                captured.ToString(),
                () => BuildPoolObject(captured, prefab),
                init, max
            );
        }
    }

    private GameObject BuildPoolObject(RingType type, GameObject prefab)
    {
        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab, _poolParent);
        }
        else
        {
            // Cube fallback — matches the authored ring prefabs' own look (all plain cubes)
            // instead of a visually inconsistent disc/cylinder shape when a type has no
            // assigned prefab.
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(_poolParent);
            go.transform.localScale = type switch
            {
                RingType.Peak   => new Vector3(0.9f, 0.9f, 0.9f),  // stands out — there's only one per song
                RingType.Impact => new Vector3(0.7f, 0.7f, 0.7f),  // rare, hits hard — bigger than the rest
                _               => new Vector3(0.45f, 0.45f, 0.45f),
            };
            var col = go.GetComponent<Collider>();
            col.isTrigger = true;
            Destroy(go.GetComponent<Rigidbody>());
        }

        // Material/colour ALWAYS comes from config.collectibles.RingColor(type) — regardless of whether this
        // type uses a custom prefab (keeps its own mesh/shape) or the fallback cube — so the
        // event's real type is always what determines what you see, never whatever colour a
        // prefab happened to be authored with.
        var renderer = go.GetComponentInChildren<MeshRenderer>();
        if (renderer != null) renderer.material = GetOrCreateMaterial(type);

        var rc = go.GetComponent<RingController>() ?? go.AddComponent<RingController>();
        rc.Setup(type);
        go.name = $"Ring_{type}";
        return go;
    }

    private Material GetOrCreateMaterial(RingType type)
    {
        if (_materials.TryGetValue(type, out var mat)) return mat;

        // Collectible Theme (see the app-flow/Theme refactor's "Collectible Theme" phase) —
        // Theme-first, falling back to MusicRunnerCollectiblesConfig's own resolution exactly like
        // before when no themed entry exists for this RingType (BaseTheme's Collectibles category
        // ships empty — see CollectibleStyle_Base.asset — so this is a zero-behavior-change
        // fallback today).
        bool  hasThemed          = TryGetThemedRingVisual(type, out var themedVisual);
        Color ringColor          = hasThemed ? themedVisual.color : config.collectibles.RingColor(type);
        bool  emissionEnabled    = hasThemed ? themedVisual.emissionEnabled : config.collectibles.ringEmissionEnabled;
        float emissionIntensity  = hasThemed ? themedVisual.emissionIntensity : config.collectibles.ringEmissionIntensity;

        mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = ringColor };

        // Deliberately independent/OFF-by-default emission — see MusicRunnerCollectiblesConfig's
        // own doc on why rings don't automatically bloom just for being colored.
        if (emissionEnabled)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", ringColor * Mathf.Max(0f, emissionIntensity));
        }

        _materials[type] = mat;
        return mat;
    }

    // Looks up CollectibleStyleSO.rings (see CurrentTheme.Collectibles) for an entry matching
    // `type` — used by both the prefab resolution above and the material resolution below, so
    // "does this RingType have a themed override" is answered in exactly one place.
    private bool TryGetThemedRingVisual(RingType type, out CollectibleStyleSO.RingVisual visual)
    {
        var rings = ThemeManager.Instance != null ? ThemeManager.Instance.CurrentTheme?.Collectibles?.rings : null;
        if (rings != null)
        {
            foreach (var r in rings)
            {
                if (r.type != type) continue;
                visual = r;
                return true;
            }
        }
        visual = default;
        return false;
    }

    private void OnDestroy()
    {
        foreach (var mat in _materials.Values)
            if (mat != null) Destroy(mat);
        if (_poolParent != null) Destroy(_poolParent.gameObject);
    }
}
