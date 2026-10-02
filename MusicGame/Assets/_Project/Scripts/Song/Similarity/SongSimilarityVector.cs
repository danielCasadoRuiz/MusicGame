using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Internal numeric fingerprint of one PLAYABLE catalog song, used ONLY to compare songs
/// (SongSimilarityService). NOT GameMusicStyle (that single enum drives UI/theme/visuals): two DREAMY
/// songs can still be far apart here.
///
/// Two blocks, every value mapped to 0..1 by a fixed, documented range (see Build):
///   features — FeatureNames order (song-level SongProfile features)
///   tags     — SentisMusicTagger.TagLabels order (musicnn probability; tags outside the stored
///              top-N are 0)
/// Catalog-relative standardisation (z-score per dimension) happens later, in SongSimilarityService,
/// so no dimension dominates merely because of its scale. Small: ~66 floats per song.
/// </summary>
[System.Serializable]
public class SongSimilarityVector
{
    /// <summary>Bump when the feature layout/mapping changes — older vectors are then ignored and rebuilt.</summary>
    public const int CurrentVersion = 1;

    public static readonly string[] FeatureNames =
    {
        "intensity", "dynamics", "aggressiveness", "danceability", "density", "complexity",
        "tempo", "tempoStability", "beatConfidence", "majorness", "voice", "brightness",
        "noisiness", "timbralChange", "impacts", "loudness",
    };

    /// <summary>Stable song id = the playable song's Addressable address.</summary>
    public string songId;
    public int version = CurrentVersion;
    public float[] features;
    public float[] tags;

    public bool IsValid => version == CurrentVersion && features != null && features.Length == FeatureNames.Length
                           && tags != null && tags.Length == SentisMusicTagger.TagLabels.Count;

    /// <summary>From a FULL SongProfile (derived features present). Null if the profile is not full.</summary>
    public static SongSimilarityVector Build(string songId, SongProfile p)
    {
        if (string.IsNullOrEmpty(songId) || p == null || p.energyEnvelope == null || p.energyEnvelope.Length == 0) return null;

        float minutes = Mathf.Max(p.duration, 1f) / 60f;
        bool keyed = p.estimatedKey >= 0;
        var f = new[]
        {
            Mean(p.intensity),                                                   // per-song normalised 0..1
            Mathf.Clamp01(Std(p.intensity) / 0.3f),                              // spread of intensity (0.3 ≈ very dynamic)
            Mean(p.aggressiveness),                                              // 0..1
            Clamp(p.danceability),                                               // 0..1
            Mean(p.density),                                                     // 0..1
            Clamp(p.complexity),                                                 // 0..1
            p.estimatedBPM > 0f ? Mathf.Clamp01(Mathf.Log(p.estimatedBPM / 60f, 2f) / Mathf.Log(200f / 60f, 2f)) : 0.5f, // log 60..200 BPM
            Clamp(p.tempoStability),                                             // 0..1
            Clamp(p.beatConfidence),                                             // 0..1
            keyed ? (p.isMajorMode ? Clamp(p.modeConfidence) : 1f - Clamp(p.modeConfidence)) : 0.5f, // 1 = clearly major
            Mean(p.voiceProbability, 0.5f),                                      // 0..1
            Brightness(p.spectralCentroid),                                      // log 250 Hz..4 kHz
            Mean(p.spectralFlatness, 0.5f),                                      // 0 tonal .. 1 noise
            Mean(p.timbralChange),                                               // 0..1
            Mathf.Clamp01((p.impactTimes?.Length ?? 0) / minutes / 60f),         // strong hits per minute, 60/min = 1
            p.loudnessDb != null && p.loudnessDb.Length > 0 ? Mathf.Clamp01((MeanRaw(p.loudnessDb) + 50f) / 50f) : 0.5f, // −50..0 dB
        };

        var labels = SentisMusicTagger.TagLabels;
        var t = new float[labels.Count];
        if (p.musicTags != null)
        {
            var index = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < labels.Count; i++) index[labels[i]] = i;
            foreach (var tag in p.musicTags)
                if (tag.tag != null && index.TryGetValue(tag.tag, out int i)) t[i] = Mathf.Max(t[i], Clamp(tag.score));
        }
        return new SongSimilarityVector { songId = songId, features = f, tags = t };
    }

    private static float Clamp(float v) => float.IsNaN(v) ? 0.5f : Mathf.Clamp01(v);

    private static float Mean(float[] a, float fallback = 0.5f)
    {
        if (a == null || a.Length == 0) return fallback;
        return Mathf.Clamp01(MeanRaw(a));
    }

    private static float MeanRaw(float[] a)
    {
        double s = 0; int n = 0;
        foreach (var v in a) if (!float.IsNaN(v) && !float.IsInfinity(v)) { s += v; n++; }
        return n > 0 ? (float)(s / n) : 0f;
    }

    private static float Std(float[] a)
    {
        if (a == null || a.Length < 2) return 0f;
        double s = 0, sq = 0; foreach (var v in a) { s += v; sq += v * v; }
        double m = s / a.Length;
        return (float)System.Math.Sqrt(System.Math.Max(0, sq / a.Length - m * m));
    }

    private static float Brightness(float[] centroidHz)
    {
        if (centroidHz == null || centroidHz.Length == 0) return 0.5f;
        float c = Mathf.Max(1f, MeanRaw(centroidHz));
        return Mathf.Clamp01(Mathf.Log(c / 250f, 2f) / 4f); // 250 Hz → 0, 4 kHz → 1
    }
}
