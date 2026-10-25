using System;
using System.Collections.Generic;

namespace Harness
{
    /// <summary>UnityEngine.Mathf semantics needed to reproduce the legacy code bit-for-bit-ish.</summary>
    public static class Mathf
    {
        public const float PI = 3.14159274f;
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Log(float f) => (float)Math.Log(f);
        public static float Log(float f, float p) => (float)Math.Log(f, p);
        public static float Log10(float f) => (float)Math.Log10(f);
        public static float Exp(float f) => (float)Math.Exp(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Round(float f) => (float)Math.Round(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static int NextPowerOfTwo(int v) { int p = 1; while (p < v) p <<= 1; return p; }
    }

    public enum LegacyWindow { Rectangular, Hanning, Hamming, BlackmanHarris, HannPeriodic }

    /// <summary>Verbatim port of OfflineFFT.cs (allocations and all).</summary>
    public static class LegacyFFT
    {
        public static float[] Compute(float[] samples, LegacyWindow windowType = LegacyWindow.BlackmanHarris)
        {
            int n = samples.Length;
            float[] real = ApplyWindow(samples, windowType);
            float[] imag = new float[n];
            Transform(real, imag);
            float[] magnitude = new float[n / 2];
            for (int i = 0; i < n / 2; i++) magnitude[i] = Mathf.Sqrt(real[i] * real[i] + imag[i] * imag[i]) / n;
            return magnitude;
        }

        public static float[] ComputePowerSpectrum(float[] samples, LegacyWindow windowType)
        {
            int n = samples.Length;
            float[] real = ApplyWindow(samples, windowType);
            float[] imag = new float[n];
            Transform(real, imag);
            float[] power = new float[n / 2 + 1];
            for (int i = 0; i <= n / 2; i++) power[i] = real[i] * real[i] + imag[i] * imag[i];
            return power;
        }

        private static void Transform(float[] real, float[] imag)
        {
            int n = real.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (real[i], real[j]) = (real[j], real[i]); (imag[i], imag[j]) = (imag[j], imag[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                float angle = -2f * Mathf.PI / len;
                float wr = Mathf.Cos(angle), wi = Mathf.Sin(angle);
                int half = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1f, ci = 0f;
                    for (int j = 0; j < half; j++)
                    {
                        int u = i + j, v = i + j + half;
                        float vr = real[v] * cr - imag[v] * ci;
                        float vi = real[v] * ci + imag[v] * cr;
                        real[v] = real[u] - vr; imag[v] = imag[u] - vi;
                        real[u] += vr; imag[u] += vi;
                        float ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }

        private static float[] ApplyWindow(float[] samples, LegacyWindow type)
        {
            int n = samples.Length;
            float[] o = new float[n];
            float nm1 = n - 1;
            for (int i = 0; i < n; i++)
            {
                float w;
                switch (type)
                {
                    case LegacyWindow.Hanning: w = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / nm1)); break;
                    case LegacyWindow.HannPeriodic: w = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / n); break;
                    case LegacyWindow.Hamming: w = 0.54f - 0.46f * Mathf.Cos(2f * Mathf.PI * i / nm1); break;
                    case LegacyWindow.BlackmanHarris:
                        w = 0.35875f - 0.48829f * Mathf.Cos(2f * Mathf.PI * i / nm1) + 0.14128f * Mathf.Cos(4f * Mathf.PI * i / nm1)
                            - 0.01168f * Mathf.Cos(6f * Mathf.PI * i / nm1);
                        break;
                    default: w = 1f; break;
                }
                o[i] = samples[i] * w;
            }
            return o;
        }
    }

    public sealed class LegacyResult
    {
        public float hopTime, avgEnergy, maxEnergy, bpm;
        public float[] energy, centroid, flux, flatness, chroma, voice, onsets;
        public float[][] bands, visual;
        public int frames;
    }

    /// <summary>Port of AudioPreAnalyzer's cache-miss FFT loop + DetectOnsets/EstimateBPM (no tagging,
    /// no coroutine yields, no derived analyzers) — the "before" of the benchmark.</summary>
    public static class LegacyAnalyzer
    {
        public static readonly float[,] Bands = { { 20, 60 }, { 60, 250 }, { 250, 500 }, { 500, 2000 }, { 2000, 4000 }, { 4000, 20000 } };

        public static LegacyResult Analyze(float[] mono, int sampleRate, int spectrumSize = 1024, int visualBandCount = 24)
        {
            int windowSize = Mathf.NextPowerOfTwo(spectrumSize);
            int hopSize = windowSize / 2;
            int numFrames = Mathf.Max(0, (mono.Length - windowSize) / hopSize);
            float hopTime = (float)hopSize / sampleRate;
            int numBands = Bands.GetLength(0);
            var vis = BuildLogBands(visualBandCount, 20f, sampleRate * 0.5f);

            var R = new LegacyResult { hopTime = hopTime, frames = numFrames };
            R.energy = new float[numFrames]; R.centroid = new float[numFrames]; R.flux = new float[numFrames];
            R.bands = new float[numBands][]; for (int b = 0; b < numBands; b++) R.bands[b] = new float[numFrames];
            R.visual = new float[visualBandCount][]; for (int b = 0; b < visualBandCount; b++) R.visual[b] = new float[numFrames];
            R.flatness = new float[numFrames]; R.chroma = new float[numFrames * 12]; R.voice = new float[numFrames];
            float[] chromaBuf = new float[12];
            float[] prev = null;

            for (int f = 0; f < numFrames; f++)
            {
                float[] chunk = new float[windowSize];
                Array.Copy(mono, f * hopSize, chunk, 0, windowSize);
                float rms = 0f;
                for (int i = 0; i < windowSize; i++) rms += chunk[i] * chunk[i];
                R.energy[f] = Mathf.Sqrt(rms / windowSize);
                float[] spectrum = LegacyFFT.Compute(chunk, LegacyWindow.BlackmanHarris);
                float wSum = 0f, magSum = 0f;
                for (int i = 0; i < spectrum.Length; i++) { float freq = (float)i * sampleRate / windowSize; wSum += freq * spectrum[i]; magSum += spectrum[i]; }
                R.centroid[f] = magSum > 0f ? wSum / magSum : 0f;
                if (prev != null)
                {
                    float flux = 0f;
                    for (int i = 0; i < spectrum.Length; i++) { float d = spectrum[i] - prev[i]; if (d > 0f) flux += d; }
                    R.flux[f] = flux;
                }
                prev = spectrum;
                for (int b = 0; b < numBands; b++) R.bands[b][f] = BandEnergy(spectrum, Bands[b, 0], Bands[b, 1], sampleRate, windowSize);
                for (int b = 0; b < visualBandCount; b++) R.visual[b][f] = BandEnergy(spectrum, vis[b, 0], vis[b, 1], sampleRate, windowSize);
                float flat = Flatness(spectrum);
                R.flatness[f] = flat;
                Chroma(spectrum, sampleRate, windowSize, chromaBuf);
                Array.Copy(chromaBuf, 0, R.chroma, f * 12, 12);
                R.voice[f] = Voice(spectrum, sampleRate, windowSize, flat);
            }
            float avg = 0f, max = 0f;
            for (int f = 0; f < numFrames; f++) { avg += R.energy[f]; if (R.energy[f] > max) max = R.energy[f]; }
            R.avgEnergy = avg / Mathf.Max(numFrames, 1); R.maxEnergy = max;
            R.onsets = DetectOnsets(R.flux, hopTime);
            R.bpm = EstimateBPM(R.onsets);
            return R;
        }

        public static float[,] BuildLogBands(int count, float minHz, float maxHz)
        {
            var b = new float[count, 2];
            float ratio = maxHz / minHz;
            for (int i = 0; i < count; i++) { b[i, 0] = minHz * Mathf.Pow(ratio, (float)i / count); b[i, 1] = minHz * Mathf.Pow(ratio, (float)(i + 1) / count); }
            return b;
        }

        public static float BandEnergy(float[] spectrum, float minHz, float maxHz, int sampleRate, int windowSize)
        {
            int lo = Mathf.Clamp(Mathf.RoundToInt(minHz * windowSize / sampleRate), 0, spectrum.Length - 1);
            int hi = Mathf.Clamp(Mathf.RoundToInt(maxHz * windowSize / sampleRate), 0, spectrum.Length - 1);
            if (hi <= lo) return spectrum[lo];
            float sum = 0f;
            for (int i = lo; i <= hi; i++) sum += spectrum[i];
            return sum / (hi - lo + 1);
        }

        public static float Flatness(float[] spectrum)
        {
            int n = spectrum.Length; float logSum = 0f, linSum = 0f;
            for (int i = 0; i < n; i++) { float v = Mathf.Max(spectrum[i], 1e-10f); logSum += Mathf.Log(v); linSum += v; }
            float geo = Mathf.Exp(logSum / n), ari = linSum / n;
            return ari > 0f ? Mathf.Clamp01(geo / ari) : 0f;
        }

        public static void Chroma(float[] spectrum, int sampleRate, int windowSize, float[] o)
        {
            const float C1 = 32.70f;
            for (int c = 0; c < 12; c++) o[c] = 0f;
            for (int i = 1; i < spectrum.Length; i++)
            {
                float freq = (float)i * sampleRate / windowSize;
                if (freq < 65f || freq > 5000f) continue;
                float midi = 12f * Mathf.Log(freq / C1, 2f);
                int c = ((int)Mathf.Round(midi) % 12 + 12) % 12;
                o[c] += spectrum[i];
            }
            float total = 0f; for (int c = 0; c < 12; c++) total += o[c];
            if (total > 0f) for (int c = 0; c < 12; c++) o[c] /= total;
        }

        public static float Voice(float[] spectrum, int sampleRate, int windowSize, float flat)
        {
            int lo = Mathf.Max(1, Mathf.RoundToInt(300f * windowSize / sampleRate));
            int hi = Mathf.Min(spectrum.Length - 1, Mathf.RoundToInt(3400f * windowSize / sampleRate));
            float v = 0f, t = 0f;
            for (int i = 1; i < spectrum.Length; i++) { t += spectrum[i]; if (i >= lo && i <= hi) v += spectrum[i]; }
            float ratio = t > 0f ? v / t : 0f;
            return Mathf.Clamp01(ratio * (1f - flat * 0.6f) * 2.2f);
        }

        public static float[] DetectOnsets(float[] flux, float hopTime)
        {
            int halfWin = 21; var onsets = new List<float>();
            for (int i = 1; i < flux.Length - 1; i++)
            {
                int lo = Mathf.Max(0, i - halfWin), hi = Mathf.Min(flux.Length - 1, i + halfWin);
                float mean = 0f; for (int j = lo; j <= hi; j++) mean += flux[j]; mean /= (hi - lo + 1);
                float variance = 0f; for (int j = lo; j <= hi; j++) variance += (flux[j] - mean) * (flux[j] - mean);
                float std = Mathf.Sqrt(variance / (hi - lo + 1));
                if (flux[i] > mean + 1.5f * std && flux[i] > flux[i - 1] && flux[i] >= flux[i + 1]) onsets.Add(i * hopTime);
            }
            return onsets.ToArray();
        }

        public static float EstimateBPM(float[] onsets)
        {
            if (onsets.Length < 4) return 120f;
            float minPeriod = 60f / 200f, maxPeriod = 60f / 60f; int bins = 80;
            float binSize = (maxPeriod - minPeriod) / bins; int[] h = new int[bins];
            for (int i = 0; i < onsets.Length - 1; i++)
            {
                float ioi = onsets[i + 1] - onsets[i];
                for (float mult = 0.5f; mult <= 2.01f; mult += 0.5f)
                {
                    float period = ioi * mult;
                    if (period < minPeriod || period > maxPeriod) continue;
                    int b = Mathf.Clamp(Mathf.FloorToInt((period - minPeriod) / binSize), 0, bins - 1);
                    h[b]++;
                }
            }
            int peak = 0; for (int i = 1; i < bins; i++) if (h[i] > h[peak]) peak = i;
            float dom = minPeriod + (peak + 0.5f) * binSize;
            return Mathf.Round(60f / dom);
        }
    }
}
