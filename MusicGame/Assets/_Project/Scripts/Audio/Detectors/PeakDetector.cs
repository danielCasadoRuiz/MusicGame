using UnityEngine;

public class PeakDetector : IAudioDetector
{
    public bool IsEnabled => true;

    private float       _threshold;
    private float       _cooldown;
    private float       _lastTime;
    private SongProfile _profile;

    public void Initialize(AudioAnalysisConfig config)
    {
        _threshold = config.peakThreshold;
        _cooldown  = config.peakCooldown;
    }

    public void SetSongProfile(SongProfile profile) => _profile = profile;

    public void Analyze(AudioData data)
    {
        if (data.timestamp - _lastTime < _cooldown) return;

        float threshold = _threshold;
        if (_profile != null)
        {
            float adapted = _profile.GetEnergyAt(data.timestamp) * 1.5f;
            if (adapted > threshold) threshold = adapted;
        }

        if (data.smoothedEnergy > threshold)
        {
            _lastTime = data.timestamp;
            EventBus.Publish(new PeakDetectedEvent { Intensity = data.smoothedEnergy, Time = data.timestamp });
        }
    }
}
