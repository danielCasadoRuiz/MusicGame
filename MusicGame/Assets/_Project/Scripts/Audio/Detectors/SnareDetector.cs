using UnityEngine;

public class SnareDetector : IAudioDetector
{
    public bool IsEnabled => true;

    // Mid band (500–2000 Hz). Matches default band index 3 in AudioAnalysisConfig.
    private const int BandIndex    = 3;
    private const int SubBassIndex = 0;

    private float       _threshold;
    private float       _cooldown;
    private float       _lastTime = float.NegativeInfinity; // clip time of the last fire
    private float       _prevValue;
    private bool        _hasPrev;                           // false right after a Reset
    private SongProfile _profile;

    public void Initialize(AudioAnalysisConfig config)
    {
        _threshold = config.snareThreshold;
        _cooldown  = config.snareCooldown;
    }

    public void SetSongProfile(SongProfile profile) => _profile = profile;

    public void Reset()
    {
        _lastTime  = float.NegativeInfinity;
        _prevValue = 0f;
        _hasPrev   = false;
    }

    public void Analyze(AudioData data)
    {
        if (data.bands == null || data.bands.Length <= BandIndex) return;

        // History is updated EVERY frame (cooldown included) — see KickDetector.
        float current = data.bands[BandIndex].smoothedValue;
        float subBass = data.bands[SubBassIndex].smoothedValue;
        float delta   = current - _prevValue;
        bool  hadPrev = _hasPrev;
        _prevValue    = current;
        _hasPrev      = true;
        if (!hadPrev) return;                                 // first frame after a reset: no delta yet

        if (data.clipTime - _lastTime < _cooldown) return;

        // Skip if sub-bass dominates — likely a kick, not a snare
        if (subBass > current * 2f) return;

        float threshold = _threshold;
        if (_profile != null)
        {
            // SongProfile is indexed by CLIP time (AudioData.clipTime, from timeSamples).
            float adapted = _profile.GetBandEnergyAt(BandIndex, data.clipTime) * 1.8f;
            if (adapted > threshold) threshold = adapted;
        }

        if (current > threshold && delta > 0f)
        {
            _lastTime = data.clipTime;
            EventBus.Publish(new SnareDetectedEvent { Intensity = current, Time = data.clipTime });
        }
    }
}
