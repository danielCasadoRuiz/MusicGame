using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Rings knocked out": when the chaser touches the player, the bonuses that penalty took away
/// (RunnerPenaltyEvent.LostTypes, sampled down to scatterMaxPieces) burst out of the player in every direction,
/// bounce around the scenery with real physics, blink and vanish. Visual only — the pieces are copies of the
/// bonus visuals with no RingController/trigger (GameplayManager.BuildRingVisualCopy), so they can never be
/// collected back. Pooled per type and prewarmed when the run starts (Initialize), so a scatter instantiates
/// nothing. Scaled time: the pause menu freezes them with everything else. Settings: RunnerHazardsConfig.
/// </summary>
public class RunnerPenaltyScatter : MonoBehaviour
{
    private sealed class Piece
    {
        public RingType type;
        public GameObject go;
        public Rigidbody rb;
        public BoxCollider box;
        public Renderer[] renderers;
        public Vector3 scale;
        public float bornAt, floorY;
    }

    private RunnerHazardsConfig _cfg;
    private GameplayManager _manager;
    private PlayerController _player;
    private Transform _root;
    private PhysicsMaterial _material;
    private readonly Dictionary<RingType, Stack<Piece>> _pool = new();
    private readonly List<Piece> _active = new();
    private readonly List<RingType> _types = new();
    private Collider[] _playerColliders;

    private System.Action<RunnerPenaltyEvent> _onPenalty;
    private System.Action<PlayerRespawnedEvent> _onRespawned;

    public int ActiveCount => _active.Count;

