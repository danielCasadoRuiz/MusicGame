using UnityEngine;

/// <summary>
/// Single authoritative source of musical time and path distance.
/// All gameplay systems (player, camera, spawner, checkpoints) read from here.
///
/// Time domains (see MusicTime for the conversions — never hand-write "± warmup"):
///   SongTime = warmupTime + clip time once the audio plays (TimelineEvent.eventTime domain)
///   ClipTime = SongTime − warmupTime (SongProfile domain; negative during a countdown from clip 0)
///   MusicDistance = SongTime * UnitsPerSecond
/// The player's canonical world position = path.GetSample(MusicDistance).position
///
/// The state machine and the time math live in MusicClockCore (pure, edit-mode tested); this
/// component only feeds it Time deltas + an AudioSource reading each frame, owns the AudioSource
/// side of seeking (StartPlayback), and publishes MusicTimeDiscontinuityEvent whenever the musical
/// timeline jumps (new run, playback start, seek, auto-resync) so stateful listeners (live
/// detectors, camera-kick dedup) reset instead of comparing against pre-jump state.
///
/// States (MusicClockState): Warmup → Playing → (Held ↔ Playing on fall-respawn) → Ended (natural
/// clip end, explicit evidence) or the range end → Farewell (BeginManualAdvance) → Stopped. Paused
/// overlays any of them. Outside Warmup/Playing/Farewell the clock is FROZEN — it never falls back
/// to wall-clock time while the audio isn't advancing.
///
/// AudioSource position only advances once per DSP buffer callback (a "staircase", not a smooth
/// ramp), which would show up as visible per-frame jitter in anything driven by MusicDistance.
/// SongTime is therefore a *filtered* value while Playing: it advances via Time.deltaTime every
/// frame and is gently pulled toward the raw audio reading (snap beyond MusicClockCore.SnapThreshold).
/// </summary>
public class MusicClock : MonoBehaviour
{
    public static MusicClock Instance { get; private set; }

    private readonly MusicClockCore _core = new();
    private AudioSource _source;

    public MusicClockState State          => _core.State;
    /// <summary>State ignoring a pause overlay.</summary>
    public MusicClockState EffectiveState => _core.EffectiveState;
    public float UnitsPerSecond           => _core.UnitsPerSecond;
    public float WarmupTime               => _core.WarmupTime;
    /// <summary>Clip position the run's playback starts from (manual play range start).</summary>
    public float StartClipTime            => _core.StartClipTime;
    /// <summary>SongTime at which the audio is due to start (warmup + StartClipTime).</summary>
    public float PlaybackStartSongTime    => _core.PlaybackStartSongTime;
    public float SongTime                 => _core.SongTime;
    /// <summary>Filtered clip time (SongTime − warmup) — the SongProfile lookup time. Only
    /// meaningful as audible music once HasPlaybackStarted; during warmup it is the countdown.</summary>
    public float ClipTime                 => _core.ClipTime;
    /// <summary>Raw clip position last read from AudioSource.timeSamples while playing.</summary>
    public float AudioClipTime            => (float)_core.AudioClipTime;
    public float MusicDistance            => _core.MusicDistance;
    public bool  IsRunning                => _core.IsRunning;
    public bool  IsPaused                 => _core.IsPaused;
    public bool  IsInWarmup               => _core.EffectiveState == MusicClockState.Warmup;
    /// <summary>True once the audio started in this run — false during the warmup countdown.</summary>
    public bool  HasPlaybackStarted       => _core.HasPlaybackStarted;
    /// <summary>The clip reached its natural end (explicit evidence, see MusicClockCore).</summary>
    public bool  NaturalEndDetected       => _core.NaturalEndDetected;
    /// <summary>Incremented on every timeline discontinuity — pollers compare it to detect jumps.</summary>
    public int   Generation               => _core.Generation;

    /// <summary>Diagnostics: last Playing tick's correction beyond deltaTime / whether it snapped.</summary>
    public float LastCorrection           => _core.LastCorrection;
    public bool  LastTickSnapped          => _core.LastTickSnapped;

    public float ToClipTime(float songTime) => MusicTime.SongToClip(songTime, _core.WarmupTime);
    public float ToSongTime(float clipTime) => MusicTime.ClipToSong(clipTime, _core.WarmupTime);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Call when a run begins (fresh start, restart, new song): enters Warmup with SongTime =
    /// startClipTime, counting up to warmupTime + startClipTime, where StartPlayback takes over. A
    /// mid-song start therefore never jumps: SongTime at the handoff already equals warmup + start.
    /// </summary>
    public void Initialize(AudioSource source, float warmupTime, float unitsPerSecond, float startClipTime = 0f)
    {
        _source = source;
        float from = _core.SongTime;
        _core.Initialize(warmupTime, unitsPerSecond, startClipTime);
        Publish(MusicTimeDiscontinuityReason.NewRun, from);
    }

