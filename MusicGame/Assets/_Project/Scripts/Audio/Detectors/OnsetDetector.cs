using UnityEngine;

public class OnsetDetector : IAudioDetector
{
    public bool IsEnabled => true;

    private float       _threshold;
    private float       _cooldown;
    private float       _lastTime;
    private float[]     _prevSpectrum;
    private SongProfile _profile;

    public void Initialize(AudioAnalysisConfig config)
    {
        _threshold = config.onsetThreshold;
        _cooldown  = config.onsetCooldown;
    }

    public void SetSongProfile(SongProfile profile) => _profile = profile;

    public void Analyze(AudioData data)
    {
        if (_prevSpectrum == null)
        {
            _prevSpectrum = (float[])data.spectrum.Clone();
            return;
        }

        float flux = 0f;
        for (int i = 0; i < data.spectrum.Length; i++)
        {
            float d = data.spectrum[i] - _prevSpectrum[i];
            if (d > 0f) flux += d;
        }
        System.Array.Copy(data.spectrum, _prevSpectrum, data.spectrum.Length);

        float threshold = _threshold;
        if (_profile != null)
        {
            float adapted = _profile.GetEnergyAt(data.timestamp) * 2f;
            if (adapted > threshold) threshold = adapted;
        }

        if (flux > threshold && data.timestamp - _lastTime > _cooldown)
        {
            _lastTime = data.timestamp;
            EventBus.Publish(new OnsetDetectedEvent { Strength = flux, Time = data.timestamp });
        }
    }
}
