using UnityEngine;

/// <summary>
/// Single authoritative source of musical time and path distance.
/// All gameplay systems (player, camera, spawner, checkpoints) read from here.
///
/// Raw SongTime formula:
///   During warmup (audio not playing): Time.time - wallStart
///   During playback:                   warmupTime + audioSource.time
///
/// AudioSource.time only advances once per DSP buffer callback (a "staircase", not a
/// smooth ramp), which would show up as visible per-frame jitter in anything driven by
/// MusicDistance (player position, camera). SongTime is therefore a *filtered* value:
/// it advances continuously via Time.deltaTime every frame and is gently pulled toward
/// the raw audio-clock reading each tick (complementary filter) — smooth moment to
/// moment, while never drifting far from the true audio position.
///
/// MusicDistance = SongTime * UnitsPerSecond
/// The player's canonical world position = path.GetSample(MusicDistance).position
///
/// A third mode, manual advance (see BeginManualAdvance), ignores the AudioSource entirely and
/// just accumulates Time.deltaTime — used for GameplayManager's farewell stretch, once the song
/// has genuinely finished and the AudioSource has already been stopped.
/// </summary>
public class MusicClock : MonoBehaviour
{
    public static MusicClock Instance { get; private set; }

    // Beyond this many seconds of error, snap instead of easing (seek/hitch/first tick).
    private const float SnapThreshold      = 0.15f;
    // Fraction of the remaining error corrected per second for small drift.
    private const float DriftCorrectionRate = 10f;

    private AudioSource _source;
    private float       _warmupTime;
    private float       _wallStart;
    private bool        _active;
    private bool        _audioPhase;   // true once we've crossed from warmup into audio-driven timing
    private float       _predicted;    // filtered SongTime
    // See BeginManualAdvance's own doc — once true, Tick() ignores _source entirely and just
    // accumulates Time.deltaTime, for the rest of this MusicClock's life (until the next
    // Initialize()).
    private bool        _manualAdvance;

    public float UnitsPerSecond { get; private set; }
    public float SongTime       { get; private set; }
    public float MusicDistance  => SongTime * UnitsPerSecond;
    public bool  IsRunning      { get; private set; }
    public bool  IsPaused       { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Call when a game session begins (wall-clock timer starts immediately).</summary>
    public void Initialize(AudioSource source, float warmupTime, float unitsPerSecond)
    {
        _source        = source;
        _warmupTime    = warmupTime;
        UnitsPerSecond = unitsPerSecond;
        _wallStart     = Time.time;
        _active        = true;
        IsRunning      = true;
        IsPaused       = false;
        _audioPhase    = false;
        _manualAdvance = false;
        _predicted     = 0f;
        Tick();
    }

    public void Stop()
    {
        _active   = false;
        IsRunning = false;
    }

    /// <summary>
    /// Call after seeking audioSource.time externally (e.g. checkpoint respawn) or right
    /// after the warmup→playback handoff. Forces an immediate hard snap (no easing) so
    /// SongTime is correct before the next frame instead of gliding back into place.
    /// </summary>
    public void ForceUpdate()
    {
        _audioPhase = false;
        Tick();
    }

    /// <summary>
    /// Freezes SongTime/MusicDistance completely. Does NOT touch the AudioSource itself —
    /// the caller (PauseController) is responsible for pausing playback. This just stops
    /// Tick() from running, so the wall-clock fallback branch never sees the paused
    /// AudioSource's isPlaying==false and snaps SongTime to a stale Time.time reading.
    /// </summary>
    public void Pause() => IsPaused = true;

    /// <summary>Resumes ticking and forces a clean resync (same as ForceUpdate) so the first
    /// tick after resume doesn't ease in from a frozen _predicted value.</summary>
    public void Resume()
    {
        IsPaused    = false;
        _audioPhase = false;
    }

    /// <summary>
    /// Switches SongTime to advancing purely via Time.deltaTime from here on, ignoring the
    /// AudioSource entirely — continuing smoothly from whatever SongTime already is, with no
    /// discontinuity. For GameplayManager's farewell stretch: the AudioSource is stopped the
    /// instant the song finishes (see GameplayManager's own ending sequence), and a played range
    /// ending at or near the clip's own natural end may have had no spare content to keep
    /// "playing" through anyway — rather than reactively patching the wall-clock fallback once
    /// that becomes a problem, this switches over UNCONDITIONALLY the moment farewell begins, so
    /// the player's forward motion never depends on how much of the clip happened to be left.
    /// Only Initialize() (a genuinely new run) turns this back off. Idempotent to call more than
    /// once.
    /// </summary>
    public void BeginManualAdvance() => _manualAdvance = true;

    private void Update()
    {
        if (!_active || IsPaused) return;
        Tick();
    }

    private void Tick()
    {
        if (_manualAdvance)
        {
            // Frame-smooth by construction — no filtering needed, same as the wall-clock branch
            // below.
            _predicted += Time.deltaTime;
            SongTime    = _predicted;
            return;
        }

        bool audioPlaying = _source != null && _source.isPlaying;
        float raw = audioPlaying
            ? _warmupTime + _source.time
            : Time.time - _wallStart;

        if (!audioPlaying)
        {
            // Wall-clock time is already frame-smooth — no filtering needed.
            _predicted  = raw;
            _audioPhase = false;
        }
        else if (!_audioPhase)
        {
            // First tick after crossing into audio time (or a forced resync): hard snap once.
            _predicted  = raw;
            _audioPhase = true;
        }
        else
        {
            _predicted += Time.deltaTime;
            float error = raw - _predicted;
            _predicted += Mathf.Abs(error) > SnapThreshold
                ? error
                : error * Mathf.Clamp01(DriftCorrectionRate * Time.deltaTime);
        }

        SongTime = _predicted;
    }

    public static MusicClock GetOrCreate(GameObject host)
    {
        if (Instance != null) return Instance;
        return host.AddComponent<MusicClock>();
    }
}
