using UnityEngine;

public class BeatDetector : IAudioDetector
{
    public bool IsEnabled => true;

    private float _threshold;
    private float _cooldown;
    private float _beatPeriod  = 0.5f; // default 120 BPM
    private float _nextBeatTime;
    private float _lastTime;

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

    public void Analyze(AudioData data)
    {
        if (data.timestamp - _lastTime < _cooldown) return;
        if (_nextBeatTime <= 0f) _nextBeatTime = data.timestamp + _beatPeriod;

        float tolerance = _beatPeriod * 0.15f;

        if (data.timestamp >= _nextBeatTime - tolerance && data.smoothedEnergy > _threshold)
        {
            _lastTime     = data.timestamp;
            _nextBeatTime = data.timestamp + _beatPeriod;
            float conf    = Mathf.Clamp01(data.smoothedEnergy / Mathf.Max(_threshold, 0.001f));
            EventBus.Publish(new BeatDetectedEvent { Confidence = conf, Time = data.timestamp });
        }
        else if (data.timestamp > _nextBeatTime + tolerance)
        {
            // Missed beat (silence/rest): advance clock without firing
            _nextBeatTime += _beatPeriod;
        }
    }
}