    private void OnEnable()
    {
        _onPenalty = OnPenalty;
        // A restart (0) wipes the run: nothing of the old one keeps bouncing.
        _onRespawned = e => { if (e.CheckpointIndex == 0) RecycleAll(); };
        EventBus.Subscribe(_onPenalty);
        EventBus.Subscribe(_onRespawned);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onPenalty);
        EventBus.Unsubscribe(_onRespawned);
        RecycleAll();
    }

    private void OnDestroy()
    {
        if (_root != null) Destroy(_root.gameObject);
        if (_material != null) Destroy(_material);
    }

    /// <summary>Per run (GameplayManager, after its pools exist): drops the previous run's pieces (the bonus types
    /// may differ) and prewarms scatterPrewarm pieces spread over this run's musical bonus types.</summary>
    public void Initialize(RunnerHazardsConfig cfg, GameplayManager manager, PlayerController player)
    {
        _cfg = cfg; _manager = manager; _player = player;
        _playerColliders = player != null ? player.GetComponentsInChildren<Collider>(true) : null;
        RecycleAll();
        foreach (var stack in _pool.Values)
            foreach (var p in stack) if (p.go != null) Destroy(p.go);
        _pool.Clear();
        if (_cfg == null || _manager == null) return;

        if (_root == null) _root = new GameObject("[PenaltyScatter]").transform;
        if (_material == null) _material = new PhysicsMaterial("PenaltyScatter") { frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Maximum };
        _material.bounciness = _cfg.scatterBounciness;
        _material.dynamicFriction = _material.staticFriction = 0.2f;

        _types.Clear();
        foreach (var t in _manager.PooledRingTypes) if (RingTypes.IsMusical(t)) _types.Add(t);
        if (_types.Count == 0) return;
        for (int i = 0; i < _cfg.scatterPrewarm; i++)
        {
            var p = Build(_types[i % _types.Count]);
            if (p != null) PoolOf(p.type).Push(p);
        }
    }

    // ── Penalty ─────────────────────────────────────────────────────────────────

    private void OnPenalty(RunnerPenaltyEvent e)
    {
        if (_cfg == null || _manager == null) return;
        // Caught: the body falls to the floor and lies there (not a frozen pose) until the respawn.
        if (e.Cause == RunnerPenaltyCause.ChaserContact && _player != null && _player.TryGetComponent(out RunnerAvatarAnimator body))
            body.PlayCaught(_cfg.caughtKnockdownClip, _cfg.caughtDownedClip, _cfg.caughtKnockdownSpeed);
        bool wanted = e.Cause == RunnerPenaltyCause.ChaserContact ? _cfg.scatterOnChaserContact : _cfg.scatterOnOtherPenalties;
        if (!wanted) return;

        // As many as the player HAD (none → nothing), at most scatterMaxPieces. The held pickups come grouped by
        // type: even sampling keeps each type's share.
        int held = e.HeldTypes != null ? e.HeldTypes.Length : 0;
        int count = Mathf.Min(held, _cfg.scatterMaxPieces);
        if (count <= 0) return;
        var origin = e.Position + Vector3.up * 1f;
        float phase = Random.value * 360f;
        for (int i = 0; i < count; i++)
        {
            RingType type = e.HeldTypes[(int)((i + 0.5f) * held / count)];
            // Evenly around the player (a ring of bonuses), each with its own jitter.
            float angle = phase + 360f * i / count + Random.Range(-12f, 12f);
            Launch(type, origin, Quaternion.Euler(0f, angle, 0f) * Vector3.forward);
        }
    }

    private void Launch(RingType type, Vector3 origin, Vector3 dir)
    {
        var p = PoolOf(type).Count > 0 ? PoolOf(type).Pop() : Build(type);
        if (p == null) return;
        p.go.transform.SetPositionAndRotation(origin + dir * 0.35f, Random.rotation);
        p.go.transform.localScale = p.scale;
        foreach (var r in p.renderers) r.enabled = true;
        p.go.SetActive(true);
        // Out of the player, never against it (and never pushing it). Re-applied on every launch: deactivating a
        // collider resets IgnoreCollision.
        if (_playerColliders != null)
            foreach (var pc in _playerColliders) if (pc != null) Physics.IgnoreCollision(p.box, pc);
        p.rb.linearVelocity = dir * Random.Range(_cfg.scatterOutSpeed.x, _cfg.scatterOutSpeed.y)
                            + Vector3.up * Random.Range(_cfg.scatterUpSpeed.x, _cfg.scatterUpSpeed.y);
        p.rb.angularVelocity = Random.onUnitSphere * _cfg.scatterSpin;
        p.bornAt = Time.time;
        p.floorY = origin.y - 30f;
        _active.Add(p);
    }

    // ── Frame ───────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_active.Count == 0 || _cfg == null) return;
        float life = _cfg.scatterLifetime, blinkFrom = life - Mathf.Min(_cfg.scatterBlinkSeconds, life);
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var p = _active[i];
            float age = Time.time - p.bornAt;
            if (p.go == null) { _active.RemoveAt(i); continue; }
            if (age >= life || p.go.transform.position.y < p.floorY) { Recycle(i); continue; }
            // Blinks faster as it is about to vanish.
            bool show = age < blinkFrom || Mathf.Repeat((age - blinkFrom) * Mathf.Lerp(8f, 18f, (age - blinkFrom) / (life - blinkFrom + 1e-4f)), 1f) < 0.55f;
            foreach (var r in p.renderers) r.enabled = show;
        }
    }

    private void Recycle(int i)
    {
        var p = _active[i];
        _active.RemoveAt(i);
        if (p.go == null) return;
        p.rb.linearVelocity = Vector3.zero;
        p.rb.angularVelocity = Vector3.zero;
        p.go.SetActive(false);
        PoolOf(p.type).Push(p);
    }

    private void RecycleAll()
    {
        for (int i = _active.Count - 1; i >= 0; i--) Recycle(i);
    }

    private Stack<Piece> PoolOf(RingType type)
    {
        if (!_pool.TryGetValue(type, out var s)) _pool[type] = s = new Stack<Piece>();
        return s;
    }

    // ── Build ───────────────────────────────────────────────────────────────────

    private Piece Build(RingType type)
    {
        var go = _manager.BuildRingVisualCopy(type, _root);
        if (go == null) return null;
        go.name = $"Scatter_{type}";
        var scale = go.transform.localScale * _cfg.scatterScale;
        go.transform.localScale = scale;

        // A box around the visual meshes (in the root's space), for the bounces.
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var bounds = new Bounds();
        bool any = false;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            for (int c = 0; c < 8; c++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                var local = go.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                else bounds.Encapsulate(local);
            }
        }
        var box = go.AddComponent<BoxCollider>();
        box.center = any ? bounds.center : Vector3.zero;
        box.size = any ? Vector3.Max(bounds.size, Vector3.one * 0.05f) : Vector3.one;
        box.sharedMaterial = _material;

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.1f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.3f;
        rb.maxAngularVelocity = 50f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        return new Piece { type = type, go = go, rb = rb, box = box, renderers = go.GetComponentsInChildren<Renderer>(true), scale = scale };
    }
}
