using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameplayManager : MonoBehaviour
{
    [SerializeField] private AudioSource      audioSource;
    [SerializeField] private GameplayConfig   config;
    [SerializeField] private PlayerController playerController;

    [Tooltip("Seconds ahead of song time to activate rings")]
    [SerializeField] private float spawnLookAhead = 1.5f;

    private CollectionStats               _stats;
    private bool                          _playing;
    private bool                          _running;
    private float                         _runStartTime;
    private RingData[]                    _rings;
    private GameObject[]                  _ringObjects;
    private int                           _nextRingIdx;

    private Action<SongProfileReadyEvent> _onProfile;
    private Action<RingCollectedEvent>    _onRing;

    private readonly Dictionary<RingType, Material> _materials = new();

    private void Awake() => _stats = new CollectionStats();

    private void OnEnable()
    {
        _onProfile = e => StartCoroutine(GenerateAndStart(e.Profile));
        _onRing    = e => _stats.Register(e.Type);
        EventBus.Subscribe(_onProfile);
        EventBus.Subscribe(_onRing);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onProfile);
        EventBus.Unsubscribe(_onRing);
    }

    // ── Level generation ──────────────────────────────────────────────────────

    private IEnumerator GenerateAndStart(SongProfile profile)
    {
        float startZ = playerController.transform.position.z;
        _rings       = new LevelGenerator().Generate(profile, config, startZ);
        _nextRingIdx = 0;

        if (config.createGroundPlane)
            BuildGroundPlane(profile.duration, startZ);

        // Pre-instantiate ALL rings at their positions, but deactivated.
        // No runtime allocation during gameplay — just SetActive toggling.
        _ringObjects = new GameObject[_rings.Length];
        for (int i = 0; i < _rings.Length; i++)
        {
            _ringObjects[i] = CreateRing(_rings[i]);
            _ringObjects[i].SetActive(false);
            if (i % 50 == 0) yield return null; // spread over frames to avoid spike
        }

        EventBus.Publish(new LevelGeneratedEvent { RingCount = _rings.Length });
        Debug.Log($"[Gameplay] Pre-spawned {_rings.Length} rings (all inactive).");

        _runStartTime = Time.time;
        _running      = true;
        playerController.StartRunning(_runStartTime);
        EventBus.Publish(new GameStartedEvent());

        yield return new WaitForSeconds(config.warmupTime);
        audioSource.Play();
        _playing = true;
    }

    // ── Main loop ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_running) return;

        // Effective song time: negative during warmup
        float effectiveTime = (Time.time - _runStartTime) - config.warmupTime;

        // Activate rings entering the look-ahead window
        while (_nextRingIdx < _rings.Length &&
               _rings[_nextRingIdx].Time <= effectiveTime + spawnLookAhead)
        {
            if (_ringObjects[_nextRingIdx] != null)
                _ringObjects[_nextRingIdx].SetActive(true);
            _nextRingIdx++;
        }

        // Detect song end
        if (_playing && !audioSource.isPlaying)
        {
            _playing = false;
            _running = false;
            playerController.StopRunning();
            EventBus.Publish(new GameEndedEvent { Stats = _stats });
        }
    }

    // ── Ring creation (called once at startup) ────────────────────────────────

    private GameObject CreateRing(in RingData data)
    {
        GameObject prefab = ResolvePrefab(data.Type);
        GameObject go     = prefab != null
            ? Instantiate(prefab, data.Position, Quaternion.identity)
            : BuildFallbackCube(data.Type, data.Position);

        var ring = go.GetComponent<RingController>() ?? go.AddComponent<RingController>();
        ring.Setup(data.Type);
        return go;
    }

    // ── Fallback visuals ──────────────────────────────────────────────────────

    private GameObject BuildFallbackCube(RingType type, Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.position   = position;
        go.transform.localScale = Vector3.one * 0.75f;

        var col = go.GetComponent<BoxCollider>();
        col.isTrigger = true;

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        go.GetComponent<MeshRenderer>().material = GetMaterial(type);
        return go;
    }

    private Material GetMaterial(RingType type)
    {
        if (_materials.TryGetValue(type, out var mat)) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = RingColor(type) };
        _materials[type] = mat;
        return mat;
    }

    private static Color RingColor(RingType type) => type switch
    {
        RingType.Kick  => new Color(0.90f, 0.20f, 0.10f),
        RingType.Snare => new Color(0.90f, 0.80f, 0.10f),
        RingType.HiHat => new Color(0.15f, 0.60f, 1.00f),
        RingType.Beat  => new Color(0.60f, 0.20f, 0.90f),
        RingType.Onset => new Color(0.10f, 0.90f, 0.50f),
        _              => Color.white,
    };

    // ── Ground plane ──────────────────────────────────────────────────────────

    private static void BuildGroundPlane(float songDuration, float startZ)
    {
        float trackLength = (songDuration + 10f) * 10f;
        var   go          = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name                 = "Ground";
        go.transform.position   = new Vector3(0f, 0f, startZ + trackLength / 2f);
        go.transform.localScale = new Vector3(2f, 1f, trackLength / 10f);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.08f, 0.08f, 0.10f) };
        go.GetComponent<MeshRenderer>().material = mat;
    }

    // ── Prefab resolution ──────────────────────────────────────────────────────

    private GameObject ResolvePrefab(RingType type)
    {
        GameObject p = type switch
        {
            RingType.Kick  => config.ringKick,
            RingType.Snare => config.ringSnare,
            RingType.HiHat => config.ringHiHat,
            RingType.Beat  => config.ringBeat,
            RingType.Onset => config.ringOnset,
            _              => null,
        };
        return p != null ? p : config.ringDefault;
    }

    private void OnDestroy()
    {
        foreach (var mat in _materials.Values)
            if (mat != null) Destroy(mat);
    }
}
