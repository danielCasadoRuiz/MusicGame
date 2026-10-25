using UnityEngine;

public class KickDetector : IAudioDetector
{
    public bool IsEnabled => true;

    // Sub Bass band (20–60 Hz). Matches default band index 0 in AudioAnalysisConfig.
    private const int BandIndex = 0;

    private float       _threshold;
    private float       _cooldown;
    private float       _lastTime = float.NegativeInfinity; // clip time of the last fire
    private float       _prevValue;
    private bool        _hasPrev;                           // false right after a Reset
    private SongProfile _profile;

    public void Initialize(AudioAnalysisConfig config)
    {
        _threshold = config.kickThreshold;
        _cooldown  = config.kickCooldown;
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

        // History is updated EVERY frame (cooldown included) so the first frame after a cooldown
        // measures its delta against the previous frame, not against a stale pre-cooldown value.
        float current = data.bands[BandIndex].smoothedValue;
        float delta   = current - _prevValue;
        bool  hadPrev = _hasPrev;
        _prevValue    = current;
        _hasPrev      = true;
        if (!hadPrev) return;                                 // first frame after a reset: no delta yet

        if (data.clipTime - _lastTime < _cooldown) return;

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
            EventBus.Publish(new KickDetectedEvent { Intensity = current, Time = data.clipTime });
        }
    }
}
