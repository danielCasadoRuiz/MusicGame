using UnityEngine;

/// <summary>What MusicClock is doing right now — see MusicClockCore's doc for each transition.</summary>
public enum MusicClockState
{
    Stopped,   // not initialised yet, or the run is over (Stop) — SongTime frozen
    Warmup,    // countdown before the audio starts: advances with game time, capped at the playback start
    Playing,   // audio-driven: SongTime = warmup + clip position (filtered)
    Held,      // audio deliberately stopped by gameplay (fall-respawn) — SongTime frozen, never wall clock
    Stalled,   // audio stopped with no explanation and no end evidence — frozen; resyncs when it plays again
    Paused,    // pause menu — frozen; Resume returns to the state it interrupted
    Ended,     // the clip reached its natural end (explicit evidence) — frozen at the clip end
    Farewell,  // manual advance after the song finished (BeginManualAdvance) — Time.deltaTime driven
}

/// <summary>Why the musical timeline jumped (see MusicTimeDiscontinuityEvent).</summary>
public enum MusicTimeDiscontinuityReason
{
    NewRun,           // Initialize: fresh run, restart or new song — every musical state is stale
    PlaybackStarted,  // audio started (warmup → playing): clip time begins at the start offset
    Seek,             // explicit seek while running (fall-respawn)
    Resync,           // audio resumed by itself after a stall — position may have moved
}

/// <summary>One frame's reading of the AudioSource, captured by MusicClock (or faked by tests).</summary>
public struct AudioPlaybackReading
{
    public bool HasClip;
    public bool IsPlaying;
    public int  TimeSamples;
    public int  Frequency;
    public int  TotalSamples;

    public double ClipTime   => MusicTime.SamplesToSeconds(TimeSamples, Frequency);
    public double ClipLength => MusicTime.SamplesToSeconds(TotalSamples, Frequency);
}

/// <summary>
/// The pure state machine + time math behind MusicClock (no MonoBehaviour, no Time.*, no
/// AudioSource): the caller feeds it frame deltas and an AudioPlaybackReading, so edit-mode tests
/// drive every transition deterministically.
///
/// SongTime per state:
///   Warmup   — startClipTime + elapsed game time, CAPPED at PlaybackStartSongTime (warmup + start).
///              A clip still loading after the countdown therefore holds the clock at the handoff
///              point instead of overrunning it and snapping back once the audio starts.
///   Playing  — warmup + clip position, filtered: AudioSource position only moves once per DSP
///              buffer (a staircase), so SongTime advances by the frame delta and is pulled toward
///              the raw reading (snap beyond SnapThreshold, ease below it).
///   Held / Stalled / Paused / Ended / Stopped — frozen. The clock NEVER falls back to wall-clock
///              time while the audio isn't advancing (the old fall-respawn forward jump).
///   Farewell — += frame delta (the song is over, the audio is stopped on purpose).
///
/// Natural end ("!isPlaying" alone is NOT enough — it is also pause, stop, error or a transition):
/// in Playing, after the source was observed playing since the last BeginPlayback, the first frame
/// it reports not playing counts as the clip's natural end only if the last position observed WHILE
/// playing plus the real time since that observation reaches the clip length (within
/// NaturalEndTolerance). The stopped source's own position is not used (it varies — see
/// IsNaturalEnd). Anything else is Stalled. Pause/Hold/
/// seek all leave Playing first, so they can never be mistaken for an end.
/// </summary>
public sealed class MusicClockCore
{
    // Beyond this many seconds of error, snap instead of easing (seek/hitch/first tick).
    public const float SnapThreshold       = 0.15f;
    // Fraction of the remaining error corrected per second for small drift.
    public const float DriftCorrectionRate = 10f;
    // How close (s) the extrapolated position must be to the clip length to accept a natural end.
    public const float NaturalEndTolerance = 0.1f;

    public MusicClockState State          { get; private set; } = MusicClockState.Stopped;
    public float WarmupTime               { get; private set; }
    public float StartClipTime            { get; private set; }
    public float UnitsPerSecond           { get; private set; }
    public float SongTime                 { get; private set; }
    /// <summary>Last raw clip position read from the AudioSource while it was playing (unfiltered).</summary>
    public double AudioClipTime           { get; private set; }
    /// <summary>True once audio playback started in this run (BeginPlayback since Initialize).</summary>
    public bool  HasPlaybackStarted       { get; private set; }
    /// <summary>True once the clip's natural end was detected (state Ended, or later Farewell/Stopped).</summary>
    public bool  NaturalEndDetected       { get; private set; }
    /// <summary>Incremented on every discontinuity (Initialize, BeginPlayback, auto-resync).</summary>
    public int   Generation               { get; private set; }

