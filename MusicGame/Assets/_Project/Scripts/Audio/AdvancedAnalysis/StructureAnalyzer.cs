using UnityEngine;

/// <summary>
/// High-level structural scores: onset density, complexity, danceability,
/// tempo stability, and beat confidence.
/// Reads intensity + timbralChange from profile (TimbralAnalyzer must run first).
/// </summary>
public class StructureAnalyzer : IOfflineAnalyzer
{
    public bool IsEnabled(AudioAnalysisConfig config)
        => config.advancedEnabled && config.advancedStructure;

    public void Analyze(OfflineAnalysisContext ctx, SongProfile profile)
    {
        int n = ctx.NumFrames;
        if (n == 0) return;

        float   hopTime = ctx.HopTime;
        float[] energy  = ctx.EnergyEnvelope;
        float[] onsets  = ctx.OnsetTimes;

        // ── Onset density (per frame) ─────────────────────────────────────────
        // Count onsets in ±1s window around each frame, normalize to [0,1]
        profile.density = ComputeDensity(energy, onsets, n, hopTime);

        // ── Tempo stability ────────────────────────────────────────────────────
        float stability = ComputeTempoStability(onsets, ctx.EstimatedBPM);
        profile.tempoStability = stability;

        // ── Beat confidence (sharpness of BPM peak in IOI histogram) ──────────
        // Approximate: fraction of IOIs within ±10 % of beat period
        profile.beatConfidence = ComputeBeatConfidence(onsets, ctx.EstimatedBPM);

        // ── Complexity (variance-based composite) ─────────────────────────────
        float densityVariance   = Variance(profile.density);
        float timbralChangeRate = profile.timbralChange != null
            ? Mean(profile.timbralChange) : 0f;
        float tempoInstability  = 1f - stability;
        profile.complexity = Mathf.Clamp01(
            0.4f * Mathf.Sqrt(densityVariance * 4f) +
            0.4f * timbralChangeRate +
            0.2f * tempoInstability);

        // ── Danceability ──────────────────────────────────────────────────────
        // BPM bonus peaks at 90-150 BPM range (most danceable)
        float bpm      = ctx.EstimatedBPM;
        float bpmBonus = bpm >= 90f && bpm <= 150f
            ? 1f - Mathf.Abs(bpm - 120f) / 60f
            : 0.2f;

        float energyConsistency = 1f - Mathf.Sqrt(Variance(Normalize(energy)));
        profile.danceability = Mathf.Clamp01(
            0.40f * stability +
            0.25f * profile.beatConfidence +
            0.20f * energyConsistency +
            0.15f * bpmBonus);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static float[] ComputeDensity(float[] energy, float[] onsets, int n, float hopTime)
    {
        var density  = new float[n];
        int window   = Mathf.Max(1, Mathf.RoundToInt(1f / hopTime));

        // Build binary onset array for fast windowed sum
        var onsetBin = new bool[n];
        if (onsets != null)
            foreach (float t in onsets)
            {
                int f = Mathf.Clamp(Mathf.FloorToInt(t / hopTime), 0, n - 1);
                onsetBin[f] = true;
            }

        int count = 0;
        // Sliding window using prefix sum would be ideal, but for typical n < 60k: O(n·W) is fine
        for (int f = 0; f < n; f++)
        {
            int lo = Mathf.Max(0, f - window);
            int hi = Mathf.Min(n - 1, f + window);
            count = 0;
            for (int j = lo; j <= hi; j++) if (onsetBin[j]) count++;
            density[f] = (float)count / (2 * window + 1);
        }
        AdvancedFFTFeatures.NormalizeInPlace(density);
        return density;
    }

    private static float ComputeTempoStability(float[] onsets, float bpm)
    {
        if (onsets == null || onsets.Length < 4) return 0.5f;
        float period = 60f / Mathf.Max(bpm, 1f);
        float tol    = period * 0.12f;
        int   stable = 0;
        for (int i = 0; i < onsets.Length - 1; i++)
        {
            float ioi = onsets[i + 1] - onsets[i];
            // Allow period multiples/fractions
            for (float mult = 0.5f; mult <= 2.01f; mult += 0.5f)
            {
                if (Mathf.Abs(ioi - period * mult) < tol * mult)
                {
                    stable++;
                    break;
                }
            }
        }
        return Mathf.Clamp01((float)stable / (onsets.Length - 1));
    }

    private static float ComputeBeatConfidence(float[] onsets, float bpm)
    {
        if (onsets == null || onsets.Length < 4) return 0.5f;
        float period = 60f / Mathf.Max(bpm, 1f);
        float tol    = period * 0.08f; // tighter for confidence
        int   aligned = 0;
        for (int i = 0; i < onsets.Length - 1; i++)
        {
            float ioi = onsets[i + 1] - onsets[i];
            float mod = ioi % period;
            if (mod > period * 0.5f) mod = period - mod;
            if (mod < tol) aligned++;
        }
        return Mathf.Clamp01((float)aligned / (onsets.Length - 1));
    }

    private static float Variance(float[] arr)
    {
        if (arr == null || arr.Length == 0) return 0f;
        float m = Mean(arr), v = 0f;
        foreach (float x in arr) v += (x - m) * (x - m);
        return v / arr.Length;
    }

    private static float Mean(float[] arr)
    {
        if (arr == null || arr.Length == 0) return 0f;
        float s = 0f;
        foreach (float x in arr) s += x;
        return s / arr.Length;
    }

    private static float[] Normalize(float[] arr)
    {
        float max = 0f;
        foreach (float v in arr) if (v > max) max = v;
        if (max <= 0f) return arr;
        var norm = new float[arr.Length];
        for (int i = 0; i < arr.Length; i++) norm[i] = arr[i] / max;
        return norm;
    }
}
