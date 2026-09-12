/// <summary>
/// Shared read-only context passed to every IOfflineAnalyzer.
/// Contains all FFT-derived per-frame data and song-level metadata.
/// Built by AudioPreAnalyzer after the main analysis loop.
/// </summary>
public class OfflineAnalysisContext
{
    public AudioAnalysisConfig Config;

    // ── Song metadata ─────────────────────────────────────────────────────────
    public int   SampleRate;
    public int   WindowSize;
    public float HopTime;
    public float Duration;
    public int   NumFrames;
    public float AvgEnergy;
    public float MaxEnergy;
    public float EstimatedBPM;

    // ── Core per-frame arrays (always present) ────────────────────────────────
    public float[]   EnergyEnvelope;   // RMS per frame
    public float[]   SpectralCentroid; // brightness Hz per frame
    public float[]   SpectralFlux;     // onset strength per frame
    public float[]   OnsetTimes;       // onset event times in seconds
    public float[][] BandEnvelopes;    // [band][frame] per-band energy

    // ── Spectrum-derived per-frame arrays (null if feature disabled) ──────────
    public float[]   SpectralFlatness; // 0=tonal, 1=noise
    public float[]   ChromaFlat;       // numFrames×12, chroma bins per frame
    public float[]   VoiceProb;        // 0..1 raw voice probability per frame

    // ── Convenience ───────────────────────────────────────────────────────────
    public float FrameTime(int f) => f * HopTime;
    public int   TimeToFrame(float t) => UnityEngine.Mathf.Clamp(
        UnityEngine.Mathf.FloorToInt(t / HopTime), 0, NumFrames - 1);
}
