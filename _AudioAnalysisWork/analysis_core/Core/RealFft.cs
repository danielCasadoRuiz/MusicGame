using System;
using System.Collections.Generic;

namespace MusicGame.Audio.Analysis
{
    public enum WindowKind : byte { Rectangular = 0, Hann = 1, HannPeriodic = 2, Hamming = 3, BlackmanHarris = 4 }

    /// <summary>
    /// Radix-2 FFT of a REAL signal of size N, computed as a complex FFT of size N/2 plus a split
    /// step. All tables (bit reversal, twiddles, split factors) are precomputed in double precision
    /// at construction; the plan is immutable and can be shared by any number of threads — every
    /// mutable buffer is a caller-owned <see cref="FftScratch"/>.
    /// </summary>
    public sealed class RealFftPlan
    {
        public readonly int Size;     // N, power of two >= 4
        public readonly int Bins;     // N/2 + 1 (DC..Nyquist)
        private readonly int _m;      // N/2
        private readonly int[] _bitRev;
        private readonly float[] _twRe, _twIm;     // e^{-2πi k / M}, k < M/2
        private readonly float[] _splitRe, _splitIm; // e^{-2πi k / N}, k <= M

        private static readonly Dictionary<int, RealFftPlan> Cache = new Dictionary<int, RealFftPlan>();

        /// <summary>Thread-safe shared plan per size.</summary>
        public static RealFftPlan Get(int size)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(size, out var p)) { p = new RealFftPlan(size); Cache[size] = p; }
                return p;
            }
        }

        public RealFftPlan(int size)
        {
            if (size < 4 || (size & (size - 1)) != 0) throw new ArgumentException("FFT size must be a power of two >= 4");
            Size = size;
            Bins = size / 2 + 1;
            _m   = size / 2;

            _bitRev = new int[_m];
            int bits = 0;
            while ((1 << bits) < _m) bits++;
            for (int i = 0; i < _m; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                _bitRev[i] = r;
            }

            int halfM = Math.Max(1, _m / 2);
            _twRe = new float[halfM];
            _twIm = new float[halfM];
            for (int k = 0; k < halfM; k++)
            {
                double a = -2.0 * Math.PI * k / _m;
                _twRe[k] = (float)Math.Cos(a);
                _twIm[k] = (float)Math.Sin(a);
            }

            _splitRe = new float[_m + 1];
            _splitIm = new float[_m + 1];
            for (int k = 0; k <= _m; k++)
            {
                double a = -2.0 * Math.PI * k / size;
                _splitRe[k] = (float)Math.Cos(a);
                _splitIm[k] = (float)Math.Sin(a);
            }
        }

        /// <summary>
        /// Forward transform of input[0..N). Writes X[k] for k = 0..N/2 into outRe/outIm (length ≥ N/2+1).
        /// Unnormalised (X[0] = Σ x). Allocation-free.
        /// </summary>
        public void Forward(float[] input, float[] outRe, float[] outIm, FftScratch s)
        {
            int m = _m;
            float[] zr = s.Re, zi = s.Im;

            // Pack even/odd samples as one complex sequence, directly in bit-reversed order.
            for (int k = 0; k < m; k++)
            {
                int r = _bitRev[k];
                zr[r] = input[2 * k];
                zi[r] = input[2 * k + 1];
            }

            // Iterative radix-2 DIT on M points with the precomputed twiddle table.
            for (int len = 2; len <= m; len <<= 1)
            {
                int half = len >> 1;
                int step = m / len;
                for (int i = 0; i < m; i += len)
                {
                    for (int j = 0; j < half; j++)
                    {
                        float wr = _twRe[j * step], wi = _twIm[j * step];
                        int u = i + j, v = u + half;
                        float vr = zr[v] * wr - zi[v] * wi;
                        float vi = zr[v] * wi + zi[v] * wr;
                        zr[v] = zr[u] - vr;
                        zi[v] = zi[u] - vi;
                        zr[u] += vr;
                        zi[u] += vi;
                    }
                }
            }

            // Split: X[k] = E[k] + W^k O[k], E = (Z[k] + conj Z[M-k]) / 2, O = -i (Z[k] - conj Z[M-k]) / 2
            for (int k = 0; k <= m; k++)
            {
                int ka = k == m ? 0 : k;
                int kb = k == 0 ? 0 : m - k;
                float ar = zr[ka], ai = zi[ka], br = zr[kb], bi = zi[kb];
                float er = 0.5f * (ar + br), ei = 0.5f * (ai - bi);
                float odr = 0.5f * (ai + bi), odi = -0.5f * (ar - br);
                float wr = _splitRe[k], wi = _splitIm[k];
                outRe[k] = er + (wr * odr - wi * odi);
                outIm[k] = ei + (wr * odi + wi * odr);
            }
        }
    }

    /// <summary>Per-worker mutable FFT buffers (never share one between threads).</summary>
    public sealed class FftScratch
    {
        public readonly float[] Re, Im;
        public FftScratch(int fftSize) { Re = new float[fftSize / 2]; Im = new float[fftSize / 2]; }
    }

    /// <summary>Precomputed analysis windows, shared and immutable once built.</summary>
    public static class WindowTables
    {
        private static readonly Dictionary<long, float[]> Cache = new Dictionary<long, float[]>();

        /// <summary>Returns a SHARED table — callers must never write to it.</summary>
        public static float[] Get(WindowKind kind, int length)
        {
            long key = ((long)kind << 32) | (uint)length;
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out var w)) { w = Build(kind, length); Cache[key] = w; }
                return w;
            }
        }

        /// <summary>Same formulas as the legacy OfflineFFT.ApplyWindow (symmetric n-1 denominators
        /// except HannPeriodic), evaluated in double.</summary>
        public static float[] Build(WindowKind kind, int n)
        {
            var w = new float[n];
            double nm1 = Math.Max(1, n - 1);
            for (int i = 0; i < n; i++)
            {
                double v;
                switch (kind)
                {
                    case WindowKind.Hann:          v = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / nm1)); break;
                    case WindowKind.HannPeriodic:  v = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / n); break;
                    case WindowKind.Hamming:       v = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / nm1); break;
                    case WindowKind.BlackmanHarris:
                        v = 0.35875 - 0.48829 * Math.Cos(2.0 * Math.PI * i / nm1)
                                    + 0.14128 * Math.Cos(4.0 * Math.PI * i / nm1)
                                    - 0.01168 * Math.Cos(6.0 * Math.PI * i / nm1);
                        break;
                    default: v = 1.0; break;
                }
                w[i] = (float)v;
            }
            return w;
        }

        public static int NextPowerOfTwo(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }
    }
}
