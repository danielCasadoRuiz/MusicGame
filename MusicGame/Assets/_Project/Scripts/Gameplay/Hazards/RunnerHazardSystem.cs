using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runs the hazards of one Runner run (created by GameplayManager): builds the obstacles of the deterministic
/// HazardLayout spawnLookAhead metres ahead of the musical front, recycles them behind the player, drives the one
/// chaser, and turns "touched by the chaser" / "trapped" into the shared respawn penalty
/// (FallRespawnSystem.TriggerFall(cause) — the same fade / hold / seek-back as a fall; a normal collision with
/// an obstacle never pauses anything). One penalty at a time: while a respawn is processing nothing else
/// fires, and respawnProtectionSeconds after it neither cause can fire again. Trapped = PlayerController
/// reports it is blocked AT the back limit of its movement area (it can no longer keep up) for trapGraceSeconds.
/// Everything uses scaled time / the music clock, so pause freezes it.
/// </summary>
public class RunnerHazardSystem : MonoBehaviour
{
    public static RunnerHazardSystem Instance { get; private set; }

    private RunnerHazardsConfig _cfg;
    private PlayerController _player;
    private GameplayManager _manager;
    private float _speed;
    private List<HazardPlacement> _placements = new();
    private int _next;

    private readonly Dictionary<RunnerObstacleKind, Stack<RunnerObstacle>> _pool = new();
    private readonly List<(RunnerObstacle obstacle, HazardPlacement placement)> _active = new();
    // Several chasers may run at once (harder levels place them closer together); each is pooled and reused.
    private readonly List<RunnerChaser> _chasers = new();
    private readonly RunnerPlayerTrail _trail = new();

    private float _trapTimer, _protectedUntil;
    private float _difficulty;
    private int _testKind;

    public IReadOnlyList<HazardPlacement> Placements => _placements;
    /// <summary>The first chaser that is not idle (tests / debug), else the first pooled one.</summary>
    public RunnerChaser Chaser
    {
        get
        {
            foreach (var c in _chasers) if (c != null && c.Current != RunnerChaser.State.Inactive) return c;
            return _chasers.Count > 0 ? _chasers[0] : null;
        }
    }
    public int ActiveChaserCount { get { int n = 0; foreach (var c in _chasers) if (c != null && c.Current != RunnerChaser.State.Inactive) n++; return n; } }
    private void DeactivateChasers() { foreach (var c in _chasers) if (c != null) c.Deactivate(); }
    public int ActiveObstacleCount => _active.Count;
    public bool IsProtected => Time.time < _protectedUntil;
    /// <summary>The obstacles currently built (tests / debug).</summary>
    public IEnumerable<RunnerObstacle> ActiveObstacles { get { foreach (var a in _active) yield return a.obstacle; } }

