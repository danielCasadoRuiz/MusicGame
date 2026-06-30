using UnityEngine;

public class AudioAnalyzer : MonoBehaviour
{
    private AudioAnalysisConfig _config;
    private AudioSampler        _sampler;
    private IAudioDetector[]    _detectors;
    private AudioData           _data;
    private float[]             _smoothSpectrum;
    private float               _smoothEnergy;

    public void Initialize(AudioSampler sampler, AudioAnalysisConfig config, IAudioDetector[] detectors)
    {
        _sampler     = sampler;
        _config      = config;
        _detectors   = detectors;

        int size        = Mathf.NextPowerOfTwo(config.spectrumSize);
        _smoothSpectrum = new float[size];
        _data           = new AudioData
        {
            spectrum = _smoothSpectrum,
            waveform = new float[size],
            bands    = new FrequencyBand[config.bands.Length],
        };

        _sampler.OnSampleReady += Process;
    }

    private void OnDestroy()
    {
        if (_sampler != null) _sampler.OnSampleReady -= Process;
    }

    private void Process(float[] spectrum, float[] waveform)
    {
        float alpha = 1f - _config.bandSmoothing;
        for (int i = 0; i < _smoothSpectrum.Length; i++)
            _smoothSpectrum[i] = Mathf.Lerp(_smoothSpectrum[i], spectrum[i], alpha);

        float rms = 0f;
        for (int i = 0; i < waveform.Length; i++) rms += waveform[i] * waveform[i];
        float energy = Mathf.Sqrt(rms / waveform.Length);
        _smoothEnergy = Mathf.Lerp(_smoothEnergy, energy, 1f - _config.energySmoothing);

        System.Array.Copy(waveform, _data.waveform, waveform.Length);
        _data.energy        = energy;
        _data.smoothedEnergy = _smoothEnergy;
        _data.bands         = ComputeBands(_smoothSpectrum);
        _data.deltaTime     = Time.deltaTime;
        _data.timestamp     = Time.time;

        EventBus.Publish(new AudioDataReadyEvent { Data = _data });

        foreach (var detector in _detectors)
            if (detector.IsEnabled)
                detector.Analyze(_data);
    }

    private FrequencyBand[] ComputeBands(float[] spectrum)
    {
        int   sampleRate = AudioSettings.outputSampleRate;
        float nyquist    = sampleRate * 0.5f;
        var   bands      = new FrequencyBand[_config.bands.Length];

        for (int b = 0; b < _config.bands.Length; b++)
        {
            var bc  = _config.bands[b];
            int lo  = Mathf.Clamp(Mathf.RoundToInt(bc.minHz * spectrum.Length / nyquist), 0, spectrum.Length - 1);
            int hi  = Mathf.Clamp(Mathf.RoundToInt(bc.maxHz * spectrum.Length / nyquist), 0, spectrum.Length - 1);
            float sum = 0f;
            for (int i = lo; i <= hi; i++) sum += spectrum[i];

            bands[b] = new FrequencyBand
            {
                label         = bc.label,
                minHz         = bc.minHz,
                maxHz         = bc.maxHz,
                rawValue      = hi >= lo ? sum / (hi - lo + 1) : 0f,
                smoothedValue = hi >= lo ? sum / (hi - lo + 1) : 0f,
            };
        }
        return bands;
    }
}
