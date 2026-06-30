using UnityEngine;

public class SnareDetector : IAudioDetector
{
    public bool IsEnabled => true;

    // Mid band (500–2000 Hz). Matches default band index 3 in AudioAnalysisConfig.
    private const int BandIndex    = 3;
    private const int SubBassIndex = 0;

    private float       _threshold;
    private float       _cooldown;
    private float       _lastTime;
    private float       _prevValue;
    private SongProfile _profile;

    public void Initialize(AudioAnalysisConfig config)
    {
        _threshold = config.snareThreshold;
        _cooldown  = config.snareCooldown;
    }

    public void SetSongProfile(SongProfile profile) => _profile = profile;

    public void Analyze(AudioData data)
    {
        if (data.timestamp - _lastTime < _cooldown) return;
        if (data.bands == null || data.bands.Length <= BandIndex) return;

        float current = data.bands[BandIndex].smoothedValue;
        float subBass = data.bands[SubBassIndex].smoothedValue;
        float delta   = current - _prevValue;
        _prevValue    = current;

        // Skip if sub-bass dominates — likely a kick, not a snare
        if (subBass > current * 2f) return;

        float threshold = _threshold;
        if (_profile != null)
        {
            float adapted = _profile.GetBandEnergyAt(BandIndex, data.timestamp) * 1.8f;
            if (adapted > threshold) threshold = adapted;
        }

        if (current > threshold && delta > 0f)
        {
            _lastTime = data.timestamp;
            EventBus.Publish(new SnareDetectedEvent { Intensity = current, Time = data.timestamp });
        }
    }
}
