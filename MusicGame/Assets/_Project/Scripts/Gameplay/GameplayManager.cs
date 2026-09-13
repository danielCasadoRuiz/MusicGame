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
    private bool             _running;
    private bool             _audioPlaying;
    private MusicClock       _clock;
    private GameplayTimeline _timeline;
    private int              _nextEventIdx;
    private int              _nextPulseIdx;
    private int              _nextRevealIdx; // distance-based EARLY visual reveal — see RevealEvent()
    private int              _nextMacroIdx;
    private SongProfile      _profile;
    private int              _fallCount;
    private int              _maxPossibleScore;
    private bool             _loggedSongEndSuppressed; // one-shot diagnostic guard, see Update()

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
    // 0..1, earned/maxPossible for THIS song's actual generated timeline — see ComputeMaxPossibleScore.
    public float              NormalizedScore  => _maxPossibleScore > 0 ? Mathf.Clamp01((float)_stats.Score / _maxPossibleScore) : 0f;

    // Debug-only: the same maxJumpHeight * bonusMaxJumpHeightFactor GameplayTimeline uses as the
    // bonus vertical ceiling — exposed here (not duplicated) purely so the debug HUD can show it.
    public float MaxReachableJumpHeight => config.core.gravity < 0f
        ? (config.core.jumpForce * config.core.jumpForce) / (2f * -config.core.gravity) * config.collectibles.bonusMaxJumpHeightFactor
        : 0f;

    // Set true by FallRespawnSystem while it owns the audio (Pause + Play cycle).
    // Prevents the song-end check from firing when audio is Paused for respawn.
    public bool SuppressSongEnd { get; set; }

    // ── Subscriptions ─────────────────────────────────────────────────────────
    private Action<SongProfileReadyEvent>  _onProfile;
    private Action<RingCollectedEvent>     _onRing;
    private Action<PlayerFellEvent>        _onFall;

    private void Awake()
    {
        _stats      = new CollectionStats();
        _clock      = MusicClock.GetOrCreate(gameObject);
        _poolParent = new GameObject("[Pools]").transform;

        // MusicWorldManager must be created before GameplayManager subscribes to SongProfileReadyEvent
        // so its OnEnable() fires first and its subscription runs first (path ready before GameplayManager's coroutine checks it)
        var world = MusicWorldManager.GetOrCreate(gameObject);
        world.Initialize(config);

        _checkpoints = GetComponent<CheckpointSystem>()  ?? gameObject.AddComponent<CheckpointSystem>();

        _fallRespawn = GetComponent<FallRespawnSystem>()  ?? gameObject.AddComponent<FallRespawnSystem>();
        _fallRespawn.Initialize(audioSource, playerController, this, config.core);

        // Pre-built UI prefab instances (Tools > MusicGame > Build UI Prefabs), if the tool has
        // been run — GameplayHUD/PauseController fall back to building their UI procedurally
        // when this is absent, so the game works either way.
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
        horizonWorld.Initialize(config.environment.horizon);
    }

    private void OnEnable()
    {
        _onProfile = e => StartCoroutine(GenerateAndStart(e.Profile));
        _onRing    = e =>
        {
            int points = ScoreFor(e.Type, e.TimingError, e.IsOffTrack);
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
        };
        _onFall = e => ApplyFallPenalty(e.FallSongTime);
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

    private IEnumerator GenerateAndStart(SongProfile profile)
    {
        _profile = profile;

        // Wait for MusicWorldManager to finish generating the path
        yield return new WaitUntil(() => MusicWorldManager.Instance?.IsReady == true);
        var path = MusicWorldManager.Instance.Path;

        // Generate timeline (no startZ needed — eventDistance is path-relative)
        _timeline         = GameplayTimeline.Generate(profile, config, path);
        _maxPossibleScore = ComputePerTypePotential();
        _nextEventIdx  = 0;
        _nextPulseIdx  = 0;
        _nextRevealIdx = 0;
        _nextMacroIdx  = 0;

        // Pools
        InitializePools();

        // Checkpoints
        _checkpoints.Initialize(profile, config.core, path, _timeline);

        // Place player at path start — ON the real musical surface, not the MusicPath
        // centerline (the surface sits above it by the frequency-driven relief; using the
        // centerline directly can start the player metres below the actual ground mesh).
        var     startSample = path.GetSample(0f);
        Vector3 startPos    = MusicWorldManager.Instance != null
            ? MusicWorldManager.Instance.SampleSurface(0f, 0f).position
            : startSample.position;
        playerController.transform.position = startPos;
        playerController.transform.rotation = Quaternion.LookRotation(startSample.tangent, Vector3.up);

        _clock.Initialize(audioSource, config.core.warmupTime, config.core.playerSpeed);

        EventBus.Publish(new LevelGeneratedEvent { RingCount = _timeline.Events.Length });

        _running = true;
        playerController.StartRunning();
        _fallRespawn.Activate();
        EventBus.Publish(new GameStartedEvent());

        yield return new WaitForSeconds(config.core.warmupTime);
        audioSource.Play();
        _clock.ForceUpdate();
        _audioPlaying = true;
    }

    // ── Main loop ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_running || _timeline == null) return;

        // The player's ACTUAL distance (CanonicalDistance + surge) — single source of truth,
        // read from PlayerController rather than re-deriving MusicDistance + ForwardOffset here.
        // Spawn/despawn windows are measured from here, so a surging player doesn't outrun
        // collectibles that haven't spawned yet or lose ones still ahead of them.
        float playerDist    = playerController.ActualDistance;
        float lookAheadD    = playerDist + config.collectibles.spawnLookAhead * config.core.playerSpeed;

        // Activate upcoming events — this only makes them visible/spawned ahead of time so the
        // player can see them coming; it is NOT the synced moment.
        while (_nextEventIdx < _timeline.Events.Length &&
               _timeline.Events[_nextEventIdx].eventDistance <= lookAheadD)
        {
            ActivateEvent(_timeline.Events[_nextEventIdx], _nextEventIdx);
            _nextEventIdx++;
        }

        // EARLY visual reveal — purely spatial, separate from the musical trigger below. This is
        // what makes a ring visibly grow/react AHEAD of the player instead of right as they cross
        // it — the actual beat-synced reaction (full-strength Pulse + BeatPulseEvent/camera kick)
        // still fires exactly on the music's own schedule via the pulseLeadTime loop right after
        // this one, completely untouched. Distance depends on the CURRENT camera view (Third/
        // First Person read differently) — CameraFollow is the single authority for both "which
        // view is active" and "what that maps to" (MusicRunnerCollectiblesConfig.GetBonusVisualActivationDistance),
        // so this changes immediately on a view toggle with zero branching here.
        float revealDistance = CameraFollow.Instance != null
            ? CameraFollow.Instance.EffectiveBonusVisualActivationDistance
            : config.collectibles.bonusVisualActivationDistanceThirdPerson;
        while (_nextRevealIdx < _timeline.Events.Length &&
               _timeline.Events[_nextRevealIdx].eventDistance - playerDist <= revealDistance)
        {
            RevealEvent(_nextRevealIdx);
            _nextRevealIdx++;
        }

        // Fire beat pulses config.collectibles.pulseLeadTime seconds BEFORE the player actually reaches
        // each event. Firing exactly on arrival reads as "already past the player" by the
        // time it's perceived — a small lead keeps the pulse visibly ahead of/reachable by
        // the player while still landing close enough to feel tied to the music.
        while (_nextPulseIdx < _timeline.Events.Length &&
               _clock.SongTime >= _timeline.Events[_nextPulseIdx].eventTime - config.collectibles.pulseLeadTime)
        {
            FirePulse(_nextPulseIdx);
            _nextPulseIdx++;
        }

        // Fire Macro moments (Impact/Drop/BuildupStart/Peak) exactly at their eventTime —
        // independent of whether the moment also spawned a collectible. Consumed by things
        // like MusicEnvironmentController without those systems needing to know about pooling.
        if (_timeline.MacroEvents != null)
        {
            while (_nextMacroIdx < _timeline.MacroEvents.Length &&
                   _clock.SongTime >= _timeline.MacroEvents[_nextMacroIdx].eventTime)
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
            if (ae.evt.eventDistance < recycleThreshold)
            {
                ae.pool.Return(ae.go);
                _activeEvents.RemoveAt(i);
            }
        }

        // Checkpoint tracking
        _checkpoints.UpdateCurrent(_clock.SongTime);

        // Song end — skip if FallRespawnSystem has paused audio for a respawn
        if (_audioPlaying && !audioSource.isPlaying)
        {
            if (SuppressSongEnd)
            {
                // DIAGNOSTIC (temporary): audio genuinely stopped, but something is holding
                // SuppressSongEnd true, so the end screen never triggers — logged once (not
                // every frame) so it's visible in the Console without spamming it.
                if (!_loggedSongEndSuppressed)
                {
                    _loggedSongEndSuppressed = true;
                    Debug.LogWarning("[GameEnd] audio stopped but SuppressSongEnd=true — " +
                                     "game-end is being suppressed and will never fire until it clears.");
                }
                return;
            }
            _loggedSongEndSuppressed = false;

            _audioPlaying = false;
            _running      = false;
            _clock.Stop();
            playerController.StopRunning();
            _fallRespawn.Deactivate();

            // No-Fall Bonus — only for a run that reached the end with zero falls. Applied once,
            // here, right before publishing the final stats.
            if (_fallCount == 0 && config.scoring.noFallScoreMultiplier > 1f)
                _stats.MultiplyScore(config.scoring.noFallScoreMultiplier);

            EventBus.Publish(new GameEndedEvent
            {
                Stats            = _stats,
                FallCount        = _fallCount,
                NormalizedScore  = NormalizedScore,
                MaxPossibleScore = _maxPossibleScore,
                Performance      = BuildGamePerformance(),
            });
        }
    }

    // ── Ring scoring ───────────────────────────────────────────────────────────

    /// <summary>
    /// Kick/Snare/HiHat/Beat/Onset: baseScorePerRing × this song's rarity multiplier for that
    /// type × timing quality. Peak/Impact: their own fixed bonus × timing quality only —
    /// they're structurally guaranteed (alwaysKeep) moments, not statistically rare ones, so
    /// they don't participate in the rarity distribution (see GameplayTimeline).
    /// </summary>
    private int ScoreFor(RingType type, float timingError, bool isOffTrack)
    {
        float timing = TimingMultiplier(timingError);

        float raw = type switch
        {
            RingType.Peak   => config.scoring.peakBonusPoints   * timing,
            RingType.Impact => config.scoring.impactBonusPoints * timing,
            _               => config.scoring.baseScorePerRing * _timeline.RarityMultiplier(type) * timing,
        };
        if (isOffTrack) raw *= config.collectibles.offTrackBonusScoreMultiplier;
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
            int perfectScore = ScoreFor(e.ringType, 0f, e.isOffTrack);
            sum += perfectScore;

            _availableByType.TryGetValue(e.ringType, out int c); _availableByType[e.ringType] = c + 1;
            _maxScoreByType.TryGetValue(e.ringType, out int s);  _maxScoreByType[e.ringType]  = s + perfectScore;
        }
        return Mathf.RoundToInt(sum * Mathf.Max(1f, config.scoring.noFallScoreMultiplier));
    }

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
        return Mathf.Clamp01((songTime - config.core.warmupTime) / _profile.duration);
    }

    /// <summary>Inverse of SongTimeToProgress — turns a protectedProgress fraction back into an
    /// absolute songTime, so it can be compared against _pickupHistory's own songTime stamps.</summary>
    private float ProgressToSongTime(float progress)
    {
        return config.core.warmupTime + Mathf.Clamp01(progress) * (_profile != null ? _profile.duration : 0f);
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
    private void ApplyFallPenalty(float fallSongTime)
    {
        _fallCount++;
        if (_pickupHistory.Count == 0) return; // nothing collected yet — nothing at risk

        float fallProgress      = SongTimeToProgress(fallSongTime);
        float protectedProgress = Mathf.Clamp01(config.scoring.fallProtectionCurve.Evaluate(fallProgress));
        float protectedSongTime = ProgressToSongTime(protectedProgress);

        float fraction = Mathf.Clamp01(config.scoring.fallPenaltyCurve.Evaluate(fallProgress));
        if (fraction <= 0f) return;

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
        }
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

        // _availableByType/_maxScoreByType are NOT cleared here — they describe the timeline
        // itself (unchanged by a restart), not this run's performance.
        _collectedByType.Clear();
        _earnedScoreByType.Clear();
        _timingErrorSumByType.Clear();
        _timingMultSumByType.Clear();
    }

    public void RequestRestartSong() => _fallRespawn.RestartSongManually();

    /// <summary>
    /// Re-arms the pieces a natural song-end stopped (main Update() loop, PlayerController,
    /// FallRespawnSystem) — called by FallRespawnSystem.RestartSong() itself, right after it
    /// replays the audio, so restarting from the END SCREEN doesn't leave gameplay frozen with
    /// _running still false. Harmless to call when already running.
    /// </summary>
    public void ResumeRunning()
    {
        _running      = true;
        _audioPlaying = true;
        playerController.StartRunning();
        _fallRespawn.Activate();
    }

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

        // World position from the event's own precomputed continuous lateral/vertical offsets
        // (decided once in GameplayTimeline.Generate — never re-rolled here), placed on the
        // REAL musical surface at this exact lateral offset (not the path centerline) — same
        // authoritative surface query the ground mesh itself is built from
        // (MusicWorldManager.SampleSurface), so a collectible can never end up under a bass
        // bump that raises the surface above centerline height.
        //
        // Split EXACTLY at evt.floorClearance — not an arbitrary fixed fraction — because a
        // Vector3.Lerp/offset moved along the wrong axis on a TILTED surface (e.g. a steep bass
        // bump) doesn't clear the real ground by the distance it thinks it does: moving `d`
        // units along direction `dir` only clears the true surface by `d * dot(dir, surface.normal)`,
        // which shrinks as the angle between `dir` and the true normal grows. So:
        //   - The floorClearance portion (guarantees the collectible isn't embedded — see
        //     GameplayTimeline.CollectibleMaxHalfHeight) goes along the TRUE surface normal,
        //     correct regardless of tilt.
        //   - Only the portion ABOVE that (extra jump-gameplay height the curve rolled) goes
        //     along the path's stable up direction, so a tilted normal on a steep bump can't
        //     shove a tall jump-height collectible sideways off the path.
        var     sample  = path.GetSample(evt.eventDistance);
        Vector3 pos;
        if (MusicWorldManager.Instance != null)
        {
            var surface = MusicWorldManager.Instance.SampleSurface(evt.eventDistance, evt.lateralOffset);
            const float baseClearance = 0.05f;
            float floorPortion = Mathf.Min(evt.verticalOffset, evt.floorClearance);
            float extraPortion = Mathf.Max(0f, evt.verticalOffset - evt.floorClearance);
            pos = surface.position + surface.normal * (baseClearance + floorPortion) + sample.up * extraPortion;
        }
        else
        {
            pos = sample.position + sample.right * evt.lateralOffset + sample.up * evt.verticalOffset;
        }

        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(sample.tangent, sample.up);
        go.SetActive(true);

        var rc = go.GetComponent<RingController>();
        if (rc != null) rc.Activate(evt, () => pool.Return(go));

        _activeEvents.Add(new ActiveEvent { evt = evt, go = go, pool = pool, index = index });
    }

    // ── Visual reveal (purely spatial, no musical meaning — see Update()'s reveal loop) ───────

    private void RevealEvent(int index)
    {
        var evt = _timeline.Events[index];
        if (evt.eventType != EventType.Ring) return;

        // Mild strength (0) — a soft "notice me" grow, deliberately weaker than the real
        // beat-synced Pulse(evt.strength) that still fires later at the exact musical moment.
        // No BeatPulseEvent here: that's the musical trigger, this is purely visual.
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
            rc.Pulse(evt.strength);
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

        EventBus.Publish(new BeatPulseEvent { Type = evt.ringType, Strength = evt.strength, Position = pos });
    }

    /// <summary>The currently-active RingController for a given timeline index, or null if it's
    /// not spawned/already recycled — shared lookup for RevealEvent and FirePulse.</summary>
    private RingController FindActiveRing(int index)
    {
        for (int i = 0; i < _activeEvents.Count; i++)
            if (_activeEvents[i].index == index)
                return _activeEvents[i].go.GetComponent<RingController>();
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

    public void SetNextEventIndex(int idx)
    {
        _nextEventIdx  = Mathf.Clamp(idx, 0, _timeline?.Events.Length ?? 0);
        _nextPulseIdx  = _nextEventIdx;
        _nextRevealIdx = _nextEventIdx;
    }

    /// <summary>Realigns the Macro-event cursor to a resumed songTime (checkpoint respawn or
    /// restart) — otherwise moments already passed would refire, or ones still ahead would be
    /// silently skipped. Called alongside SetNextEventIndex by FallRespawnSystem.</summary>
    public void ResyncMacroIndex(float songTime)
    {
        _nextMacroIdx = 0;
        var macro = _timeline?.MacroEvents;
        if (macro == null) return;
        while (_nextMacroIdx < macro.Length && macro[_nextMacroIdx].eventTime < songTime)
            _nextMacroIdx++;
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
        _clock.Initialize(audioSource, warmupTime, config.core.playerSpeed);
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
            var prefab   = config.collectibles.ResolvePrefab(captured);
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

        Color ringColor = config.collectibles.RingColor(type);
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = ringColor };

        // Deliberately independent/OFF-by-default emission — see MusicRunnerCollectiblesConfig's
        // own doc on why rings don't automatically bloom just for being colored.
        if (config.collectibles.ringEmissionEnabled)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", ringColor * Mathf.Max(0f, config.collectibles.ringEmissionIntensity));
        }

        _materials[type] = mat;
        return mat;
    }

    private void OnDestroy()
    {
        foreach (var mat in _materials.Values)
            if (mat != null) Destroy(mat);
        if (_poolParent != null) Destroy(_poolParent.gameObject);
    }
}
