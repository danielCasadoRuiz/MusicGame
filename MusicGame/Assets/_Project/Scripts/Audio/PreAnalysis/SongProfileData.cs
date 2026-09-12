using System;
using UnityEngine;

[Serializable]
public class BandEnvelope
{
    public float[] values;
}

/// <summary>
/// JSON-serializable mirror of SongProfile for disk caching.
/// Stores per-frame arrays that require raw FFT spectrum to compute
/// (spectralFlatness, chromaFlat, voiceProbability) alongside the core features.
/// Derived features (loudness, dynamics, structure, harmony scores) are always
/// recomputed from these cached arrays — they are fast (< 1 s) and intentionally
/// excluded from cache to keep the format stable across config changes.
/// </summary>
[Serializable]
public class SongProfileData
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string clipName;
    public int    clipSamples;

    // ── Core scalars ──────────────────────────────────────────────────────────
    public float   duration;
    public int     sampleRate;
    public float   estimatedBPM;
    public float   averageEnergy;
    public float   maxEnergy;
    public float   analysisHopTime;

    // ── Core per-frame arrays ─────────────────────────────────────────────────
    public float[]        energyEnvelope;
    public float[]        spectralCentroid;
    public float[]        spectralFlux;
    public float[]        onsetTimes;
    public BandEnvelope[] bandEnvelopes;       // float[][] not supported by JsonUtility
    public BandEnvelope[] visualBandEnvelopes; // higher-resolution spectrum for mesh + debug
    public SongSegment[]  segments;

    // ── Spectrum-derived per-frame arrays (added for advanced analysis) ────────
    public float[] spectralFlatness;   // 0=tonal, 1=noise
    public float[] chromaFlat;         // numFrames×12
    public float[] voiceProbability;   // 0..1 raw

    // ── Semantic music tags (musicnn) — expensive (ML inference), so unlike loudness/dynamics/
    // harmony/etc. these ARE cached rather than always recomputed. musicTagModelVersion is the
    // presence/version guard AudioPreAnalyzer uses to re-tag ONLY this part when the model
    // changes, without invalidating the rest of an otherwise-still-valid cached analysis.
    public MusicTagScore[] musicTags;
    public int             musicTagModelVersion;

    // ── Validation ────────────────────────────────────────────────────────────

    public bool Matches(AudioClip clip)
        => clipName == clip.name && clipSamples == clip.samples;

    // ── Serialization helpers ─────────────────────────────────────────────────

    public static SongProfileData From(AudioClip clip, SongProfile p)
    {
        var bands       = ToBandEnvelopes(p.bandEnvelopes);
        var visualBands = ToBandEnvelopes(p.visualBandEnvelopes);

        return new SongProfileData
        {
            clipName          = clip.name,
            clipSamples       = clip.samples,
            duration          = p.duration,
            sampleRate        = p.sampleRate,
            estimatedBPM      = p.estimatedBPM,
            averageEnergy     = p.averageEnergy,
            maxEnergy         = p.maxEnergy,
            analysisHopTime   = p.analysisHopTime,
            energyEnvelope    = p.energyEnvelope,
            spectralCentroid  = p.spectralCentroid,
            spectralFlux      = p.spectralFlux,
            onsetTimes        = p.onsetTimes,
            bandEnvelopes     = bands,
            visualBandEnvelopes = visualBands,
            segments          = p.segments,
            spectralFlatness  = p.spectralFlatness,
            chromaFlat        = p.chromaFlat,
            voiceProbability  = p.voiceProbability,
            musicTags             = p.musicTags,
            musicTagModelVersion  = p.musicTagModelVersion,
        };
    }

    private static BandEnvelope[] ToBandEnvelopes(float[][] src)
    {
        int n = src?.Length ?? 0;
        var bands = new BandEnvelope[n];
        for (int i = 0; i < n; i++)
            bands[i] = new BandEnvelope { values = src[i] };
        return bands;
    }

    private static float[][] FromBandEnvelopes(BandEnvelope[] src)
    {
        int n = src?.Length ?? 0;
        var bands = new float[n][];
        for (int i = 0; i < n; i++)
            bands[i] = src[i]?.values;
        return bands;
    }

    public SongProfile ToProfile()
    {
        return new SongProfile
        {
            duration          = duration,
            sampleRate        = sampleRate,
            estimatedBPM      = estimatedBPM,
            averageEnergy     = averageEnergy,
            maxEnergy         = maxEnergy,
            analysisHopTime   = analysisHopTime,
            energyEnvelope    = energyEnvelope,
            spectralCentroid  = spectralCentroid,
            spectralFlux      = spectralFlux,
            onsetTimes        = onsetTimes,
            bandEnvelopes     = FromBandEnvelopes(bandEnvelopes),
            visualBandEnvelopes = FromBandEnvelopes(visualBandEnvelopes),
            segments          = segments,
            spectralFlatness  = spectralFlatness,
            chromaFlat        = chromaFlat,
            voiceProbability  = voiceProbability,
            estimatedKey      = -1,  // recomputed by HarmonyAnalyzer
            musicTags             = musicTags,
            musicTagModelVersion  = musicTagModelVersion,
        };
    }
}
