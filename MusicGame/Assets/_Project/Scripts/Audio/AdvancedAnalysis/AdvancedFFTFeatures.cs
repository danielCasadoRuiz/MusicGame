using UnityEngine;

/// <summary>
/// Per-frame spectrum features computed inside the AudioPreAnalyzer FFT loop.
/// All methods are pure and allocation-free (caller provides output buffers).
/// </summary>
public static class AdvancedFFTFeatures
{
    // ── Spectral Flatness ─────────────────────────────────────────────────────
    // Ratio of geometric mean to arithmetic mean of the spectrum.
    // 0 = pure tone, 1 = white noise. Great proxy for tonal vs. noisy content.

    public static float SpectralFlatness(float[] spectrum)
    {
        int   n      = spectrum.Length;
        float logSum = 0f;
        float linSum = 0f;
        for (int i = 0; i < n; i++)
        {
            float v = Mathf.Max(spectrum[i], 1e-10f);
            logSum += Mathf.Log(v);
            linSum += v;
        }
        float geoMean  = Mathf.Exp(logSum / n);
        float ariMean  = linSum / n;
        return ariMean > 0f ? Mathf.Clamp01(geoMean / ariMean) : 0f;
    }

    // ── Chromagram ────────────────────────────────────────────────────────────
    // Maps each FFT bin to one of 12 chromatic classes (C=0 … B=11).
    // Reference: C4 = 261.63 Hz. Valid range: 65 Hz (C2) – 5000 Hz.

    public static void ComputeChroma(float[] spectrum, int sampleRate, int windowSize,
                                     float[] chroma12Out)
    {
        const float C1 = 32.70f; // C1 = 32.703 Hz

        for (int c = 0; c < 12; c++) chroma12Out[c] = 0f;

        for (int i = 1; i < spectrum.Length; i++)
        {
            float freq = (float)i * sampleRate / windowSize;
            if (freq < 65f || freq > 5000f) continue;

            float midi  = 12f * Mathf.Log(freq / C1, 2f);
            int   c     = ((int)Mathf.Round(midi) % 12 + 12) % 12;
            chroma12Out[c] += spectrum[i];
        }

        // Normalize
        float total = 0f;
        for (int c = 0; c < 12; c++) total += chroma12Out[c];
        if (total > 0f)
            for (int c = 0; c < 12; c++) chroma12Out[c] /= total;
    }

    // ── Voice Probability ─────────────────────────────────────────────────────
    // Heuristic: voice energy concentrates in 300-3400 Hz (fundamental + formants)
    // and is relatively tonal (low spectral flatness).
    // NOT a neural detector — treats this as a "voice-likely" signal for gameplay.

    public static float VoiceProbability(float[] spectrum, int sampleRate, int windowSize,
                                         float spectralFlatness)
    {
        int voiceLo = Mathf.Max(1, Mathf.RoundToInt(300f  * windowSize / sampleRate));
        int voiceHi = Mathf.Min(spectrum.Length - 1,
                                Mathf.RoundToInt(3400f * windowSize / sampleRate));

        float voiceE = 0f, totalE = 0f;
        for (int i = 1; i < spectrum.Length; i++)
        {
            totalE += spectrum[i];
            if (i >= voiceLo && i <= voiceHi) voiceE += spectrum[i];
        }

        float ratio = totalE > 0f ? voiceE / totalE : 0f;

        // Tonal content boosts probability; noisy content reduces it
        float tonalBoost = 1f - spectralFlatness * 0.6f;

        return Mathf.Clamp01(ratio * tonalBoost * 2.2f); // ×2.2 so ~50% voice-band coverage → ~1
    }

    // ── Shared math ──────────────────────────────────────────────────────────

    /// <summary>Pearson correlation between two equal-length arrays.</summary>
    public static float PearsonCorr(float[] a, float[] b)
    {
        int   n     = a.Length;
        float meanA = 0f, meanB = 0f;
        for (int i = 0; i < n; i++) { meanA += a[i]; meanB += b[i]; }
        meanA /= n; meanB /= n;

        float num = 0f, denA = 0f, denB = 0f;
        for (int i = 0; i < n; i++)
        {
            float da = a[i] - meanA, db = b[i] - meanB;
            num  += da * db;
            denA += da * da;
            denB += db * db;
        }
        float den = Mathf.Sqrt(denA * denB);
        return den > 0f ? num / den : 0f;
    }

    /// <summary>Circular shift of a 12-element chroma array by semitones.</summary>
    public static float[] ShiftChroma(float[] arr, int shift)
    {
        var result = new float[12];
        for (int i = 0; i < 12; i++)
            result[i] = arr[((i - shift) % 12 + 12) % 12];
        return result;
    }

    /// <summary>Box-filter smoothing (in-place). windowFrames should be odd.</summary>
    public static void BoxSmooth(float[] arr, int windowFrames)
    {
        if (arr == null || arr.Length < 2 || windowFrames < 2) return;
        int   half = windowFrames / 2;
        var   tmp  = new float[arr.Length];
        for (int f = 0; f < arr.Length; f++)
        {
            int lo = Mathf.Max(0, f - half);
            int hi = Mathf.Min(arr.Length - 1, f + half);
            float s = 0f;
            for (int j = lo; j <= hi; j++) s += arr[j];
            tmp[f] = s / (hi - lo + 1);
        }
        System.Array.Copy(tmp, arr, arr.Length);
    }

    /// <summary>Normalize array to 0..1 range (in-place). Returns max value.</summary>
    public static float NormalizeInPlace(float[] arr)
    {
        float max = 0f;
        for (int i = 0; i < arr.Length; i++) if (arr[i] > max) max = arr[i];
        if (max > 0f) for (int i = 0; i < arr.Length; i++) arr[i] /= max;
        return max;
    }
}
