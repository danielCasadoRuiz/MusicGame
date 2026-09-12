using UnityEngine;

[System.Serializable]
public class SongProfile
{
    // ── Core (always populated) ───────────────────────────────────────────────
    public float    duration;
    public int      sampleRate;
    public float    estimatedBPM;
    public float    averageEnergy;
    public float    maxEnergy;
    public float    analysisHopTime;

    public float[]   energyEnvelope;
    public float[]   spectralCentroid;
    public float[]   spectralFlux;
    public float[]   onsetTimes;
    public float[][] bandEnvelopes;      // 6 classification bands (Kick/Snare/HiHat) — fixed layout, indices matter
    public float[][] visualBandEnvelopes; // higher-resolution, log-spaced — ground mesh + debug spectrum
    public SongSegment[] segments;

    // ── Per-frame spectrum-derived (cached; needs raw FFT to compute) ─────────
    public float[]   spectralFlatness;   // 0=tonal, 1=noise
    public float[]   chromaFlat;         // numFrames×12 chromagram
    public float[]   voiceProbability;   // 0..1 raw → smoothed by VoiceAnalyzer

    // ── Loudness ──────────────────────────────────────────────────────────────
    public float[]   loudnessDb;         // 20·log10(rms) per frame

    // ── Timbral ───────────────────────────────────────────────────────────────
    public float[]   aggressiveness;     // 0..1 per frame
    public float[]   timbralChange;      // rate of spectral color change, 0..1
    public float[]   intensity;          // 0..1 composite (energy + flux)

    // ── Dynamics ──────────────────────────────────────────────────────────────
    public float[]   energyGradient;     // normalized dE/dt, -1..+1
    public float[]   buildupCurve;       // 0..1 buildup momentum per frame

    // ── Dynamic events ────────────────────────────────────────────────────────
    public float[]   impactTimes;        // times of strong hits (seconds)
    public float[]   dropTimes;          // times of drops / releases
    public float[]   buildupStartTimes;  // when each buildup begins

    // ── Zones ─────────────────────────────────────────────────────────────────
    public TimeRange[]  silenceZones;
    public TimeRange[]  breakZones;
    public TimeRange[]  crescendoZones;
    public TimeRange[]  decrescendoZones;
    public VocalZone[]  vocalZones;

    // ── Structural density ────────────────────────────────────────────────────
    public float[]   density;            // 0..1 onset density per frame

    // ── Harmony ───────────────────────────────────────────────────────────────
    public float[]   totalChroma;        // 12-bin accumulated chromagram
    public int       estimatedKey;       // 0=C … 11=B, -1 = not computed
    public bool      isMajorMode;
    public float     modeConfidence;     // 0..1
    public float[]   melodicContour;     // smoothed centroid derivative, -1..+1

    // ── Song-level scores ─────────────────────────────────────────────────────
    public float     complexity;         // 0..1
    public float     danceability;       // 0..1
    public float     tempoStability;     // 0..1
    public float     beatConfidence;     // 0..1

    // ── Repeated sections ─────────────────────────────────────────────────────
    public SimilarSection[] similarSections;

    // ── Semantic music tags (musicnn, local Sentis inference) ─────────────────
    // Raw model output, sorted by descending score, already thresholded/top-N'd per
    // AudioAnalysisConfig — see SentisMusicTagger. Cached (see SongProfileData); a stale-model
    // cache entry is detected via musicTagModelVersion != SentisMusicTagger.ModelVersion and
    // re-tagged without discarding the rest of the (expensive) cached analysis.
    public MusicTagScore[] musicTags;
    public int             musicTagModelVersion;

    public bool HasMusicTags => musicTags != null && musicTags.Length > 0;

    // ── Note names lookup ─────────────────────────────────────────────────────
    public static readonly string[] NoteNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public string KeyName => estimatedKey >= 0
        ? NoteNames[estimatedKey] + (isMajorMode ? " maj" : " min")
        : "?";

    // ── Lookup methods ────────────────────────────────────────────────────────

    public SongSegment GetSegmentAt(float time)
    {
        if (segments == null || segments.Length == 0) return default;
        foreach (var seg in segments)
            if (time >= seg.startTime && time < seg.endTime)
                return seg;
        return segments[segments.Length - 1];
    }

