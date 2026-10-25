using UnityEngine;

/// <summary>
/// The latent black cube. Asleep beside the track (slow bob / turn) → the player comes within
/// activationDistance → a short WARNING (red glow + shiver) → it waits for the player to pass, drifting
/// continuously toward the track → CHASE from behind: its own longitudinal speed (accelerating to the music's
/// pace + catchUpSpeed) and the player's lateral / height position of followDelay seconds ago, approached at a
/// bounded lateral speed — so dodging, jumping and changing direction is how to escape. It never teleports.
/// It gives up (shrinks and recycles) after its duration, or when the player dodged it and it overtook them.
/// Driven by RunnerHazardSystem (Tick), which also decides the contact penalty. Deliberately dark and cold
/// (black + red) so it never reads as a bonus.
/// </summary>
public class RunnerChaser : MonoBehaviour
{
    public enum State { Inactive, Asleep, Warning, WaitingPass, Chasing, Leaving }

    [SerializeField] private Renderer[] glowRenderers;
    [SerializeField] private Color sleepGlow = new(0.25f, 0.02f, 0.02f);
    [SerializeField] private Color warnGlow  = new(4f, 0.15f, 0.05f);
    [SerializeField] private Color chaseGlow = new(2f, 0.05f, 0.02f);
    [Tooltip("Half its size (m) — also the contact radius together with the player's capsule radius.")]
    [Min(0.1f)] public float halfSize = 0.45f;
    [Tooltip("Hover height of its centre above the ground while asleep (m).")]
    [Min(0.5f)] public float sleepHeight = 1.7f;
    [Tooltip("Metres beyond the track edge it sleeps at.")]
    [Min(0f)] public float sleepOutside = 1.2f;

    public State Current { get; private set; } = State.Inactive;
    /// <summary>Contact counts only while chasing.</summary>
    public bool IsDangerous => Current == State.Chasing;
    public float Distance => _s;
    public float Elapsed => _chaseTime;

    // Parameters for this chase (difficulty-dependent, set by Arm).
    private float _anchorDistance, _activation, _warning, _catchUp, _accel, _delay, _duration, _lateralSpeed;
    private float _s, _v, _lat, _height, _timer, _chaseTime, _leaveTimer;
    private int   _side;
    private float _bobAmp = 0.15f;
    private MaterialPropertyBlock _mpb;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public struct Params
    {
        public float distance; public int side;
        public float activation, warning, catchUp, acceleration, followDelay, duration, lateralSpeed;
    }

    public void Arm(in Params p)
    {
        _anchorDistance = p.distance; _side = p.side >= 0 ? 1 : -1;
        _activation = p.activation; _warning = p.warning; _catchUp = p.catchUp; _accel = p.acceleration;
        _delay = p.followDelay; _duration = p.duration; _lateralSpeed = p.lateralSpeed;
        _s = _anchorDistance; _v = 0f; _timer = 0f; _chaseTime = 0f; _leaveTimer = 0f;
        _lat = float.NaN; _height = sleepHeight; _bobAmp = 0.15f;
        transform.localScale = Vector3.one * (halfSize * 2f);
        Current = State.Asleep;
        gameObject.SetActive(true);
        SetGlow(sleepGlow);
    }

    public void Deactivate()
    {
        Current = State.Inactive;
        gameObject.SetActive(false);
    }

