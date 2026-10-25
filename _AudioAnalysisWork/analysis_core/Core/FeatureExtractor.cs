using System;
using System.Threading;

namespace MusicGame.Audio.Analysis
{
    /// <summary>Overlap-weighted average of spectrum bins inside a Hz band (DC excluded).</summary>
    public sealed class BandTable
    {
        public readonly int     start;
        public readonly float[] weights;
        public readonly float   invWeightSum;
        public readonly bool    useLongSpectrum;

        public BandTable(int start, float[] weights, bool useLong)
        {
            this.start = start; this.weights = weights; useLongSpectrum = useLong;
            float s = 0f;
            for (int i = 0; i < weights.Length; i++) s += weights[i];
            invWeightSum = s > 0f ? 1f / s : 0f;
        }

        /// <summary>Bin k covers [(k-½)Δ, (k+½)Δ]; its weight is its overlap (Hz) with [minHz, maxHz].
        /// Bins 1..maxBin only (DC never contributes). A band narrower than every overlap still gets
        /// its nearest bin, so it is never empty.</summary>
        public static BandTable Build(float minHz, float maxHz, float binHz, int maxBin, bool useLong)
        {
            int lo = Math.Max(1, (int)Math.Floor(minHz / binHz + 0.5));
            int hi = Math.Min(maxBin, (int)Math.Ceiling(maxHz / binHz - 0.5));
            if (hi < lo)
            {
                int k = Math.Max(1, Math.Min(maxBin, (int)Math.Round(0.5 * (minHz + maxHz) / binHz)));
                return new BandTable(k, new[] { 1f }, useLong);
            }
            var w = new float[hi - lo + 1];
            for (int k = lo; k <= hi; k++)
            {
                double a = Math.Max(minHz, (k - 0.5) * binHz);
                double b = Math.Min(maxHz, (k + 0.5) * binHz);
                w[k - lo] = (float)Math.Max(0.0, b - a);
            }
            return new BandTable(lo, w, useLong);
        }

        public float Evaluate(float[] spectrum)
        {
            float s = 0f;
            for (int i = 0; i < weights.Length; i++) s += weights[i] * spectrum[start + i];
            return s * invWeightSum;
        }
    }

    /// <summary>Sample-rate–resolved sizes and tables of the frame-feature pass (immutable, shared).</summary>
    public sealed class FeatureLayout
    {
        public readonly int   sampleRate;
        public readonly int   windowLength;
        public readonly int   fftSize;
        public readonly int   hop;
        public readonly float hopTime;
        public readonly int   specBins;      // fftSize/2 (legacy convention: no Nyquist bin)
        public readonly float binHz;
        public readonly float[] window;
        public readonly RealFftPlan plan;

        public readonly int     longWindowLength; // 0 = disabled (in samples of the long-window signal)
        public readonly int     longFactor;       // decimation factor of the signal the long window reads
        public readonly int     longFftSize;
        public readonly float[] longWindow;
        public readonly RealFftPlan longPlan;

        public readonly BandTable[] classBands;
        public readonly BandTable[] visualBands;
        public readonly BandSpec[]  classSpecs;
        public readonly BandSpec[]  visualSpecs;

        public readonly int[]   chromaClass;  // per bin, -1 = ignored
        public readonly float[] binFreq;      // legacy float frequency of each bin
        public readonly int     voiceLo, voiceHi;

        public readonly bool flatness, chroma, voice;

