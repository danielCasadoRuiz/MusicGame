using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds structurally similar sections by comparing compact feature vectors
/// per segment (energy mean, centroid mean, flatness mean, 12 chroma bins).
/// Disabled by default — enable advancedSimilarity in AudioAnalysisConfig.
/// O(S²) where S = duration / segmentSize. For a 4-min song at 4s segments: ~60² = 3600 ops.
/// </summary>
public class SimilarityAnalyzer : IOfflineAnalyzer
{
    public bool IsEnabled(AudioAnalysisConfig config)
        => config.advancedEnabled && config.advancedSimilarity;

    public void Analyze(OfflineAnalysisContext ctx, SongProfile profile)
    {
        int   n         = ctx.NumFrames;
        float hopTime   = ctx.HopTime;
        float segSize   = Mathf.Max(1f, ctx.Config.similaritySegmentSize);
        float threshold = ctx.Config.similarityThreshold;
        int   segFrames = Mathf.Max(1, Mathf.RoundToInt(segSize / hopTime));
        int   numSegs   = n / segFrames;
        if (numSegs < 2) return;

        bool hasChroma   = ctx.ChromaFlat    != null;
        bool hasFlatness = ctx.SpectralFlatness != null;

        // Build feature vector per segment (15 dims: energy + centroid + flatness + 12 chroma)
        const int DIMS = 15;
        var features = new float[numSegs][];
        for (int s = 0; s < numSegs; s++)
        {
            int fStart = s * segFrames;
            int fEnd   = Mathf.Min(fStart + segFrames, n);
            int count  = fEnd - fStart;

            var vec = new float[DIMS];

            // Energy mean (dim 0)
            float eSum = 0f;
            for (int f = fStart; f < fEnd; f++) eSum += ctx.EnergyEnvelope[f];
            vec[0] = eSum / count;

            // Centroid mean normalized (dim 1)
            float cSum = 0f;
            for (int f = fStart; f < fEnd; f++) cSum += ctx.SpectralCentroid[f];
            vec[1] = cSum / count / 8000f; // approximate normalization by max freq

            // Flatness mean (dim 2)
            if (hasFlatness)
            {
                float flSum = 0f;
                for (int f = fStart; f < fEnd; f++) flSum += ctx.SpectralFlatness[f];
                vec[2] = flSum / count;
            }

            // 12 chroma bins mean (dims 3..14)
            if (hasChroma)
            {
                for (int c = 0; c < 12; c++)
                {
                    float chromaSum = 0f;
                    for (int f = fStart; f < fEnd; f++)
                        chromaSum += ctx.ChromaFlat[f * 12 + c];
                    vec[3 + c] = chromaSum / count;
                }
            }

            features[s] = vec;
        }

        // Normalize each feature dimension across all segments
        for (int d = 0; d < DIMS; d++)
        {
            float max = 1e-6f;
            for (int s = 0; s < numSegs; s++) if (features[s][d] > max) max = features[s][d];
            for (int s = 0; s < numSegs; s++) features[s][d] /= max;
        }

        // Pairwise cosine similarity
        var similar = new List<SimilarSection>();
        for (int a = 0; a < numSegs - 1; a++)
        {
            for (int b = a + 2; b < numSegs; b++) // skip adjacent segments
            {
                float sim = CosineSim(features[a], features[b]);
                if (sim >= threshold)
                {
                    similar.Add(new SimilarSection
                    {
                        timeA      = a * segSize,
                        timeB      = b * segSize,
                        length     = segSize,
                        similarity = sim,
                    });
                }
            }
        }
        profile.similarSections = similar.ToArray();
    }

    private static float CosineSim(float[] a, float[] b)
    {
        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        float denom = Mathf.Sqrt(normA * normB);
        return denom > 0f ? dot / denom : 0f;
    }
}