    private System.Action<PlayerFellEvent> _onFell;
    private System.Action<PlayerRespawnedEvent> _onRespawned;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnEnable()
    {
        _onFell = _ => { _trapTimer = 0f; DeactivateChasers(); };
        _onRespawned = e =>
        {
            _protectedUntil = Time.time + (_cfg != null ? _cfg.respawnProtectionSeconds : 1.5f);
            _trapTimer = 0f;
            _trail.Clear();
            // A restart (0) goes back to the start; a respawn keeps every hazard ahead of the player but none
            // closer than respawnClearAhead (the player never reappears inside or right in front of one).
            Resync(e.CheckpointIndex == 0 ? float.MinValue : (_player != null ? _player.ActualDistance + (_cfg != null ? _cfg.respawnClearAhead : 6f) : 0f));
        };
        EventBus.Subscribe(_onFell);
        EventBus.Subscribe(_onRespawned);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFell);
        EventBus.Unsubscribe(_onRespawned);
    }

    public void Initialize(RunnerHazardsConfig cfg, PlayerController player, GameplayManager manager, GameplayTimeline timeline,
                           MusicPath path, int layoutSeed, float difficulty, float playStartSongTime, float playEndSongTime, float speed)
    {
        _cfg = cfg; _player = player; _manager = manager; _speed = speed; _difficulty = difficulty;
        ClearAll();
        _placements = HazardLayout.Generate(cfg, timeline != null ? timeline.Events : null, path, layoutSeed, difficulty, playStartSongTime, playEndSongTime, speed);
        _next = 0;
        int chasers = 0; foreach (var p in _placements) if (p.isChaser) chasers++;
        Debug.Log($"[RunnerHazards] {_placements.Count - chasers} obstacles + {chasers} chasers (difficulty {difficulty:F2}{(cfg != null && cfg.testGallery && RunnerHazardsConfig.TestAllowed ? ", TEST GALLERY" : "")}).");
    }

    // ── Frame ───────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_cfg == null || _player == null || _manager == null || !_manager.IsRunning) return;
        var world = MusicWorldManager.Instance;
        var path = world != null ? world.Path : null;
        if (path == null) return;
        float dt = Time.deltaTime;

        if (_cfg.testHotkeys && RunnerHazardsConfig.TestAllowed) TestHotkeys(path);

        // Spawn ahead of the musical front (the song's schedule), recycle behind the player's real position.
        float front = _player.MusicFrontDistance;
        while (_next < _placements.Count && _placements[_next].distance <= front + _cfg.spawnLookAhead)
            Spawn(_placements[_next++], path, world);
        float behind = _player.ActualDistance - _cfg.recycleBehind;
        for (int i = _active.Count - 1; i >= 0; i--)
            if (_active[i].placement.distance < behind) Recycle(i);

        bool processing = FallRespawnSystem.Instance != null && FallRespawnSystem.Instance.IsProcessing;
        if (!_player.IsFalling && dt > 0f)
            _trail.Record(Time.time, _player.LateralOffset, PlayerHeightAboveGround(world));

        if (!processing)
            foreach (var c in _chasers)
                if (c != null && c.Current != RunnerChaser.State.Inactive) c.Tick(dt, _player, path, world, _trail, _speed);

        if (processing || _player.IsFalling || dt <= 0f) { _trapTimer = 0f; return; }

        // Trapped: blocked at the back limit for long enough (voluntarily standing at the limit is never this).
        _trapTimer = _player.BlockedAtBackLimit ? _trapTimer + dt : 0f;
        if (_trapTimer >= _cfg.trapGraceSeconds) Penalize(RunnerPenaltyCause.Trapped);
        else
            foreach (var c in _chasers)
                if (c != null && c.IsDangerous && c.Touches(_player)) { Penalize(RunnerPenaltyCause.ChaserContact); break; }
    }

    /// <summary>The single penalty gate (also used by tests): ignored while protected or while a respawn runs.</summary>
    public bool Penalize(RunnerPenaltyCause cause)
    {
        var frs = FallRespawnSystem.Instance;
        if (frs == null || frs.IsProcessing || IsProtected) return false;
        _trapTimer = 0f;
        Debug.Log($"[RunnerHazards] penalty: {cause}");
        frs.TriggerFall(cause);
        return true;
    }

    private float PlayerHeightAboveGround(MusicWorldManager world)
    {
        float ground = world.SampleSurface(_player.ActualDistance, _player.LateralOffset).position.y;
        return Mathf.Max(0f, _player.transform.position.y - ground);
    }

    // ── Spawn / recycle ─────────────────────────────────────────────────────────

    private void Spawn(in HazardPlacement p, MusicPath path, MusicWorldManager world)
    {
        if (p.isChaser) { SpawnChaser(p); return; }
        var prefab = _cfg.PrefabFor(p.kind);
        if (prefab == null) return;
        if (!_pool.TryGetValue(p.kind, out var stack)) _pool[p.kind] = stack = new Stack<RunnerObstacle>();
        var o = stack.Count > 0 ? stack.Pop() : Instantiate(prefab, transform);
        o.gameObject.SetActive(true);
        o.Build(p.trackWidth, p.lateral);
        Place(o.transform, p, path, world, o.Length);
        _active.Add((o, p));
    }

    // Base = the highest ground point under the obstacle's footprint (every opening keeps its full height).
    private static void Place(Transform t, in HazardPlacement p, MusicPath path, MusicWorldManager world, float length)
    {
        var s = path.GetSample(p.distance);
        float half = p.trackWidth * 0.5f, top = float.MinValue;
        for (int iz = -1; iz <= 1; iz++)
            for (int ix = -4; ix <= 4; ix++)
            {
                float y = world != null ? world.SampleSurface(p.distance + iz * length * 0.5f, ix / 4f * half).position.y : s.position.y;
                if (y > top) top = y;
            }
        Vector3 fwd = s.tangent; fwd.y = 0f;
        t.position = new Vector3(s.position.x, top, s.position.z);
        t.rotation = Quaternion.LookRotation(fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward, Vector3.up);
    }

    private void SpawnChaser(in HazardPlacement p)
    {
        if (_cfg.chaserPrefab == null) return;
        // An idle pooled chaser, or a new one: several may chase at once.
        RunnerChaser chaser = null;
        foreach (var c in _chasers) if (c != null && c.Current == RunnerChaser.State.Inactive) { chaser = c; break; }
        if (chaser == null) { chaser = Instantiate(_cfg.chaserPrefab, transform); _chasers.Add(chaser); }
        _lastArmed = chaser;
        float d = _difficulty;
        chaser.Arm(new RunnerChaser.Params
        {
            distance = p.distance, side = p.lateral >= 0f ? 1 : -1,
            activation   = _cfg.chaserActivationDistance,
            warning      = RunnerHazardsConfig.Lerp(_cfg.easyChaserWarning, _cfg.hardChaserWarning, d),
            catchUp      = RunnerHazardsConfig.Lerp(_cfg.easyChaserCatchUpSpeed, _cfg.hardChaserCatchUpSpeed, d),
            acceleration = RunnerHazardsConfig.Lerp(_cfg.easyChaserAcceleration, _cfg.hardChaserAcceleration, d),
            followDelay  = RunnerHazardsConfig.Lerp(_cfg.easyChaserFollowDelay, _cfg.hardChaserFollowDelay, d),
            duration     = RunnerHazardsConfig.Lerp(_cfg.easyChaserDuration, _cfg.hardChaserDuration, d),
            lateralSpeed = _cfg.chaserLateralSpeed,
        });
    }

    private void Recycle(int i)
    {
        var (o, p) = _active[i];
        _active.RemoveAt(i);
        o.gameObject.SetActive(false);
        if (!_pool.TryGetValue(p.kind, out var stack)) _pool[p.kind] = stack = new Stack<RunnerObstacle>();
        stack.Push(o);
    }

    private void ClearAll()
    {
        for (int i = _active.Count - 1; i >= 0; i--) Recycle(i);
        DeactivateChasers();
    }

    private RunnerChaser _lastArmed;

    /// <summary>Despawns everything and continues with the first placement beyond `fromDistance`.</summary>
    private void Resync(float fromDistance)
    {
        ClearAll();
        _next = 0;
        while (_next < _placements.Count && _placements[_next].distance < fromDistance) _next++;
    }

    // ── TEST (development only, off by default) ─────────────────────────────────

    /// <summary>Tests: drops the generated placement and everything built (the test then spawns its own).</summary>
    public void ClearGeneratedForTest() { ClearAll(); _placements.Clear(); _next = 0; }

    /// <summary>Builds an obstacle `ahead` metres in front of the player's real position (tests / hotkeys).</summary>
    public RunnerObstacle SpawnTestObstacle(RunnerObstacleKind kind, float ahead, float lateral)
    {
        var world = MusicWorldManager.Instance;
        var path = world != null ? world.Path : null;
        if (path == null || _cfg == null || _cfg.PrefabFor(kind) == null) return null;
        float dist = _player.ActualDistance + ahead;
        Spawn(new HazardPlacement { distance = dist, songTime = dist / Mathf.Max(0.01f, _speed), kind = kind, lateral = lateral, trackWidth = path.GetWidth(dist) }, path, world);
        return _active[_active.Count - 1].obstacle;
    }

    /// <summary>Arms the chaser `ahead` metres in front of the player (tests / hotkeys).</summary>
    public RunnerChaser SpawnTestChaser(float ahead, int side = 1)
    {
        var path = MusicWorldManager.Instance != null ? MusicWorldManager.Instance.Path : null;
        if (path == null || _cfg == null) return null;
        float dist = _player.ActualDistance + ahead;
        DeactivateChasers();
        SpawnChaser(new HazardPlacement { distance = dist, isChaser = true, lateral = side, trackWidth = path.GetWidth(dist) });
        return _lastArmed;
    }

    private void TestHotkeys(MusicPath path)
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.f8Key.wasPressedThisFrame)
        {
            var kind = (RunnerObstacleKind)(_testKind++ % System.Enum.GetValues(typeof(RunnerObstacleKind)).Length);
            Debug.Log($"[RunnerHazards] TEST: {kind} 25 m ahead");
            SpawnTestObstacle(kind, 25f, kind == RunnerObstacleKind.LowBlock || kind == RunnerObstacleKind.HighBlock ? _player.LateralOffset : 0f);
        }
        if (kb.f9Key.wasPressedThisFrame)
        {
            Debug.Log("[RunnerHazards] TEST: chaser 15 m ahead");
            SpawnTestChaser(15f, _player.LateralOffset >= 0f ? -1 : 1);
        }
    }
}