    // Diagnostics only (late-pulse investigation) — never read by game logic.
    /// <summary>Last Playing tick: correction applied on top of the frame's deltaTime (raw audio position −
    /// prediction, eased or snapped), in seconds.</summary>
    public float LastCorrection           { get; private set; }
    /// <summary>Last Playing tick hard-snapped to the audio position (error beyond SnapThreshold, or a resync).</summary>
    public bool  LastTickSnapped          { get; private set; }

    public float ClipTime              => MusicTime.SongToClip(SongTime, WarmupTime);
    public float MusicDistance         => MusicTime.SongToDistance(SongTime, UnitsPerSecond);
    /// <summary>SongTime at which the audio is due to start (end of the warmup countdown).</summary>
    public float PlaybackStartSongTime => MusicTime.ClipToSong(StartClipTime, WarmupTime);
    public bool  IsRunning             => State != MusicClockState.Stopped;
    public bool  IsPaused              => State == MusicClockState.Paused;
    /// <summary>State ignoring a pause overlay (what the clock will return to on Resume).</summary>
    public MusicClockState EffectiveState => State == MusicClockState.Paused ? _stateBeforePause : State;

    private MusicClockState _stateBeforePause;
    private bool   _snapNext;          // next audio reading hard-snaps (BeginPlayback/Resume/ForceResync)
    private bool   _observedPlaying;   // the source was seen playing since the last BeginPlayback
    private double _clipLength;        // of the clip being played (0 = unknown)

    /// <summary>Fresh run: warmup countdown from startClipTime up to warmup + startClipTime.</summary>
    public void Initialize(float warmupTime, float unitsPerSecond, float startClipTime)
    {
        WarmupTime         = Mathf.Max(0f, warmupTime);
        UnitsPerSecond     = unitsPerSecond;
        StartClipTime      = Mathf.Max(0f, startClipTime);
        SongTime           = StartClipTime;
        AudioClipTime      = StartClipTime;
        State              = MusicClockState.Warmup;
        HasPlaybackStarted = false;
        NaturalEndDetected = false;
        _snapNext          = false;
        _observedPlaying   = false;
        _clipLength        = 0.0;
        Generation++;
    }

    /// <summary>The audio was (re)started at clipTime — warmup handoff, seek or respawn. SongTime
    /// jumps to warmup + clipTime NOW; the next playing reading hard-snaps to the real position.</summary>
    public void BeginPlayback(double clipTime, double clipLength)
    {
        if (State == MusicClockState.Stopped) return;
        SongTime           = MusicTime.ClipToSong((float)clipTime, WarmupTime);
        AudioClipTime      = clipTime;
        _clipLength        = clipLength;
        _snapNext          = true;
        _observedPlaying   = false;
        HasPlaybackStarted = true;
        NaturalEndDetected = false;
        if (State == MusicClockState.Paused) _stateBeforePause = MusicClockState.Playing;
        else                                 State = MusicClockState.Playing;
        Generation++;
    }

    /// <summary>Gameplay is about to stop the audio on purpose (fall-respawn): freeze SongTime until
    /// the next BeginPlayback. Returns false (no-op) outside an audio-driven state — e.g. a fall
    /// during the warmup countdown keeps counting down.</summary>
    public bool Hold()
    {
        if (State == MusicClockState.Paused)
        {
            if (_stateBeforePause != MusicClockState.Playing && _stateBeforePause != MusicClockState.Stalled) return false;
            _stateBeforePause = MusicClockState.Held;
            return true;
        }
        if (State != MusicClockState.Playing && State != MusicClockState.Stalled) return false;
        State = MusicClockState.Held;
        return true;
    }

    public void Pause()
    {
        if (State == MusicClockState.Stopped || State == MusicClockState.Paused) return;
        _stateBeforePause = State;
        State             = MusicClockState.Paused;
    }

    /// <summary>Back to the interrupted state; an audio-driven one hard-resyncs on the next reading
    /// instead of easing in from the frozen value.</summary>
    public void Resume()
    {
        if (State != MusicClockState.Paused) return;
        State = _stateBeforePause;
        if (State == MusicClockState.Playing) _snapNext = true;
    }

