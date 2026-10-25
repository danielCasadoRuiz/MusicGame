using System;

namespace MusicGame.Audio.Analysis
{
    /// <summary>
    /// Zero-phase (centred, symmetric) windowed-sinc low-pass + integer decimation. Output sample j
    /// corresponds to input sample j·factor exactly (no group delay), so frame centres map 1:1.
    /// Used for the analyses that only need low frequencies (lowest visual bands, kick band) so
    /// their long windows cost a fraction of a full-rate FFT. Chunk-parallel and deterministic.
    /// </summary>
    public sealed class LowRateSignal
    {
        public readonly float[] samples;
        public readonly int factor;
        public readonly int sampleRate; // decimated rate (rounded down; exact rate = source / factor)
        public readonly double exactRate;

        private LowRateSignal(float[] s, int factor, double exactRate)
        {
            samples = s; this.factor = factor; this.exactRate = exactRate; sampleRate = (int)Math.Floor(exactRate);
        }

        /// <summary>Factor-1 view of a full-rate signal (no copy).</summary>
        public static LowRateSignal Wrap(float[] x, int sampleRate) => new LowRateSignal(x, 1, sampleRate);

        /// <summary>Largest power-of-two factor keeping the decimated rate ≥ minRateHz.</summary>
        public static int ChooseFactor(int sampleRate, double minRateHz)
        {
            int f = 1;
            while (sampleRate / (double)(f * 2) >= minRateHz) f *= 2;
            return f;
        }

        public static LowRateSignal Create(float[] x, int sampleRate, int factor, AnalysisRunOptions opt)
        {
            int n = (x.Length + factor - 1) / factor;
            var y = new float[n];
            if (factor <= 1) { Array.Copy(x, y, x.Length); return new LowRateSignal(y, 1, sampleRate); }

            // Cutoff at 80 % of the output Nyquist, Blackman window, 16 taps per factor (odd length).
            int half = 8 * factor;
            int taps = 2 * half + 1;
            double fc = 0.8 * 0.5 / factor; // cycles per input sample
            var h = new float[taps];
            double sum = 0;
            for (int i = 0; i < taps; i++)
            {
                int k = i - half;
                double sinc = k == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * k) / (Math.PI * k);
                double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (taps - 1));
                h[i] = (float)(sinc * w);
                sum += h[i];
            }
            for (int i = 0; i < taps; i++) h[i] = (float)(h[i] / sum);

            int len = x.Length;
            ParallelChunks.Run(n, 8192, opt, () => 0, (a, b, _) =>
            {
                for (int j = a; j < b; j++)
                {
                    int c = j * factor;
                    int s0 = c - half;
                    float acc = 0f;
                    if (s0 >= 0 && s0 + taps <= len)
                        for (int i = 0; i < taps; i++) acc += h[i] * x[s0 + i];
                    else
                        for (int i = 0; i < taps; i++) { int idx = s0 + i; if (idx >= 0 && idx < len) acc += h[i] * x[idx]; }
                    y[j] = acc;
                }
            });
            return new LowRateSignal(y, factor, (double)sampleRate / factor);
        }
    }
}
