using UnityEngine;

/// <summary>
/// Harmony analysis from chromagram data:
/// - Accumulated 12-bin chroma
/// - Key estimation (Krumhansl-Schmuckler profiles)
/// - Major/minor mode + confidence
/// - Melodic contour (smoothed spectral centroid derivative)
/// </summary>
public class HarmonyAnalyzer : IOfflineAnalyzer
{
    // Krumhansl-Schmuckler key profiles
    private static readonly float[] MajorTemplate =
        { 6.35f, 2.23f, 3.48f, 2.33f, 4.38f, 4.09f, 2.52f, 5.19f, 2.39f, 3.66f, 2.29f, 2.88f };
    private static readonly float[] MinorTemplate =
        { 6.33f, 2.68f, 3.52f, 5.38f, 2.60f, 3.53f, 2.54f, 4.75f, 3.98f, 2.69f, 3.34f, 3.17f };

    public bool IsEnabled(AudioAnalysisConfig config)
        => config.advancedEnabled && config.advancedHarmony;

    public void Analyze(OfflineAnalysisContext ctx, SongProfile profile)
    {
        int n = ctx.NumFrames;

        // ── Melodic contour (works without chroma) ────────────────────────────
        profile.melodicContour = ComputeMelodicContour(ctx.SpectralCentroid, n);

        // ── Chroma-dependent features ──────────────────────────────────────────
        float[] chromaFlat = ctx.ChromaFlat;
        if (chromaFlat == null || chromaFlat.Length < 12)
        {
            profile.estimatedKey  = -1;
            profile.totalChroma   = new float[12];
            return;
        }

        // Accumulate per-frame chroma into song-level totals
        var total = new float[12];
        for (int f = 0; f < n; f++)
            for (int c = 0; c < 12; c++)
                total[c] += chromaFlat[f * 12 + c];

        // Normalize
        float sum = 0f;
        for (int c = 0; c < 12; c++) sum += total[c];
        if (sum > 0f) for (int c = 0; c < 12; c++) total[c] /= sum;
        profile.totalChroma = total;

        // Key + mode estimation via Pearson correlation with template rotations
        float bestCorr = float.MinValue;
        int   bestKey  = 0;
        bool  bestMaj  = true;

        for (int shift = 0; shift < 12; shift++)
        {
            float[] majShifted = AdvancedFFTFeatures.ShiftChroma(MajorTemplate, shift);
            float[] minShifted = AdvancedFFTFeatures.ShiftChroma(MinorTemplate, shift);

            float majCorr = AdvancedFFTFeatures.PearsonCorr(total, Normalize(majShifted));
            float minCorr = AdvancedFFTFeatures.PearsonCorr(total, Normalize(minShifted));

            if (majCorr > bestCorr) { bestCorr = majCorr; bestKey = shift; bestMaj = true; }
            if (minCorr > bestCorr) { bestCorr = minCorr; bestKey = shift; bestMaj = false; }
        }

        profile.estimatedKey   = bestKey;
        profile.isMajorMode    = bestMaj;
        profile.modeConfidence = Mathf.Clamp01((bestCorr + 1f) * 0.5f); // [-1,1] → [0,1]
    }

    // ── Melodic contour ───────────────────────────────────────────────────────
    // Approximated from spectral centroid direction: rising = brighter/higher pitch tendency

    private static float[] ComputeMelodicContour(float[] centroid, int n)
    {
        if (centroid == null || n < 3) return new float[n];

        // Smooth centroid first (~0.3s window)
        var smooth = (float[])centroid.Clone();
        AdvancedFFTFeatures.BoxSmooth(smooth, 15);

        var contour = new float[n];
        for (int f = 1; f < n - 1; f++)
            contour[f] = smooth[f + 1] - smooth[f - 1]; // central difference
        contour[0]     = contour[1];
        contour[n - 1] = contour[n - 2];

        // Smooth derivative and normalize to [-1, 1]
        AdvancedFFTFeatures.BoxSmooth(contour, 11);
        float maxAbs = 1e-6f;
        for (int f = 0; f < n; f++) if (Mathf.Abs(contour[f]) > maxAbs) maxAbs = Mathf.Abs(contour[f]);
        for (int f = 0; f < n; f++) contour[f] /= maxAbs;

        return contour;
    }

    // L2-normalize a 12-bin template
    private static float[] Normalize(float[] arr)
    {
        float sqSum = 0f;
        foreach (float v in arr) sqSum += v * v;
        float norm = Mathf.Sqrt(sqSum);
        var result = new float[arr.Length];
        for (int i = 0; i < arr.Length; i++) result[i] = norm > 0f ? arr[i] / norm : 0f;
        return result;
    }
}
