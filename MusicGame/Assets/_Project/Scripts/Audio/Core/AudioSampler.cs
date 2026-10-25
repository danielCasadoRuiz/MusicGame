using System;
using UnityEngine;

public class AudioSampler : MonoBehaviour
{
    private AudioSource         _source;
    private AudioAnalysisConfig _config;
    private float[]             _spectrum;
    private float[]             _waveform;

    /// <summary>(spectrum, waveform, clipTime) — clipTime is the AudioClip position (s) captured from
    /// AudioSource.timeSamples in the same frame as the spectrum, i.e. the SongProfile domain.</summary>
    public event Action<float[], float[], double> OnSampleReady;

    public void Initialize(AudioSource source, AudioAnalysisConfig config)
    {
        _source   = source;
        _config   = config;
        int size  = Mathf.NextPowerOfTwo(config.spectrumSize);
        _spectrum = new float[size];
        _waveform = new float[size];
    }

    private void Update()
    {
        if (_source == null || !_source.isPlaying) return;
        var clip = _source.clip;
        if (clip == null) return;
        double clipTime = MusicTime.SamplesToSeconds(_source.timeSamples, clip.frequency);
        _source.GetSpectrumData(_spectrum, _config.audioChannel, _config.fftWindow);
        _source.GetOutputData(_waveform, _config.audioChannel);
        OnSampleReady?.Invoke(_spectrum, _waveform, clipTime);
    }
}