    /// <summary>
    /// Seeks the AudioSource to clipTime, plays it and snaps SongTime there (warmup handoff, fall
    /// respawn, restart). The ONLY way gameplay should (re)start the music while the clock runs —
    /// so the clock always knows the jump was intentional and publishes it.
    /// </summary>
    public void StartPlayback(float clipTime, MusicTimeDiscontinuityReason reason)
    {
        if (_source == null) return;
        var    clip       = _source.clip;
        double clipLength = 0.0;
        if (clip != null)
        {
            clipLength = MusicTime.SamplesToSeconds(clip.samples, clip.frequency);
            _source.timeSamples = MusicTime.SecondsToSampleIndex(clipTime, clip.frequency, clip.samples);
        }
        else
        {
            _source.time = Mathf.Max(0f, clipTime);
        }
        _source.Play();

        float from = _core.SongTime;
        _core.BeginPlayback(MusicTime.ClampClipTime(clipTime, (float)clipLength), clipLength);
        // Read the real position back right away (zero delta) so SongTime is exact before the
        // next frame instead of gliding back into place.
        _core.Tick(0f, 0f, Read());
        Publish(reason, from);
    }

    /// <summary>Gameplay stops the audio on purpose (fall-respawn) — call right before pausing it.
    /// Freezes SongTime until the next StartPlayback. No-op during the warmup countdown.</summary>
    public bool Hold() => _core.Hold();

    /// <summary>
    /// Freezes SongTime/MusicDistance completely. Does NOT touch the AudioSource itself — the caller
    /// (PauseController) is responsible for pausing playback.
    /// </summary>
    public void Pause() => _core.Pause();

    /// <summary>Back to the interrupted state; Playing hard-resyncs to the audio on the next tick.</summary>
    public void Resume() => _core.Resume();

    /// <summary>Hard-resync to the AudioSource right now (e.g. after an external seek).</summary>
    public void ForceUpdate()
    {
        _core.ForceResync();
        _core.Tick(0f, 0f, Read());
    }

    /// <summary>
    /// Switches SongTime to advancing purely via Time.deltaTime from here on (state Farewell),
    /// ignoring the AudioSource entirely — continuing smoothly from the current SongTime. For
    /// GameplayManager's farewell stretch: the song has finished and the AudioSource is stopped on
    /// purpose. Only Initialize() (a genuinely new run) leaves it. Idempotent.
    /// </summary>
    public void BeginManualAdvance() => _core.BeginFarewell();

    public void Stop() => _core.Stop();

    /// <summary>
    /// True when playback has reached endClipTime: the raw clip position got there while Playing (a
    /// play range ending before the clip does), or the clip's natural end was detected. Never true
    /// just because the source is not playing (pause/hold/stop/transition).
    /// </summary>
    public bool ReachedPlaybackEnd(float endClipTime)
    {
        var s = _core.EffectiveState;
        if (s == MusicClockState.Ended) return true;
        return s == MusicClockState.Playing && _core.AudioClipTime >= endClipTime;
    }

    private void Update()
    {
        if (!_core.IsRunning || _core.IsPaused) return;
        var before = _core.State;
        float from = _core.SongTime;
        var reading = Read();
        bool resynced = _core.Tick(Time.deltaTime, Time.unscaledDeltaTime, reading);
        if (resynced) Publish(MusicTimeDiscontinuityReason.Resync, from);
        if (_core.State != before && (_core.State == MusicClockState.Stalled || _core.State == MusicClockState.Ended))
            Debug.Log($"[MusicClock] {before} → {_core.State} at songTime={_core.SongTime:F3} lastClip={_core.AudioClipTime:F3} " +
                      $"source timeSamples={reading.TimeSamples}/{reading.TotalSamples}");
    }

    private AudioPlaybackReading Read()
    {
        var reading = new AudioPlaybackReading();
        if (_source == null) return reading;
        reading.IsPlaying = _source.isPlaying;
        var clip = _source.clip;
        if (clip != null)
        {
            reading.HasClip      = true;
            reading.TimeSamples  = _source.timeSamples;
            reading.Frequency    = clip.frequency;
            reading.TotalSamples = clip.samples;
        }
        return reading;
    }

    private void Publish(MusicTimeDiscontinuityReason reason, float fromSongTime)
    {
        EventBus.Publish(new MusicTimeDiscontinuityEvent
        {
            Reason       = reason,
            FromSongTime = fromSongTime,
            ToSongTime   = _core.SongTime,
            ToClipTime   = _core.ClipTime,
            Generation   = _core.Generation,
        });
    }

    public static MusicClock GetOrCreate(GameObject host)
    {
        if (Instance != null) return Instance;
        return host.AddComponent<MusicClock>();
    }
}
