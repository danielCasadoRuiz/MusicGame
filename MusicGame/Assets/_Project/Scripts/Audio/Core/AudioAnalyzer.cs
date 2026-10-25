using System;
using UnityEngine;

public class AudioAnalyzer : MonoBehaviour
{
    private AudioAnalysisConfig _config;
    private AudioSampler        _sampler;
    private IAudioDetector[]    _detectors;
    private AudioData           _data;
    private float[]             _smoothSpectrum;
    private float               _smoothEnergy;
    // After a reset the smoothing restarts from the first new frame instead of easing in from
    // pre-jump (or zeroed) values — easing from 0 would look like a rising attack to the detectors.
    private bool                _primeSmoothing = true;

    private readonly ClipTimeContinuity _continuity = new();
    private int _lastSampleFrame = -1;

    private Action<MusicTimeDiscontinuityEvent> _onDiscontinuity;
    private Action<PlayerRespawnedEvent>        _onRespawn;
    private Action<GameStartedEvent>            _onGameStarted;

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

        // Every way the music can jump resets detector state: MusicClock's own discontinuities
        // (new run / restart / song start / seek / resync), plus respawn and game start for good
        // measure (cheap, idempotent). Clip-time jumps nobody announced are caught in Process.
        _onDiscontinuity = e => ResetState($"clock {e.Reason}");
        _onRespawn       = _ => ResetState("respawn");
        _onGameStarted   = _ => ResetState("game start");
        EventBus.Subscribe(_onDiscontinuity);
        EventBus.Subscribe(_onRespawn);
        EventBus.Subscribe(_onGameStarted);
    }

    private void OnDestroy()
    {
        if (_sampler != null) _sampler.OnSampleReady -= Process;
        if (_onDiscontinuity != null) EventBus.Unsubscribe(_onDiscontinuity);
        if (_onRespawn       != null) EventBus.Unsubscribe(_onRespawn);
        if (_onGameStarted   != null) EventBus.Unsubscribe(_onGameStarted);
    }

    /// <summary>Resets every detector plus this analyzer's own smoothing/continuity state.</summary>
    public void ResetState(string reason)
    {
        if (_detectors != null)
            foreach (var d in _detectors) d.Reset();
        _continuity.Reset();
        _primeSmoothing  = true;
        _lastSampleFrame = -1;
        if (_config != null && _config.enableDebugLog)
            Debug.Log($"[AudioAnalyzer] detector state reset ({reason})");
    }

    private void Process(float[] spectrum, float[] waveform, double clipTime)
    {
        bool consecutive = _lastSampleFrame >= 0 && Time.frameCount == _lastSampleFrame + 1;
        _lastSampleFrame = Time.frameCount;
        if (_continuity.Observe(clipTime, consecutive, Time.unscaledDeltaTime))
        {
            ResetState($"clip-time jump to {clipTime:F3}s");
            _continuity.Observe(clipTime, false, 0.0); // re-anchor at the new position
            _lastSampleFrame = Time.frameCount;
        }

        float rms = 0f;
        for (int i = 0; i < waveform.Length; i++) rms += waveform[i] * waveform[i];
        float energy = Mathf.Sqrt(rms / waveform.Length);

        if (_primeSmoothing)
        {
            Array.Copy(spectrum, _smoothSpectrum, _smoothSpectrum.Length);
            _smoothEnergy   = energy;
            _primeSmoothing = false;
        }
        else
        {
            float alpha = 1f - _config.bandSmoothing;
            for (int i = 0; i < _smoothSpectrum.Length; i++)
                _smoothSpectrum[i] = Mathf.Lerp(_smoothSpectrum[i], spectrum[i], alpha);
            _smoothEnergy = Mathf.Lerp(_smoothEnergy, energy, 1f - _config.energySmoothing);
        }

        Array.Copy(waveform, _data.waveform, waveform.Length);
        _data.energy         = energy;
        _data.smoothedEnergy = _smoothEnergy;
        _data.bands          = ComputeBands(_smoothSpectrum);
        _data.deltaTime      = Time.deltaTime;
        _data.clipTime       = (float)clipTime;

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
