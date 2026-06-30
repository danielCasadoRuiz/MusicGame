using System;
using UnityEngine;

public class AudioSampler : MonoBehaviour
{
    private AudioSource         _source;
    private AudioAnalysisConfig _config;
    private float[]             _spectrum;
    private float[]             _waveform;

    public event Action<float[], float[]> OnSampleReady;

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
        _source.GetSpectrumData(_spectrum, _config.audioChannel, _config.fftWindow);
        _source.GetOutputData(_waveform, _config.audioChannel);
        OnSampleReady?.Invoke(_spectrum, _waveform);
    }
}
