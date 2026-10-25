using UnityEngine;

public class PeakDetector : IAudioDetector
{
    public bool IsEnabled => true;

    private float       _threshold;
    private float       _cooldown;
    private float       _lastTime = float.NegativeInfinity; // clip time of the last fire
    private SongProfile _profile;

    public void Initialize(AudioAnalysisConfig config)
    {
        _threshold = config.peakThreshold;
        _cooldown  = config.peakCooldown;
    }

    public void SetSongProfile(SongProfile profile) => _profile = profile;

    public void Reset() => _lastTime = float.NegativeInfinity;

    public void Analyze(AudioData data)
    {
        if (data.clipTime - _lastTime < _cooldown) return;

        float threshold = _threshold;
        if (_profile != null)
        {
            // SongProfile is indexed by CLIP time (AudioData.clipTime, from timeSamples).
            float adapted = _profile.GetEnergyAt(data.clipTime) * 1.5f;
            if (adapted > threshold) threshold = adapted;
        }

        if (data.smoothedEnergy > threshold)
        {
            _lastTime = data.clipTime;
            EventBus.Publish(new PeakDetectedEvent { Intensity = data.smoothedEnergy, Time = data.clipTime });
        }
    }
}