    /// <summary>Hard-resync on the next playing reading (legacy ForceUpdate semantics).</summary>
    public void ForceResync() => _snapNext = true;

    /// <summary>Farewell: from here on SongTime advances by frame delta, continuing from its current
    /// value with no discontinuity. Idempotent.</summary>
    public void BeginFarewell()
    {
        if (State == MusicClockState.Stopped) return;
        if (State == MusicClockState.Paused) _stateBeforePause = MusicClockState.Farewell;
        else                                 State = MusicClockState.Farewell;
    }

    public void Stop() => State = MusicClockState.Stopped;

    /// <summary>
    /// Advances one frame. deltaTime = scaled game delta (warmup/farewell/filter), unscaledDeltaTime
    /// = real delta (audio keeps its own real-time pace — used only to extrapolate the last observed
    /// position for the natural-end test). Returns true when an auto-resync discontinuity happened
    /// (Stalled → Playing), which the caller publishes.
    /// </summary>
    public bool Tick(float deltaTime, float unscaledDeltaTime, in AudioPlaybackReading audio)
    {
        switch (State)
        {
            case MusicClockState.Warmup:
                SongTime = Mathf.Min(SongTime + deltaTime, PlaybackStartSongTime);
                return false;

            case MusicClockState.Farewell:
                SongTime += deltaTime;
                return false;

            case MusicClockState.Stalled:
                if (!audio.IsPlaying) return false;
                // The source plays again by itself (focus/device recovery, external UnPause): adopt
                // its position as the new truth and report the jump.
                AudioClipTime    = audio.ClipTime;
                SongTime         = MusicTime.ClipToSong((float)AudioClipTime, WarmupTime);
                if (audio.HasClip) _clipLength = audio.ClipLength;
                _observedPlaying = true;
                _snapNext        = false;
                State            = MusicClockState.Playing;
                Generation++;
                return true;

            case MusicClockState.Playing:
                TickPlaying(deltaTime, unscaledDeltaTime, audio);
                return false;

            default: // Stopped, Held, Paused, Ended — frozen
                return false;
        }
    }

    private void TickPlaying(float deltaTime, float unscaledDeltaTime, in AudioPlaybackReading audio)
    {
        if (audio.IsPlaying)
        {
            _observedPlaying = true;
            AudioClipTime    = audio.ClipTime;
            if (audio.HasClip) _clipLength = audio.ClipLength;

            float raw = MusicTime.ClipToSong((float)AudioClipTime, WarmupTime);
            if (_snapNext)
            {
                LastCorrection  = raw - SongTime - deltaTime;
                LastTickSnapped = true;
                SongTime  = raw;
                _snapNext = false;
                return;
            }

            float predicted = SongTime + deltaTime;
            float error     = raw - predicted;
            LastTickSnapped = Mathf.Abs(error) > SnapThreshold;
            LastCorrection  = LastTickSnapped ? error : error * Mathf.Clamp01(DriftCorrectionRate * deltaTime);
            SongTime = predicted + LastCorrection;
            return;
        }

        // Not playing while we believe we are. Before the source was ever seen playing (the frame
        // of Play()), keep waiting — frozen, never wall clock.
        if (!_observedPlaying) return;

        if (IsNaturalEnd(AudioClipTime, unscaledDeltaTime, _clipLength, audio))
        {
            NaturalEndDetected = true;
            AudioClipTime      = _clipLength;
            SongTime           = Mathf.Max(SongTime, MusicTime.ClipToSong((float)_clipLength, WarmupTime));
            State              = MusicClockState.Ended;
        }
        else
        {
            State = MusicClockState.Stalled;
        }
    }

    /// <summary>The explicit natural-end evidence (see class doc). Pure — exposed for tests.</summary>
    public static bool IsNaturalEnd(double lastObservedClipTime, double secondsSinceObservation,
                                    double clipLength, in AudioPlaybackReading now)
    {
        if (clipLength <= 0.0 || now.IsPlaying || !now.HasClip) return false;
        // The stopped source's own position is NOT evidence: after a natural end the play-mode probe
        // saw it report 0, the clip length, AND the position playback was last started from, on
        // different runs. The evidence is where the audio WAS: its last observed position (taken
        // while playing, so never a pause/hold/seek — those leave Playing first) plus the real time
        // since that observation reached the clip's end.
        double extrapolated = lastObservedClipTime + System.Math.Max(0.0, secondsSinceObservation);
        return extrapolated >= clipLength - NaturalEndTolerance;
    }
}
