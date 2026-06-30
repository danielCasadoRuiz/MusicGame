using UnityEngine;

public class KickDetector : IAudioDetector
{
    public bool IsEnabled => true;

    // Sub Bass band (20–60 Hz). Matches default band index 0 in AudioAnalysisConfig.
    private const int BandIndex = 0;

    private float       _threshold;
    private float       _cooldown;
    private float       _lastTime;
    private float       _prevValue;
    private SongProfile _profile;

    public void Initialize(AudioAnalysisConfig config)
    {
        _threshold = config.kickThreshold;
        _cooldown  = config.kickCooldown;
    }

    public void SetSongProfile(SongProfile profile) => _profile = profile;

    public void Analyze(AudioData data)
    {
        if (data.timestamp - _lastTime < _cooldown) return;
        if (data.bands == null || data.bands.Length <= BandIndex) return;

        float current = data.bands[BandIndex].smoothedValue;
        float delta   = current - _prevValue;
        _prevValue    = current;

        float threshold = _threshold;
        if (_profile != null)
        {
            float adapted = _profile.GetBandEnergyAt(BandIndex, data.timestamp) * 1.8f;
            if (adapted > threshold) threshold = adapted;
        }

        if (current > threshold && delta > 0f)
        {
            _lastTime = data.timestamp;
            EventBus.Publish(new KickDetectedEvent { Intensity = current, Time = data.timestamp });
        }
    }
}