        public FeatureLayout(AnalysisParams p, int sampleRate)
        {
            this.sampleRate = sampleRate;
            windowLength = Math.Max(16, (int)Math.Round(p.FeatureWindowSeconds * sampleRate));
            fftSize      = WindowTables.NextPowerOfTwo(windowLength);
            hop          = Math.Max(1, (int)Math.Round(p.FeatureHopSeconds * sampleRate));
            hopTime      = (float)hop / sampleRate;
            specBins     = fftSize / 2;
            binHz        = (float)sampleRate / fftSize;
            window       = WindowTables.Get(WindowKind.BlackmanHarris, windowLength);
            plan         = RealFftPlan.Get(fftSize);
            flatness = p.ComputeFlatness; chroma = p.ComputeChroma; voice = p.ComputeVoice;

            // Classification bands: main spectrum only (keeps their time resolution).
            int nb = p.ClassificationBandCount;
            classBands = new BandTable[nb];
            classSpecs = new BandSpec[nb];
            for (int b = 0; b < nb; b++)
            {
                var spec = p.GetClassificationBand(b);
                classSpecs[b] = spec;
                classBands[b] = BandTable.Build(spec.minHz, spec.maxHz, binHz, specBins - 1, false);
            }

            // Visual bands: same log layout as legacy BuildLogBands (float math reproduced).
            int nv = p.VisualBandCount;
            float vMin = p.VisualMinHz;
            float vMax = p.VisualMaxHz > 0f ? Math.Min(p.VisualMaxHz, sampleRate * 0.5f) : sampleRate * 0.5f;
            float ratio = vMax / vMin;
            visualSpecs = new BandSpec[nv];
            for (int i = 0; i < nv; i++)
            {
                float lo = vMin * (float)Math.Pow(ratio, (float)i / nv);
                float hi = vMin * (float)Math.Pow(ratio, (float)(i + 1) / nv);
                visualSpecs[i] = new BandSpec("VB" + i, lo, hi);
            }

            bool wantLong = p.LowBandLongWindowSeconds > 0 && p.LowBandCrossoverHz > 0f;
            bool anyLong = false;
            if (wantLong)
                for (int i = 0; i < nv; i++) if (visualSpecs[i].maxHz <= p.LowBandCrossoverHz) anyLong = true;
            if (anyLong)
            {
                longFactor       = p.LowRateFactor(sampleRate);
                longWindowLength = Math.Max(16, (int)Math.Round(p.LowBandLongWindowSeconds * sampleRate / longFactor));
                longFftSize      = WindowTables.NextPowerOfTwo(longWindowLength);
                longWindow       = WindowTables.Get(WindowKind.BlackmanHarris, longWindowLength);
                longPlan         = RealFftPlan.Get(longFftSize);
            }
            float longBinHz = anyLong ? (float)((double)sampleRate / longFactor / longFftSize) : 0f;
            visualBands = new BandTable[nv];
            for (int i = 0; i < nv; i++)
            {
                bool useLong = anyLong && visualSpecs[i].maxHz <= p.LowBandCrossoverHz;
                visualBands[i] = useLong
                    ? BandTable.Build(visualSpecs[i].minHz, visualSpecs[i].maxHz, longBinHz, longFftSize / 2 - 1, true)
                    : BandTable.Build(visualSpecs[i].minHz, visualSpecs[i].maxHz, binHz, specBins - 1, false);
            }

            // Legacy chroma / voice / centroid bin tables, same float arithmetic as AdvancedFFTFeatures.
            binFreq = new float[specBins];
            chromaClass = new int[specBins];
            const float C1 = 32.70f;
            for (int i = 0; i < specBins; i++)
            {
                float freq = (float)i * sampleRate / fftSize;
                binFreq[i] = freq;
                chromaClass[i] = -1;
                if (i == 0 || freq < 65f || freq > 5000f) continue;
                float midi = 12f * (float)Math.Log(freq / C1, 2f);
                chromaClass[i] = ((int)(float)Math.Round(midi) % 12 + 12) % 12;
            }
            voiceLo = Math.Max(1, (int)Math.Round(300f * fftSize / sampleRate));
            voiceHi = Math.Min(specBins - 1, (int)Math.Round(3400f * fftSize / sampleRate));
        }

        /// <summary>Center-aligned frame count: frames centred on 0, hop, 2·hop … ≤ last sample.</summary>
        public int FrameCount(int sampleCount) => sampleCount <= 0 ? 0 : (sampleCount - 1) / hop + 1;
    }

    public static class FrameMath
    {
        /// <summary>Copies the window centred on `center` (zero outside the signal), multiplies by the
        /// window table and zero-pads to dst.Length. Returns Σ x² of the RAW (unwindowed) samples.</summary>
        public static float ExtractWindowed(float[] x, int center, float[] window, float[] dst)
        {
            int len = window.Length;
            int start = center - len / 2;
            int n = x.Length;
            float sumSq = 0f;
            if (start >= 0 && start + len <= n)
            {
                for (int i = 0; i < len; i++)
                {
                    float v = x[start + i];
                    sumSq += v * v;
                    dst[i] = v * window[i];
                }
            }
            else
            {
                for (int i = 0; i < len; i++)
                {
                    int idx = start + i;
                    float v = (idx >= 0 && idx < n) ? x[idx] : 0f;
                    sumSq += v * v;
                    dst[i] = v * window[i];
                }
            }
            for (int i = len; i < dst.Length; i++) dst[i] = 0f;
            return sumSq;
        }
    }