    public float GetEnergyAt(float time)         => Sample(energyEnvelope,   time);
    public float GetLoudnessAt(float time)        => Sample(loudnessDb,       time);
    public float GetIntensityAt(float time)       => Sample(intensity,        time);
    public float GetDensityAt(float time)         => Sample(density,          time);
    public float GetFlatnessAt(float time)        => Sample(spectralFlatness, time);
    public float GetAgressAt(float time)          => Sample(aggressiveness,   time);
    public float GetBuildupAt(float time)         => Sample(buildupCurve,     time);
    public float GetGradientAt(float time)        => Sample(energyGradient,   time);
    public float GetVoiceProbAt(float time)       => Sample(voiceProbability, time);
    public float GetMelodicContourAt(float time)  => Sample(melodicContour,   time);
    public float GetTimbralChangeAt(float time)   => Sample(timbralChange,    time);

    /// <summary>True if `time` falls inside a detected contiguous vocal zone (speech/singing).</summary>
    public bool IsVocalAt(float time)
    {
        if (vocalZones == null) return false;
        foreach (var z in vocalZones)
            if (time >= z.start && time < z.end) return true;
        return false;
    }

    public float GetBandEnergyAt(int bandIndex, float time)
    {
        if (bandEnvelopes == null || bandIndex >= bandEnvelopes.Length) return 0f;
        return Sample(bandEnvelopes[bandIndex], time);
    }

    public int VisualBandCount => visualBandEnvelopes?.Length ?? 0;

    public float GetVisualBandEnergyAt(int bandIndex, float time)
    {
        if (visualBandEnvelopes == null || bandIndex < 0 || bandIndex >= visualBandEnvelopes.Length) return 0f;
        return Sample(visualBandEnvelopes[bandIndex], time);
    }

    /// <summary>
    /// Catmull-Rom interpolated sample of a visual band's energy — unlike GetVisualBandEnergyAt
    /// (nearest-frame/floor), this is continuous between analysis frames. Used wherever a
    /// signal gets resampled much finer than analysisHopTime (the world mesh's per-row height,
    /// the UI equalizer bars): a nearest-frame lookup sampled that finely just repeats the same
    /// raw value across several rows/frames then jumps at the next frame boundary — a staircase,
    /// not a smooth signal, that no amount of downstream mesh subdivision or box-blur smoothing
    /// fixes at the source (the downstream smoothing was only rounding/flattening that staircase's
    /// steps, which also ate real peak height — fixing the sampling here needed no downstream
    /// value-losing correction). The result is clamped to the local 4-point value range so it
    /// still passes exactly through every real sample and never overshoots beyond what the data
    /// actually contains.
    /// </summary>
    public float GetVisualBandEnergyAtSmooth(int bandIndex, float time)
    {
        if (visualBandEnvelopes == null || bandIndex < 0 || bandIndex >= visualBandEnvelopes.Length || analysisHopTime <= 0f)
            return 0f;
        var arr = visualBandEnvelopes[bandIndex];
        if (arr == null || arr.Length == 0) return 0f;
        if (arr.Length == 1) return arr[0];

        float f    = Mathf.Max(0f, time) / analysisHopTime;
        int   i1   = Mathf.Clamp(Mathf.FloorToInt(f), 0, arr.Length - 1);
        float frac = Mathf.Clamp01(f - i1);
        int   i0   = Mathf.Clamp(i1 - 1, 0, arr.Length - 1);
        int   i2   = Mathf.Clamp(i1 + 1, 0, arr.Length - 1);
        int   i3   = Mathf.Clamp(i1 + 2, 0, arr.Length - 1);

        float p0 = arr[i0], p1 = arr[i1], p2 = arr[i2], p3 = arr[i3];
        float result = CatmullRom1D(p0, p1, p2, p3, frac);

        float lo = Mathf.Min(Mathf.Min(p0, p1), Mathf.Min(p2, p3));
        float hi = Mathf.Max(Mathf.Max(p0, p1), Mathf.Max(p2, p3));
        return Mathf.Clamp(result, lo, hi); // never overshoot beyond the local data range
    }

    private static float CatmullRom1D(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>Returns a copy of the 12-bin chroma vector at the given song time.</summary>
    public float[] GetChromaAt(float time)
    {
        if (chromaFlat == null || analysisHopTime <= 0f) return null;
        int frame = Mathf.Clamp(Mathf.FloorToInt(time / analysisHopTime),
                                0, chromaFlat.Length / 12 - 1);
        var c = new float[12];
        System.Array.Copy(chromaFlat, frame * 12, c, 0, 12);
        return c;
    }

    private float Sample(float[] arr, float time)
    {
        if (arr == null || arr.Length == 0 || analysisHopTime <= 0f) return 0f;
        int idx = Mathf.Clamp(Mathf.FloorToInt(time / analysisHopTime), 0, arr.Length - 1);
        return arr[idx];
    }
}