    /// <summary>One frame (dt = scaled delta: frozen while paused). `trail` gives the player's lateral / height
    /// above ground `delay` seconds ago.</summary>
    public void Tick(float dt, PlayerController player, MusicPath path, MusicWorldManager world, RunnerPlayerTrail trail, float musicSpeed)
    {
        if (Current == State.Inactive || path == null) return;
        float half = path.GetWidth(_s) * 0.5f;
        if (float.IsNaN(_lat)) _lat = _side * (half + sleepOutside);
        float playerD = player.ActualDistance;
        float t = Time.time;

        switch (Current)
        {
            case State.Asleep:
                if (playerD >= _anchorDistance - _activation)
                {
                    Current = State.Warning; _timer = 0f;
                    var a = GameAudio.Instance; a?.Play(a.Library != null ? a.Library.chaserWake : null, GameAudioScope.Runner);
                }
                break;
            case State.Warning:
                _timer += dt;
                if (_timer >= _warning) Current = State.WaitingPass;
                break;
            case State.WaitingPass:
                // Continuous drift toward the player's line while it lets the player go by.
                _lat = Mathf.MoveTowards(_lat, trail.LateralAt(t - _delay, player.LateralOffset), _lateralSpeed * 0.5f * dt);
                if (playerD >= _anchorDistance + halfSize + 1f)
                {
                    Current = State.Chasing; _v = musicSpeed * 0.85f; _chaseTime = 0f;
                    var a = GameAudio.Instance; a?.Play(a.Library != null ? a.Library.chaserActivate : null, GameAudioScope.Runner);
                }
                break;
            case State.Chasing:
                _chaseTime += dt;
                _v = Mathf.MoveTowards(_v, musicSpeed + _catchUp, _accel * dt);
                _s += _v * dt;
                _lat = Mathf.MoveTowards(_lat, trail.LateralAt(t - _delay, player.LateralOffset), _lateralSpeed * dt);
                _height = Mathf.MoveTowards(_height, trail.HeightAt(t - _delay, 0f) + halfSize + 0.35f, 6f * dt);
                // Dodged: it went past the player without touching — or its time is up.
                if (_s > playerD + halfSize + 1.2f || _chaseTime >= _duration) { Current = State.Leaving; _leaveTimer = 0f; }
                break;
            case State.Leaving:
                _leaveTimer += dt;
                _s += _v * dt;
                _height += 3f * dt;
                transform.localScale = Vector3.one * (halfSize * 2f * Mathf.Clamp01(1f - _leaveTimer / 0.6f));
                if (_leaveTimer >= 0.6f) { Deactivate(); return; }
                break;
        }

        // Pose: on the path at its own distance, its lateral, its height above the local ground.
        var sample = path.GetSample(_s);
        // Ground under it, sampled ON the track (clamped to its edge): beside the track there is no surface,
        // and crossing the edge must not make it jump.
        float ground = world != null ? world.SampleSurface(_s, Mathf.Clamp(_lat, -half, half)).position.y : sample.position.y;
        Vector3 pos = sample.position + sample.right * _lat;
        // The idle bob fades out once it wakes (never a step).
        _bobAmp = Mathf.MoveTowards(_bobAmp, Current == State.Asleep ? 0.15f : 0f, 0.5f * dt);
        pos.y = ground + _height + Mathf.Sin(t * 2.1f) * _bobAmp;
        transform.position = pos;
        float spin = Current switch { State.Asleep => 25f, State.Warning => 400f, State.Chasing => 160f, _ => 90f };
        transform.rotation = Quaternion.Euler(0f, spin * dt, 0f) * transform.rotation;

        Color glow = Current switch
        {
            State.Warning => Color.Lerp(sleepGlow, warnGlow, 0.5f + 0.5f * Mathf.Sin(_timer * 40f)),
            State.Asleep  => sleepGlow * (0.7f + 0.3f * Mathf.Sin(t * 1.5f)),
            _             => chaseGlow,
        };
        SetGlow(glow);
        if (Current == State.Warning) transform.localScale = Vector3.one * (halfSize * 2f * (1f + 0.12f * Mathf.Sin(_timer * 50f)));
        else if (Current != State.Leaving) transform.localScale = Vector3.one * (halfSize * 2f);
    }

    /// <summary>Touching the player's capsule (sphere of halfSize vs the capsule segment).</summary>
    public bool Touches(PlayerController player)
    {
        float r = 0.45f, h = player.CurrentCapsuleHeight;
        Vector3 feet = player.transform.position;
        Vector3 a = feet + Vector3.up * r, b = feet + Vector3.up * Mathf.Max(r, h - r);
        Vector3 p = transform.position;
        Vector3 ab = b - a;
        float k = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return (p - (a + ab * k)).magnitude < r + halfSize * 0.9f;
    }

    private void SetGlow(Color c)
    {
        if (glowRenderers == null) return;
        _mpb ??= new MaterialPropertyBlock();
        foreach (var rd in glowRenderers)
        {
            if (rd == null) continue;
            rd.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionId, c);
            _mpb.SetColor(BaseColorId, new Color(Mathf.Min(1f, c.r * 0.5f), c.g * 0.3f, c.b * 0.3f));
            rd.SetPropertyBlock(_mpb);
        }
    }
}

/// <summary>The player's recent lateral offset and height above ground, sampled every frame (the chaser follows
/// it with a delay). Fixed ring buffer — no allocation while running.</summary>
public class RunnerPlayerTrail
{
    private readonly float[] _t, _lat, _h;
    private int _head, _count;

    public RunnerPlayerTrail(int capacity = 512) { _t = new float[capacity]; _lat = new float[capacity]; _h = new float[capacity]; }

    public void Clear() { _head = 0; _count = 0; }

    public void Record(float time, float lateral, float height)
    {
        _t[_head] = time; _lat[_head] = lateral; _h[_head] = height;
        _head = (_head + 1) % _t.Length;
        _count = Mathf.Min(_count + 1, _t.Length);
    }

    public float LateralAt(float time, float fallback) => Sample(time, _lat, fallback);
    public float HeightAt(float time, float fallback)  => Sample(time, _h, fallback);

    // Newest entry at or before `time` (the oldest one if `time` predates the buffer).
    private float Sample(float time, float[] values, float fallback)
    {
        if (_count == 0) return fallback;
        int idx = (_head - 1 + _t.Length) % _t.Length;
        for (int n = 0; n < _count; n++)
        {
            if (_t[idx] <= time) return values[idx];
            if (n == _count - 1) break;
            idx = (idx - 1 + _t.Length) % _t.Length;
        }
        return values[idx];
    }
}