    /// <summary>
    /// Frame-feature pass: energy, centroid, legacy flux, flatness, classification bands, visual
    /// bands, chroma, raw voice proxy. Formulas and scales follow the legacy AudioPreAnalyzer /
    /// AdvancedFFTFeatures (magnitude/windowLength, Blackman-Harris, bins 0..N/2-1) so consumers keep
    /// working; the differences are documented: center-aligned frames, DC excluded from bands,
    /// overlap-weighted band edges, long-window lowest visual bands.
    /// </summary>
    public static class FeatureExtractor
    {
        private sealed class Scratch
        {
            public float[] frame, re, im, mag, prevMag, longFrame, longRe, longIm, longMag, chroma;
            public FftScratch fft, longFft;
        }

        /// <param name="low">decimated signal (factor == L.longFactor) when the layout uses the long window; else null.</param>
        public static void Compute(float[] mono, LowRateSignal low, FeatureLayout L, AnalysisResult r, AnalysisRunOptions opt, Action<float> progress)
        {
            if (L.longWindowLength > 0 && (low == null || low.factor != L.longFactor))
                throw new ArgumentException("FeatureExtractor: low-rate signal with factor " + L.longFactor + " required");
            int n = L.FrameCount(mono.Length);
            r.frameCount = n;
            r.hopTime = L.hopTime;
            r.energy = new float[n];
            r.centroid = new float[n];
            r.flux = new float[n];
            r.flatness = L.flatness ? new float[n] : null;
            r.voice = L.voice ? new float[n] : null;
            r.chroma = L.chroma ? new float[n * 12] : null;
            r.bands = new float[L.classBands.Length][];
            for (int b = 0; b < r.bands.Length; b++) r.bands[b] = new float[n];
            r.visualBands = new float[L.visualBands.Length][];
            for (int b = 0; b < r.visualBands.Length; b++) r.visualBands[b] = new float[n];
            r.classificationBandSpecs = (BandSpec[])L.classSpecs.Clone();
            r.visualBandSpecs = (BandSpec[])L.visualSpecs.Clone();
            if (n == 0) return;

            int chunk = Math.Max(16, opt?.chunkFrames ?? 1024);
            int chunks = (n + chunk - 1) / chunk;
            int done = 0;

            ParallelChunks.Run(n, chunk, opt, () => NewScratch(L), (a, b, s) => ComputeRange(mono, low, L, r, a, b, s),
                c => { int d = Interlocked.Increment(ref done); progress?.Invoke((float)d / chunks); });
        }

        private static Scratch NewScratch(FeatureLayout L)
        {
            var s = new Scratch
            {
                frame = new float[L.fftSize], re = new float[L.fftSize / 2 + 1], im = new float[L.fftSize / 2 + 1],
                mag = new float[L.specBins], prevMag = new float[L.specBins], chroma = new float[12],
                fft = new FftScratch(L.fftSize),
            };
            if (L.longWindowLength > 0)
            {
                s.longFrame = new float[L.longFftSize];
                s.longRe = new float[L.longFftSize / 2 + 1];
                s.longIm = new float[L.longFftSize / 2 + 1];
                s.longMag = new float[L.longFftSize / 2];
                s.longFft = new FftScratch(L.longFftSize);
            }
            return s;
        }

        private static float Spectrum(float[] mono, FeatureLayout L, int frame, Scratch s, float[] magOut)
        {
            float sumSq = FrameMath.ExtractWindowed(mono, frame * L.hop, L.window, s.frame);
            L.plan.Forward(s.frame, s.re, s.im, s.fft);
            float inv = 1f / L.windowLength;
            for (int i = 0; i < L.specBins; i++)
                magOut[i] = (float)Math.Sqrt(s.re[i] * s.re[i] + s.im[i] * s.im[i]) * inv;
            return sumSq;
        }

