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

    // ── Advanced Pre-Analysis ────────────────────────────────────────────────
    // These run once offline after the FFT loop and populate extra SongProfile fields.

    [Header("Advanced Pre-Analysis (master switch)")]
    public bool advancedEnabled = true;

    [Header("Advanced — Loudness / RMS dB")]
    public bool advancedLoudness = true;

    [Header("Advanced — Dynamics")]
    public bool  advancedDynamics    = true;
    [Range(0f, 0.3f)]  public float silenceThreshold  = 0.05f;  // fraction of avg energy
    [Range(0.05f, 0.5f)] public float breakThreshold  = 0.20f;
    [Range(0.5f, 1f)]  public float impactThreshold   = 0.75f;
    public float buildupMinLength = 3f;    // seconds
    public float crescendoWindow  = 2.5f;  // seconds

    [Header("Advanced — Timbre (flatness / aggressiveness)")]
    public bool advancedTimbre = true;

    [Header("Advanced — Structure (density / complexity / danceability)")]
    public bool advancedStructure = true;

    [Header("Advanced — Harmony (chroma / key / mode)")]
    public bool advancedHarmony = true;

    [Header("Advanced — Voice detection")]
    public bool  advancedVoice      = true;
    [Range(0f, 1f)] public float voiceThreshold = 0.30f;
    public float voiceMinDuration = 1.5f;  // seconds

    [Header("Advanced — Section similarity (expensive)")]
    public bool  advancedSimilarity       = false;  // disabled by default
    [Range(2f, 16f)] public float similaritySegmentSize = 4f;
    [Range(0.6f, 1f)] public float similarityThreshold  = 0.82f;

    [Header("Advanced — Semantic Music Tagging")]
    [Tooltip("Runs a local musicnn model (Sentis/ONNX, on-device, no network) once during pre-" +
             "analysis to get real genre/mood tags (funk, dance, happy, ...). Cached alongside " +
             "the rest of the song's analysis — see SongProfile.musicTagModelVersion.")]
    public bool  advancedSemanticTagging = true;
    [Tooltip("How many of the model's top-scoring tags to keep after thresholding.")]
    [Range(1, 20)] public int   semanticTagTopN    = 8;
    [Tooltip("Tags scoring below this are dropped even if they'd otherwise make the Top N.")]
    [Range(0f, 1f)] public float semanticTagMinScore = 0.05f;
    [Tooltip("Shows a verbose raw-tag breakdown in the F1 debug HUD (the compact STYLE/VIBE/" +
             "OTHER summary in the live top bar is controlled by advancedSemanticTagging alone).")]
    public bool  semanticTagDebugUI = false;

    [Header("Manual Play Range — LOCAL/uploaded songs only")]
    [Tooltip("Catalog/automatic songs will pick their own 'interesting chunk' algorithmically later " +
             "(not built yet) — this manual override only ever applies to a locally-picked file " +
             "(Song Selection's PLAY YOUR SONG), where there's no such algorithm to fall back on.")]
    public bool  useManualPlayRange        = false;
    [Tooltip("Seconds into the uploaded file where the played/analyzed window starts.")]
    public float manualPlayRangeStartSeconds = 0f;
    [Tooltip("Seconds into the uploaded file where the played/analyzed window ends. Clamped to the " +
             "file's actual length — a value of 0 (or beyond the file's length) means 'to the end'.")]
    public float manualPlayRangeEndSeconds   = 60f;

    [Header("Visual Spectrum (mesh cross-section + debug spectrum)")]
    [Tooltip("Independent from the 6 classification bands above (Kick/Snare/HiHat detection " +
             "keeps using those, unchanged). Log-spaced from 20 Hz to Nyquist, computed once " +
             "offline in the same FFT pass — this is what the ground mesh and the debug " +
             "spectrum panel both read, so they always show the exact same data.")]
    [Range(8, 64)] public int visualBandCount = 24;

    [Header("Visualizer")]
    public bool  enableVisualizer      = true;
    [Tooltip("When true, the bottom frequency-bar panel only shows while the song is actually " +
             "playing (AudioSource.isPlaying) — hidden before the song starts, while paused, " +
             "and after it ends, instead of staying on screen frozen at stale values.")]
    public bool  showFrequencyBarDuringGameplay = true;
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
