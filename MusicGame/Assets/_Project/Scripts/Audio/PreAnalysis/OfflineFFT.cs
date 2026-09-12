using UnityEngine;

public static class OfflineFFT
{
    public static float[] Compute(float[] samples, FFTWindowType windowType = FFTWindowType.BlackmanHarris)
    {
        int n = samples.Length;

        float[] real = ApplyWindow(samples, windowType);
        float[] imag = new float[n];

        Transform(real, imag);

        float[] magnitude = new float[n / 2];
        for (int i = 0; i < n / 2; i++)
            magnitude[i] = Mathf.Sqrt(real[i] * real[i] + imag[i] * imag[i]) / n;

        return magnitude;
    }

    private static void Transform(float[] real, float[] imag)
    {
        int n = real.Length;

        // Bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        // Cooley-Tukey DIT butterfly
        for (int len = 2; len <= n; len <<= 1)
        {
            float angle = -2f * Mathf.PI / len;
            float wr    = Mathf.Cos(angle);
            float wi    = Mathf.Sin(angle);
            int   half  = len >> 1;

            for (int i = 0; i < n; i += len)
            {
                float cr = 1f, ci = 0f;
                for (int j = 0; j < half; j++)
                {
                    int   u  = i + j;
                    int   v  = i + j + half;
                    float vr = real[v] * cr - imag[v] * ci;
                    float vi = real[v] * ci + imag[v] * cr;

                    real[v] = real[u] - vr;
                    imag[v] = imag[u] - vi;
                    real[u] += vr;
                    imag[u] += vi;

                    float ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = ncr;
                }
            }
        }
    }

    /// <summary>
    /// Raw (unnormalized) power spectrum |FFT|^2, including the Nyquist bin (n/2+1 bins total)
    /// — the convention librosa's STFT/melspectrogram uses, unlike Compute() above which returns
    /// a magnitude/n-normalized spectrum without the Nyquist bin for the existing visualizer/
    /// detector pipeline. Kept as a separate method so that pipeline's normalization is untouched.
    /// </summary>
    public static float[] ComputePowerSpectrum(float[] samples, FFTWindowType windowType)
    {
        int n = samples.Length;

        float[] real = ApplyWindow(samples, windowType);
        float[] imag = new float[n];

        Transform(real, imag);

        float[] power = new float[n / 2 + 1];
        for (int i = 0; i <= n / 2; i++)
            power[i] = real[i] * real[i] + imag[i] * imag[i];

        return power;
    }

    private static float[] ApplyWindow(float[] samples, FFTWindowType type)
    {
        int     n    = samples.Length;
        float[] out_ = new float[n];
        float   nm1  = n - 1;

        for (int i = 0; i < n; i++)
        {
            float w = type switch
            {
                FFTWindowType.Hanning =>
                    0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / nm1)),
                // "Periodic"/DFT-even Hann (denominator n, not n-1) — the convention scipy's
                // get_window(..., fftbins=True) and librosa's STFT use for windowing, as opposed
                // to the symmetric Hanning case above (kept unchanged for existing callers).
                FFTWindowType.HannPeriodic =>
                    0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / n),
                FFTWindowType.Hamming =>
                    0.54f - 0.46f * Mathf.Cos(2f * Mathf.PI * i / nm1),
                FFTWindowType.BlackmanHarris =>
                    0.35875f
                    - 0.48829f * Mathf.Cos(2f * Mathf.PI * i / nm1)
                    + 0.14128f * Mathf.Cos(4f * Mathf.PI * i / nm1)
                    - 0.01168f * Mathf.Cos(6f * Mathf.PI * i / nm1),
                _ => 1f
            };
            out_[i] = samples[i] * w;
        }
        return out_;
    }
}

public enum FFTWindowType { Rectangular, Hanning, Hamming, BlackmanHarris, HannPeriodic }
