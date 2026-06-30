using UnityEngine;

[CreateAssetMenu(fileName = "AudioAnalysisConfig", menuName = "MusicGame/Audio Analysis Config")]
public class AudioAnalysisConfig : ScriptableObject
{
    [Header("FFT")]
    public int       spectrumSize  = 1024;
    public FFTWindow fftWindow     = FFTWindow.BlackmanHarris;
    public int       audioChannel  = 0;

    [Header("Smoothing")]
    [Range(0f, 0.99f)] public float energySmoothing = 0.15f;
    [Range(0f, 0.99f)] public float bandSmoothing   = 0.20f;

    [Header("Frequency Bands")]
    public FrequencyBandConfig[] bands = new FrequencyBandConfig[]
    {
        new FrequencyBandConfig("Sub Bass",    20f,    60f),
        new FrequencyBandConfig("Bass",        60f,   250f),
        new FrequencyBandConfig("Low Mid",    250f,   500f),
        new FrequencyBandConfig("Mid",        500f,  2000f),
        new FrequencyBandConfig("High Mid",  2000f,  4000f),
        new FrequencyBandConfig("Treble",    4000f, 20000f),
    };

    [Header("Active Detectors")]
    public bool enableEnergy  = true;
    public bool enableBands   = true;
    public bool enablePeaks   = true;
    public bool enableOnsets  = true;
    public bool enableBeat    = true;
    public bool enableKick    = true;
    public bool enableSnare   = true;
    public bool enableHiHat   = true;

    [Header("Thresholds")]
    [Range(0f, 1f)] public float energyThreshold = 0.05f;
    [Range(0f, 1f)] public float peakThreshold   = 0.15f;
    [Range(0f, 1f)] public float onsetThreshold  = 0.10f;
    [Range(0f, 1f)] public float beatThreshold   = 0.10f;
    [Range(0f, 1f)] public float kickThreshold   = 0.12f;
    [Range(0f, 1f)] public float snareThreshold  = 0.08f;
    [Range(0f, 1f)] public float hiHatThreshold  = 0.04f;

    [Header("Cooldowns (seconds)")]
    public float peakCooldown  = 0.10f;
    public float onsetCooldown = 0.05f;
    public float beatCooldown  = 0.20f;
    public float kickCooldown  = 0.10f;
    public float snareCooldown = 0.10f;
    public float hiHatCooldown = 0.04f;

    [Header("Debug")]
    public bool enableDebugLog = false;

    [Header("Visualizer")]
    public bool  enableVisualizer      = true;
    public int   visualizerBarCount    = 32;
    public int   visualizerLEDCount    = 20;
    public float visualizerHeight      = 180f;
    [Range(0f, 0.99f)]
    public float visualizerSmoothing   = 0.35f;
    public float visualizerSensitivity = 150f;
    public float visualizerPeakHold    = 1.2f;
    public float visualizerPeakFall    = 1.5f;
    public bool  visualizerLogScale    = true;
}

[System.Serializable]
public class FrequencyBandConfig
{
    public string label;
    public float  minHz;
    public float  maxHz;

    public FrequencyBandConfig() { }

    public FrequencyBandConfig(string label, float minHz, float maxHz)
    {
        this.label = label;
        this.minHz = minHz;
        this.maxHz = maxHz;
    }
}