        private static void ComputeRange(float[] mono, LowRateSignal low, FeatureLayout L, AnalysisResult r, int f0, int f1, Scratch s)
        {
            // Flux needs the previous frame's spectrum: recompute it so the chunk is self-contained
            // (identical to the sequential value → chunking never changes results).
            bool havePrev = f0 > 0;
            if (havePrev) Spectrum(mono, L, f0 - 1, s, s.prevMag);

            int nb = L.specBins;
            for (int f = f0; f < f1; f++)
            {
                float[] mag = s.mag;
                float sumSq = Spectrum(mono, L, f, s, mag);
                r.energy[f] = (float)Math.Sqrt(sumSq / L.windowLength);

                float wSum = 0f, magSum = 0f;
                for (int i = 0; i < nb; i++) { wSum += L.binFreq[i] * mag[i]; magSum += mag[i]; }
                r.centroid[f] = magSum > 0f ? wSum / magSum : 0f;

                if (havePrev)
                {
                    float flux = 0f;
                    float[] pm = s.prevMag;
                    for (int i = 0; i < nb; i++) { float d = mag[i] - pm[i]; if (d > 0f) flux += d; }
                    r.flux[f] = flux;
                }

                for (int b = 0; b < L.classBands.Length; b++) r.bands[b][f] = L.classBands[b].Evaluate(mag);

                if (L.longWindowLength > 0)
                {
                    int lc = (int)(((long)f * L.hop + L.longFactor / 2) / L.longFactor); // centre in the decimated domain
                    FrameMath.ExtractWindowed(low.samples, lc, L.longWindow, s.longFrame);
                    L.longPlan.Forward(s.longFrame, s.longRe, s.longIm, s.longFft);
                    float inv = 1f / L.longWindowLength;
                    // Only the bins the long bands need (low end) — the rest is never read.
                    int need = 0;
                    for (int b = 0; b < L.visualBands.Length; b++)
                        if (L.visualBands[b].useLongSpectrum) need = Math.Max(need, L.visualBands[b].start + L.visualBands[b].weights.Length);
                    for (int i = 0; i < need; i++)
                        s.longMag[i] = (float)Math.Sqrt(s.longRe[i] * s.longRe[i] + s.longIm[i] * s.longIm[i]) * inv;
                }
                for (int b = 0; b < L.visualBands.Length; b++)
                {
                    var t = L.visualBands[b];
                    r.visualBands[b][f] = t.Evaluate(t.useLongSpectrum ? s.longMag : mag);
                }

                float flat = 0.5f;
                if (L.flatness)
                {
                    // Legacy AdvancedFFTFeatures.SpectralFlatness (natural log, 1e-10 floor, all bins incl. DC).
                    float logSum = 0f, linSum = 0f;
                    for (int i = 0; i < nb; i++)
                    {
                        float v = mag[i] > 1e-10f ? mag[i] : 1e-10f;
                        logSum += (float)Math.Log(v);
                        linSum += v;
                    }
                    float geo = (float)Math.Exp(logSum / nb);
                    float ari = linSum / nb;
                    flat = ari > 0f ? Clamp01(geo / ari) : 0f;
                    r.flatness[f] = flat;
                }

                if (L.chroma)
                {
                    float[] c = s.chroma;
                    for (int k = 0; k < 12; k++) c[k] = 0f;
                    for (int i = 1; i < nb; i++) { int cc = L.chromaClass[i]; if (cc >= 0) c[cc] += mag[i]; }
                    float total = 0f;
                    for (int k = 0; k < 12; k++) total += c[k];
                    int o = f * 12;
                    for (int k = 0; k < 12; k++) r.chroma[o + k] = total > 0f ? c[k] / total : 0f;
                }

                if (L.voice)
                {
                    float voiceE = 0f, totalE = 0f;
                    for (int i = 1; i < nb; i++)
                    {
                        totalE += mag[i];
                        if (i >= L.voiceLo && i <= L.voiceHi) voiceE += mag[i];
                    }
                    float ratio = totalE > 0f ? voiceE / totalE : 0f;
                    float tonal = 1f - flat * 0.6f;
                    r.voice[f] = Clamp01(ratio * tonal * 2.2f);
                }

                // Current spectrum becomes previous (swap buffers, no copy).
                var tmp = s.prevMag; s.prevMag = s.mag; s.mag = tmp;
                havePrev = true;
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
