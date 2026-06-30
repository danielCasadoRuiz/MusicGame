using UnityEngine;

public class AudioContextProvider
{
    private SongProfile _profile;
    private AudioSource _source;

    public void Initialize(AudioSource source)
    {
        _source = source;
        EventBus.Subscribe<SongProfileReadyEvent>(OnProfileReady);
    }

    private void OnProfileReady(SongProfileReadyEvent e) => _profile = e.Profile;

    public AudioContext GetCurrent()
    {
        if (_profile == null || _source == null) return default;

        float t = _source.time;
        return new AudioContext
        {
            currentSegment     = _profile.GetSegmentAt(t),
            normalizedEnergy   = _profile.maxEnergy > 0f ? _profile.GetEnergyAt(t) / _profile.maxEnergy : 0f,
            normalizedPosition = _profile.duration  > 0f ? t / _profile.duration : 0f,
            estimatedBPM       = _profile.estimatedBPM,
            songTime           = t,
            songDuration       = _profile.duration,
            isValid            = true,
        };
    }

    public void Dispose() => EventBus.Unsubscribe<SongProfileReadyEvent>(OnProfileReady);
}
