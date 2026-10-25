using UnityEngine;

public class OnsetDetector : IAudioDetector
{
    public bool IsEnabled => true;

    private float       _threshold;
    private float       _cooldown;
    private float       _lastTime = float.NegativeInfinity; // clip time of the last fire
    private float[]     _prevSpectrum;                      // null right after a Reset
    private SongProfile _profile;

    public void Initialize(AudioAnalysisConfig config)
    {
        _threshold = config.onsetThreshold;
        _cooldown  = config.onsetCooldown;
    }

    public void SetSongProfile(SongProfile profile) => _profile = profile;

    public void Reset()
    {
        _lastTime     = float.NegativeInfinity;
        _prevSpectrum = null;
    }

    public void Analyze(AudioData data)
    {
        if (_prevSpectrum == null || _prevSpectrum.Length != data.spectrum.Length)
        {
            _prevSpectrum = (float[])data.spectrum.Clone();
            return;
        }

        // The previous spectrum is updated every frame (cooldown included); the cooldown only
        // gates firing.
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
            // SongProfile is indexed by CLIP time (AudioData.clipTime, from timeSamples).
            float adapted = _profile.GetEnergyAt(data.clipTime) * 2f;
            if (adapted > threshold) threshold = adapted;
        }

        if (flux > threshold && data.clipTime - _lastTime > _cooldown)
        {
            _lastTime = data.clipTime;
            EventBus.Publish(new OnsetDetectedEvent { Strength = flux, Time = data.clipTime });
        }
    }
}
