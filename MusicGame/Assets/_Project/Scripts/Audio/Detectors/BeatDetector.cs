using UnityEngine;

/// <summary>
/// Expects a beat every 60/estimatedBPM seconds of CLIP time and fires when energy is high near
/// that expectation. Note: the phase is anchored at the first frame it sees (after a reset), not to
/// the music's real beat phase — a heuristic for the debug overlay, not a beat tracker.
/// </summary>
public class BeatDetector : IAudioDetector
{
    public bool IsEnabled => true;

    private float _threshold;
    private float _cooldown;
    private float _beatPeriod  = 0.5f; // default 120 BPM
    private float _nextBeatTime;
    private bool  _hasPhase;           // false right after a Reset
    private float _lastTime = float.NegativeInfinity;

    public void Initialize(AudioAnalysisConfig config)
    {
        _threshold  = config.beatThreshold;
        _cooldown   = config.beatCooldown;
    }

    public void SetSongProfile(SongProfile profile)
    {
        if (profile.estimatedBPM > 0f)
            _beatPeriod = 60f / profile.estimatedBPM;
    }

    public void Reset()
    {
        _hasPhase = false;
        _lastTime = float.NegativeInfinity;
    }

    public void Analyze(AudioData data)
    {
        float t = data.clipTime;
        if (!_hasPhase)
        {
            _nextBeatTime = t + _beatPeriod;
            _hasPhase     = true;
            return;
        }

        float tolerance = _beatPeriod * 0.15f;
        bool  canFire   = t - _lastTime >= _cooldown;

        if (canFire && t >= _nextBeatTime - tolerance && data.smoothedEnergy > _threshold)
        {
            _lastTime     = t;
            _nextBeatTime = t + _beatPeriod;
            float conf    = Mathf.Clamp01(data.smoothedEnergy / Mathf.Max(_threshold, 0.001f));
            EventBus.Publish(new BeatDetectedEvent { Confidence = conf, Time = t });
        }
        else
        {
            // Missed beat(s) (silence/rest, or cooldown): advance the expectation without firing —
            // also during the cooldown, so the phase never goes stale.
            while (t > _nextBeatTime + tolerance) _nextBeatTime += _beatPeriod;
        }
    }
}
