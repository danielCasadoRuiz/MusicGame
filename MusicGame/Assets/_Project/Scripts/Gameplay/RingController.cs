using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RingController : MonoBehaviour
{
    [Tooltip("Fractional scale a ring sits at from spawn until its beat pulse fires — kept " +
             "small so upcoming rings read as a faint hint rather than cluttering the view; " +
             "the pulse then grows it into full existence exactly on its musical moment.")]
    [SerializeField] private float ghostScale = 0.4f;

    [Tooltip("Trigger size multiplier over the VISUAL mesh's own size, applied once fully " +
             "materialized (factor=1). Authored prefabs can be small/cute (e.g. 0.3 scale) and " +
             "still be fair to pick up while running past at speed — the pickup RANGE doesn't " +
             "have to match the visual size 1:1. Increase if a type still feels hard to hit; " +
             "1 = exactly the visual mesh's size, no leeway.")]
    [SerializeField] private float pickupLeeway = 2f;

    [Tooltip("MUSICAL cubes: seconds a cube the player passed without collecting takes to shrink away before it is recycled.")]
    [SerializeField] private float missShrinkDuration = 0.25f;
    [Tooltip("MUSICAL cubes: the cube counts as passed (missed) once it is this far BEHIND the player along the path.")]
    [SerializeField] private float missedBehindDistance = 1.5f;

    [Header("Collection Feedback")]
    [Tooltip("Played the instant this collectible is collected — assign a clip per prefab so " +
             "each bonus type has its own sound, independent of the visual/colour feedback.")]
    [SerializeField] private AudioClip collectSound;
    [Range(0f, 1f)]
    [SerializeField] private float     collectVolume = 1f;

    public RingType Type { get; private set; }

    /// <summary>World trigger size over the prefab's own collider size (see SetVisualScale) — read by
    /// GameplayTimeline's reach model (CollectibleTriggerHalfHeight) so placement uses the REAL pickup volume.</summary>
    public float PickupLeeway => Mathf.Max(1f, pickupLeeway);

    private bool     _collected;
    private bool     _hit;      // musical cube: its musical moment has happened (full size)
    private bool     _missing;  // musical cube: passed without collecting, shrinking away
    private Vector3  _originalScale;
    private TimelineEvent _sourceEvent;  // the generated event this pooled instance currently represents
    private float    _encounterDelay; // seconds after eventTime the player is expected to meet it (see Activate)
    private Action   _returnCallback; // set by ObjectPool owner via Activate()
    private Coroutine _pulseRoutine;
    private Collider  _collider;
    private Renderer  _renderer;
    private BoxCollider _boxCollider;   // non-null only if the collider is a BoxCollider
    private Vector3     _colliderBaseSize;

    /// <summary>
    /// Called once when the pool creates this object. Sets type, trigger, and scale reference.
    /// </summary>
    public void Setup(RingType type)
    {
        Type           = type;
        _originalScale = transform.localScale;
        _collider      = GetComponent<Collider>();
        _collider.isTrigger = true;
        _renderer      = GetComponentInChildren<Renderer>();

        _boxCollider = _collider as BoxCollider;
        if (_boxCollider != null) _colliderBaseSize = _boxCollider.size;
    }

    /// <summary>
    /// Called by GameplayManager each time this object is taken from the pool.
    /// onReturn is invoked at the end of the collect animation to return this GO to the pool.
    /// Starts at ghost scale — Pulse() grows it to full size on its musical moment.
    /// The COLLIDER is full-size and collectible from this very first frame regardless — see
    /// SetVisualScale — only the visual "materialize" grows with the beat pulse.
    /// </summary>
    /// <param name="encounterDelay">PlayerController.TrailSeconds: the player (without surge) meets this pickup that
    /// long AFTER its musical moment, so timing is scored against eventTime + encounterDelay (the hit stays at eventTime).</param>
    public void Activate(TimelineEvent evt, Action onReturn, float encounterDelay = 0f)
    {
        Type            = evt.ringType;
        _encounterDelay = Mathf.Max(0f, encounterDelay);
        _sourceEvent    = evt;
        _returnCallback = onReturn;
        _collected      = false;
        _hit            = false;
        _missing        = false;
        _pulseRoutine   = null; // SetActive(false) already killed any running coroutine — no state is inherited from the pool
        SetVisualScale(ghostScale);
        if (_collider != null) _collider.enabled = true; // undo the disable a previous collection did
        if (_renderer != null) _renderer.enabled = true; // undo the hide a previous collection did
    }

    // Scales the VISUAL transform (ghost → pulse → full) while keeping the trigger collider at
    // a constant world-space size (scaled up by pickupLeeway over the visual mesh) — a
    // collectible must be equally collectible the instant it appears as it is once fully
    // "materialized", AND fair to hit while running past at speed even if its authored visual
    // is small. Compensates a BoxCollider's size inversely to the scale factor (world size =
    // size * scale, so size = base*leeway / factor keeps a constant, leeway-scaled world size);
    // other collider shapes fall back to scaling normally with the visual (only these prefabs
    // use BoxCollider today).
    private void SetVisualScale(float factor)
    {
        transform.localScale = _originalScale * factor;
        if (_boxCollider != null && factor > 0.0001f)
            _boxCollider.size = _colliderBaseSize * Mathf.Max(1f, pickupLeeway) / factor;
    }

    private void OnEnable()
    {
        // Safety reset in case Activate() hasn't been called yet (first activation)
        _collected = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_collected) return;
        if (other.GetComponent<PlayerController>() == null) return;

        _collected = true;
        if (_pulseRoutine != null) { StopCoroutine(_pulseRoutine); _pulseRoutine = null; }

        // Instant, unambiguous "got it": collider and the main visual are gone THIS frame —
        // no lingering shrink animation the player could read as "did I get that?". A separate
        // decoupled pickup effect (particles/flash/pop) can hook into RingCollectedEvent below
        // without needing this GameObject to stay visible for it.
        if (_collider != null) _collider.enabled = false;
        if (_renderer != null) _renderer.enabled = false;

        // PlayClipAtPoint spins up its own temporary AudioSource and cleans itself up — the
        // clip keeps playing even though this GameObject returns to the pool (and potentially
        // gets moved/reused) on the very same frame.
        if (collectSound != null)
        {
            // On the Sfx channel (its own volume); a loose one-shot only without GameAudio (isolated test scenes).
            if (GameAudio.Instance != null) GameAudio.Instance.PlayClip(collectSound, collectVolume, GameAudioScope.Runner);
            else AudioSource.PlayClipAtPoint(collectSound, transform.position, collectVolume);
        }

        // Expected = when the player, running its intentional trail behind the musical front, meets it —
        // not the hit moment itself (that would penalise every normal, non-surge pickup by the trail).
        float expectedTime = _sourceEvent.eventTime + _encounterDelay;
        float actualTime   = MusicClock.Instance != null ? MusicClock.Instance.SongTime : expectedTime;
        float timingError  = Mathf.Abs(actualTime - expectedTime);
        EventBus.Publish(new RingCollectedEvent
        {
            Type         = Type,
            Position     = transform.position,
            TimingError  = timingError,
            ExpectedTime = expectedTime,
            ActualTime   = actualTime,
            Strength     = _sourceEvent.strength,
            Confidence   = _sourceEvent.confidence,
            Contributors = _sourceEvent.contributors,
            IsOffTrack   = _sourceEvent.isOffTrack,
            ScoreMultiplier = _sourceEvent.scoreMultiplier,
        });

        var cb = _returnCallback;
        _returnCallback = null;
        cb?.Invoke(); // → pool.Return(this.gameObject) → SetActive(false)
    }

    /// <summary>Distance behind the player (along the path) at which a musical cube counts as passed.</summary>
    public float MissedBehindDistance => missedBehindDistance;
    /// <summary>eventTime of the event this pooled instance currently represents.</summary>
    public float SourceEventTime => _sourceEvent.eventTime;
    public bool IsMusical => RingTypes.IsMusical(Type);
    public bool IsMissing => _missing;
    public bool IsHit => _hit;
    public bool IsCollected => _collected;
    /// <summary>Diagnostics: current visual size relative to the prefab (ghost before the hit, 1 after).</summary>
    public float VisualScaleFactor => _originalScale.x != 0f ? transform.localScale.x / _originalScale.x : 1f;
    public float GhostScale => ghostScale;

    /// <summary>
    /// MUSICAL cube, its musical moment (MusicClock.SongTime reached eventTime — no lead, no proximity):
    /// small → full size INSTANTLY, no growth ramp and no overshoot that would shrink it back. It then stays
    /// full size while it is collectable. A cube already collected is gone and is not brought back.
    /// The collider is untouched (constant world-size trigger, see SetVisualScale).
    /// </summary>
    public void Hit()
    {
        if (_collected || _missing || _hit) return;
        if (_pulseRoutine != null) { StopCoroutine(_pulseRoutine); _pulseRoutine = null; }
        _hit = true;
        SetVisualScale(1f);
    }

    /// <summary>MUSICAL cube the player passed without collecting: no longer collectable, shrinks smoothly from
    /// its current size to nothing, then returns to the pool (the same callback a collection uses).</summary>
    public void Miss()
    {
        if (_collected || _missing) return;
        _missing = true;
        if (_collider != null) _collider.enabled = false;
        if (_pulseRoutine != null) StopCoroutine(_pulseRoutine);
        _pulseRoutine = StartCoroutine(MissAnimation());
    }

    private IEnumerator MissAnimation()
    {
        float from = _originalScale.x != 0f ? transform.localScale.x / _originalScale.x : 1f;
        float t = 0f;
        while (t < missShrinkDuration)
        {
            t += Time.deltaTime;
            transform.localScale = _originalScale * Mathf.Lerp(from, 0f, Mathf.Clamp01(t / missShrinkDuration));
            yield return null;
        }
        _pulseRoutine = null;
        var cb = _returnCallback;
        _returnCallback = null;
        cb?.Invoke(); // → pool.Return → SetActive(false)
    }

    /// <summary>
    /// RESOURCE pickups (Life / Special) only — unchanged visual. Beat-synced "materialize" — fired by GameplayManager the instant MusicClock.SongTime
    /// reaches this ring's musical moment, independent of collision. The ring is already
    /// visible in advance at ghost scale (anticipation); THIS is the actual musical hit, and
    /// the attack must land on the exact frame it's called — any eased ramp-up here reads as
    /// "growing" instead of "hitting", diluting the beat. So the jump to punch scale is
    /// instantaneous (one frame); only the settle back down to full size (1.0) is animated.
    /// </summary>
    public void Pulse(float strength)
    {
        if (_collected) return;
        if (_pulseRoutine != null) StopCoroutine(_pulseRoutine);

        var   profile = GetProfile(Type);
        float amount  = Mathf.Clamp01(0.35f + strength * 0.65f);
        float peak    = 1f + amount * profile.overshoot;

        SetVisualScale(peak); // instant attack — no lerp from ghost scale
        _pulseRoutine = StartCoroutine(SettleAnimation(peak, profile));
    }

    // Per-type feel: Kick reads as heavy (big overshoot on arrival), HiHat reads as sharp
    // (fast, snaps past full size on the way in before settling).
    private readonly struct PulseProfile
    {
        public readonly float duration;
        public readonly float overshoot; // 0 = settle straight at full size; >0 = overshoot past it first

        public PulseProfile(float duration, float overshoot)
        {
            this.duration  = duration;
            this.overshoot = overshoot;
        }
    }

    private static PulseProfile GetProfile(RingType type) => type switch
    {
        RingType.Kick  => new PulseProfile(duration: 0.22f, overshoot: 0.45f),
        RingType.HiHat => new PulseProfile(duration: 0.10f, overshoot: 0.55f),
        RingType.Snare => new PulseProfile(duration: 0.15f, overshoot: 0.30f),
        RingType.Peak   => new PulseProfile(duration: 0.30f, overshoot: 0.80f),
        RingType.Impact => new PulseProfile(duration: 0.26f, overshoot: 0.65f),
        _               => new PulseProfile(duration: 0.14f, overshoot: 0.25f),
    };

    /// <summary>
    /// The largest scale factor this type's pulse punch can ever reach (strength=1, worst case)
    /// — kept as the generator's clearance reservation even though musical cubes no longer overshoot (Hit
    /// stops at 1.0): changing it would move every placement and the stored layouts (BonusLayoutProvider).
    /// Single source of truth so GameplayTimeline.CollectibleFloor can reserve enough surface
    /// clearance for the PEAK of the animation, not just the resting scale=1 size. Without this,
    /// a collectible placed using only its resting half-height could have the bottom of its
    /// scaled-up mesh dip below the surface for the instant its punch is at full overshoot.
    /// </summary>
    public static float MaxPulseScale(RingType type) => 1f + GetProfile(type).overshoot;

    // Punch already landed instantly (see Pulse) — this only eases the release back to 1.0.
    private IEnumerator SettleAnimation(float peak, PulseProfile profile)
    {
        float t = 0f;
        while (t < profile.duration)
        {
            t += Time.deltaTime;
            SetVisualScale(Mathf.Lerp(peak, 1f, Mathf.Clamp01(t / profile.duration)));
            yield return null;
        }

        SetVisualScale(1f);
        _pulseRoutine = null;
    }

    private void Update()
    {
        if (!_collected && !_missing)
            transform.Rotate(0f, 55f * Time.deltaTime, 0f);
    }
}
