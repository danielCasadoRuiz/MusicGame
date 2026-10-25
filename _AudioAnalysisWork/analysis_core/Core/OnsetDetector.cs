using System;
using System.Collections.Generic;
using System.Threading;

namespace MusicGame.Audio.Analysis
{
    /// <summary>Sample-rate–resolved layout of the onset pass (immutable, shared by workers).</summary>
    public sealed class OnsetLayout
    {
        public sealed class Resolution
        {
            public int windowLength, fftSize;
            public int factor;               // 1 = full-rate signal, else the low-rate (decimated) signal
            public float[] window;
            public RealFftPlan plan;
            public float ampNorm;            // 2 / Σw → a full-scale sine reads ~1
            public int binLo, binHi;         // union of the groups' bins incl. ±1 neighbours (max filter)
        }

        public readonly int    sampleRate;
        public readonly int    hop;
        public readonly float  hopTime;
        public readonly int    lag;
        public readonly float  gamma;
        public readonly Resolution[] resolutions;
        public readonly int[]  groupRes;     // group → resolution index
        public readonly int[]  groupLo, groupHi; // group bin range (inclusive) in its resolution
        public readonly OnsetGroupSpec[] groups;
        public readonly int    lowGroup, midGroup, highGroup;
        public readonly int    refLo, refHi; // hi-hat reference band, bins of the Mid group's resolution
        public readonly float  refWidthDb, highWidthDb; // 10·log10(bandwidth Hz) of the reference band and of the High group

        public readonly int lowFactor; // decimation factor available for low groups (1 = none)

        public OnsetLayout(AnalysisParams p, int sampleRate)
        {
            this.sampleRate = sampleRate;
            hop     = Math.Max(1, (int)Math.Round(p.OnsetHopSeconds * sampleRate));
            hopTime = (float)hop / sampleRate;
            lag     = Math.Max(1, (int)Math.Round(p.OnsetDiffLagSeconds / ((double)hop / sampleRate)));
            gamma   = p.LogCompressionGamma;

            int ng = p.OnsetGroupCount;
            groups = new OnsetGroupSpec[ng];
            for (int g = 0; g < ng; g++) groups[g] = p.GetOnsetGroup(g);
            lowGroup = p.FindOnsetGroup(OnsetBandRole.Low);
            midGroup = p.FindOnsetGroup(OnsetBandRole.Mid);
            highGroup = p.FindOnsetGroup(OnsetBandRole.High);

            // A group whose band sits well inside the low-rate signal's passband reads that signal
            // (same window duration, factor× fewer samples per FFT).
            lowFactor = p.LowRateFactor(sampleRate);
            double lowRate = (double)sampleRate / lowFactor;
            var keys = new List<long>();
            groupRes = new int[ng];
            for (int g = 0; g < ng; g++)
            {
                int fac = (lowFactor > 1 && groups[g].maxHz <= 0.35 * lowRate) ? lowFactor : 1;
                int len = Math.Max(16, (int)Math.Round(groups[g].windowSeconds * sampleRate / fac));
                long key = ((long)fac << 32) | (uint)len;
                int idx = keys.IndexOf(key);
                if (idx < 0) { keys.Add(key); idx = keys.Count - 1; }
                groupRes[g] = idx;
            }
            resolutions = new Resolution[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                int len = (int)(keys[i] & 0xFFFFFFFF), fac = (int)(keys[i] >> 32);
                var r = new Resolution { windowLength = len, factor = fac, fftSize = WindowTables.NextPowerOfTwo(len) };
                r.window = WindowTables.Get(WindowKind.Hann, r.windowLength);
                r.plan = RealFftPlan.Get(r.fftSize);
                double sw = 0; for (int k = 0; k < r.window.Length; k++) sw += r.window[k];
                r.ampNorm = (float)(2.0 / sw);
                r.binLo = int.MaxValue; r.binHi = 0;
                resolutions[i] = r;
            }
            groupLo = new int[ng]; groupHi = new int[ng];
            for (int g = 0; g < ng; g++)
            {
                var r = resolutions[groupRes[g]];
                float binHz = (float)((double)sampleRate / r.factor / r.fftSize);
                int maxBin = r.fftSize / 2 - 1;
                int lo = Math.Max(1, (int)Math.Ceiling(groups[g].minHz / binHz));
                int hi = Math.Min(maxBin, (int)Math.Floor(groups[g].maxHz / binHz));
                if (hi < lo) { int k = Math.Max(1, Math.Min(maxBin, (int)Math.Round(0.5 * (groups[g].minHz + groups[g].maxHz) / binHz))); lo = hi = k; }
                groupLo[g] = lo; groupHi[g] = hi;
                r.binLo = Math.Min(r.binLo, Math.Max(0, lo - 1));
                r.binHi = Math.Max(r.binHi, Math.Min(r.fftSize / 2, hi + 1));
            }
            {
                var r = resolutions[groupRes[midGroup]];
                float binHz = (float)((double)sampleRate / r.factor / r.fftSize);
                refLo = Math.Max(r.binLo, (int)Math.Ceiling(p.HatReferenceMinHz / binHz));
                refHi = Math.Min(r.binHi, (int)Math.Floor(p.HatReferenceMaxHz / binHz));
                if (refHi < refLo) refHi = refLo;
                // Bandwidths (dB re 1 Hz) so the hi-hat test compares power DENSITIES: the High band
                // (≈11 kHz) is ~4.4× wider than the reference (≈2.5 kHz), so comparing totals made flat
                // (snare-like) noise look +6.4 dB "high-heavy" from bandwidth alone.
                refWidthDb = 10f * (float)Math.Log10((refHi - refLo + 1) * (double)binHz);
                var rh = resolutions[groupRes[highGroup]];
                double hBin = (double)sampleRate / rh.factor / rh.fftSize;
                highWidthDb = 10f * (float)Math.Log10((groupHi[highGroup] - groupLo[highGroup] + 1) * hBin);
            }
        }

        public int FrameCount(int sampleCount) => sampleCount <= 0 ? 0 : (sampleCount - 1) / hop + 1;
    }

    /// <summary>Per-frame onset evidence of every group (kept for diagnostics / tempo).</summary>
    public sealed class OnsetFunctions
    {
        public int frames;
        public float hopTime;
        public float[][] odf;     // log-compressed positive flux, mean per bin
        public float[][] levelDb; // total power of the group's bins, dB (a full-scale sine ≈ +1.8 dB)
        public float[][] cover;   // fraction of the group's bins whose log magnitude jumped (broadband/noisy attack → high)
        public float[]   refDb;   // total power of the hi-hat reference band (dB)
    }

    /// <summary>
    /// Band-group onset detection:
    ///  1. evidence  — per group, log-compressed positive spectral flux (with a ±1-bin max filter on
    ///     the reference frame so vibrato/slow glides do not fire), adaptive threshold (local mean of
    ///     the recent context × ratio + delta), local-max peak picking, noise-floor gate, duplicate
    ///     suppression per group, parabolic sub-frame refinement, calibrated latency removal;
    ///  2. families  — group peaks within the merge window become one event; heuristic, multi-label
    ///     kick/snare/hi-hat confidences from which groups fired, how noisy the new energy is and how
    ///     long it rings; nothing convincing → Generic;
    ///  3. selection — NOT here (gameplay decides). Only per-family duplicate intervals are applied.
    /// </summary>
    public static class OnsetDetector
    {
        private sealed class ResState
        {
            public float[] frame, re, im;
            public FftScratch fft;
            public float[][] logRing, powRing; // lag+1 slots of the needed bins
        }

        private sealed class Scratch { public ResState[] res; }

        // ── Pass 1: per-frame evidence (parallel, chunk-independent) ─────────────────────────────

        public static bool NeedsLowRate(OnsetLayout L)
        {
            foreach (var r in L.resolutions) if (r.factor > 1) return true;
            return false;
        }

        /// <param name="low">decimated signal with factor L.lowFactor if any group uses it (see NeedsLowRate).</param>
        public static OnsetFunctions ComputeFunctions(float[] mono, LowRateSignal low, OnsetLayout L, AnalysisRunOptions opt, Action<float> progress)
        {
            if (NeedsLowRate(L) && (low == null || low.factor != L.lowFactor))
                throw new ArgumentException("OnsetDetector: low-rate signal with factor " + L.lowFactor + " required");
            int n = L.FrameCount(mono.Length);
            int ng = L.groups.Length;
            var F = new OnsetFunctions { frames = n, hopTime = L.hopTime, odf = new float[ng][], levelDb = new float[ng][], cover = new float[ng][] };
            for (int g = 0; g < ng; g++) { F.odf[g] = new float[n]; F.levelDb[g] = new float[n]; F.cover[g] = new float[n]; }
            F.refDb = new float[n];
            if (n == 0) return F;

            int chunk = Math.Max(32, opt?.chunkFrames ?? 1024) * 2; // onset hop is finer → bigger chunks
            int chunks = (n + chunk - 1) / chunk;
            int done = 0;
            ParallelChunks.Run(n, chunk, opt, () => NewScratch(L), (a, b, s) => ComputeRange(mono, low, L, F, a, b, s),
                c => { int d = Interlocked.Increment(ref done); progress?.Invoke((float)d / chunks); });
            return F;
        }

        private static Scratch NewScratch(OnsetLayout L)
        {
            var s = new Scratch { res = new ResState[L.resolutions.Length] };
            for (int i = 0; i < L.resolutions.Length; i++)
            {
                var r = L.resolutions[i];
                var st = new ResState
                {
                    frame = new float[r.fftSize], re = new float[r.fftSize / 2 + 1], im = new float[r.fftSize / 2 + 1],
                    fft = new FftScratch(r.fftSize), logRing = new float[L.lag + 1][], powRing = new float[L.lag + 1][],
                };
                for (int k = 0; k <= L.lag; k++) { st.logRing[k] = new float[r.fftSize / 2 + 1]; st.powRing[k] = new float[r.fftSize / 2 + 1]; }
                s.res[i] = st;
            }
            return s;
        }

        /// <summary>Power ratio (6 dB) a bin must gain to count as "hit" by the attack.</summary>
        private const float CoverJumpRatio = 4f;
        /// <summary>Only bins within 40 dB of the frame's strongest bin of the group are considered.</summary>
        private const float CoverRangeRatio = 1e-4f;

        private static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        private static void SpectrumInto(float[] mono, LowRateSignal low, OnsetLayout L, int ri, int frame, ResState st, int slot)
        {
            var r = L.resolutions[ri];
            if (r.factor == 1) FrameMath.ExtractWindowed(mono, frame * L.hop, r.window, st.frame);
            else
            {
                long c = (long)frame * L.hop;
                int lc = (int)(c >= 0 ? (c + r.factor / 2) / r.factor : -((-c + r.factor / 2) / r.factor));
                FrameMath.ExtractWindowed(low.samples, lc, r.window, st.frame);
            }
            r.plan.Forward(st.frame, st.re, st.im, st.fft);
            float[] lg = st.logRing[slot], pw = st.powRing[slot];
            float norm2 = r.ampNorm * r.ampNorm, gamma = L.gamma;
            for (int k = r.binLo; k <= r.binHi; k++)
            {
                float p = (st.re[k] * st.re[k] + st.im[k] * st.im[k]) * norm2;
                pw[k] = p;
                lg[k] = (float)Math.Log(1.0 + gamma * Math.Sqrt(p));
            }
        }

        private static void ComputeRange(float[] mono, LowRateSignal low, OnsetLayout L, OnsetFunctions F, int f0, int f1, Scratch s)
        {
            int slots = L.lag + 1;
            // History frames f0-lag .. f0-1 (negative indices are legitimate, mostly silent, frames).
            for (int ri = 0; ri < L.resolutions.Length; ri++)
                for (int h = f0 - L.lag; h < f0; h++) SpectrumInto(mono, low, L, ri, h, s.res[ri], Mod(h, slots));

            for (int f = f0; f < f1; f++)
            {
                int cur = Mod(f, slots), prev = Mod(f - L.lag, slots);
                for (int ri = 0; ri < L.resolutions.Length; ri++) SpectrumInto(mono, low, L, ri, f, s.res[ri], cur);

                for (int g = 0; g < L.groups.Length; g++)
                {
                    var st = s.res[L.groupRes[g]];
                    float[] lc = st.logRing[cur], lp = st.logRing[prev], pc = st.powRing[cur], pp = st.powRing[prev];
                    int lo = L.groupLo[g], hi = L.groupHi[g];
                    float odf = 0f; double powSum = 0; float pMax = 0f;
                    for (int k = lo; k <= hi; k++)
                    {
                        float refv = lp[k];
                        if (lp[k - 1] > refv) refv = lp[k - 1];
                        if (lp[k + 1] > refv) refv = lp[k + 1];
                        float d = lc[k] - refv;
                        if (d > 0f) odf += d;
                        powSum += pc[k];
                        if (pc[k] > pMax) pMax = pc[k];
                    }
                    // Coverage: bins that carry part of the frame's spectrum (within 40 dB of its strongest
                    // bin) AND gained ≥ 6 dB over the reference frame (±1-bin max). Level independent.
                    int jumped = 0;
                    float floorP = pMax * CoverRangeRatio;
                    for (int k = lo; k <= hi; k++)
                    {
                        float pk = pc[k];
                        if (pk <= floorP) continue;
                        float refp = pp[k];
                        if (pp[k - 1] > refp) refp = pp[k - 1];
                        if (pp[k + 1] > refp) refp = pp[k + 1];
                        if (pk > CoverJumpRatio * refp) jumped++;
                    }
                    int K = hi - lo + 1;
                    F.odf[g][f] = odf / K;
                    F.levelDb[g][f] = (float)(10.0 * Math.Log10(powSum + 1e-20));
                    F.cover[g][f] = (float)jumped / K;
                    if (g == L.midGroup)
                    {
                        double rs = 0;
                        for (int k = L.refLo; k <= L.refHi; k++) rs += pc[k];
                        F.refDb[f] = (float)(10.0 * Math.Log10(rs + 1e-20));
                    }
                }
            }
        }

        // ── Pass 2: peak picking per group (sequential, deterministic) ───────────────────────────

        private struct GroupPeak
        {
            public int group, frame;
            public double time;
            public float odf, thr, salience;
        }

        /// <summary>Raw (uncalibrated) group peak times, exposed for calibration/diagnostics.</summary>
        public struct RawPeak { public int group; public double rawTime; public float salience; }

        /// <summary>Per-event classification evidence (diagnostics only, never persisted).</summary>
        public struct Evidence
        {
            public float time;
            public bool lowP, midP, highP;
            public float lowSal, midSal, highSal;
            public float lowRing, midRing, highRing;   // seconds, -1 = not measurable
            public float lowInc, midInc, highInc;      // dB of added power
            public float lowCov, midCov, highCov;
            public float kick, snare, hat, refInc, rise;
            public override string ToString() =>
                $"t={time:F3} rise{rise:F1} ref{refInc:F0} L[{(lowP ? "P" : "-")} s{lowSal:F1} r{lowRing * 1000:F0} i{lowInc:F0} c{lowCov:F2}] M[{(midP ? "P" : "-")} s{midSal:F1} r{midRing * 1000:F0} i{midInc:F0} c{midCov:F2}] H[{(highP ? "P" : "-")} s{highSal:F1} r{highRing * 1000:F0} i{highInc:F0} c{highCov:F2}] k{kick:F2} s{snare:F2} h{hat:F2}";
        }
        [ThreadStatic] public static List<Evidence> DebugEvidence; // harness-only diagnostics sink (null in production)

        public static OnsetEvent[] Detect(OnsetFunctions F, OnsetLayout L, AnalysisParams p, CancellationToken ct, List<RawPeak> rawOut = null)
        {
            int n = F.frames, ng = L.groups.Length;
            if (n < 1) return Array.Empty<OnsetEvent>();
            double hopT = (double)L.hop / L.sampleRate;
            int preMax  = Math.Max(1, (int)Math.Round(p.PeakPreMaxSeconds / hopT));
            int postMax = Math.Max(1, (int)Math.Round(p.PeakPostMaxSeconds / hopT));
            int preAvg  = Math.Max(1, (int)Math.Round(p.PeakPreAvgSeconds / hopT));
            int postAvg = Math.Max(0, (int)Math.Round(p.PeakPostAvgSeconds / hopT));
            int gateFr  = Math.Max(1, (int)Math.Round(0.020 / hopT));

            var thr = new float[ng][];
            var peaks = new List<GroupPeak>();
            var perGroupPeakFrames = new List<int>[ng];

            for (int g = 0; g < ng; g++)
            {
                ct.ThrowIfCancellationRequested();
                var spec = L.groups[g];
                float[] o = F.odf[g], lev = F.levelDb[g];
                // Prefix sums (double) for the moving mean.
                var pre = new double[n + 1];
                for (int i = 0; i < n; i++) pre[i + 1] = pre[i] + o[i];
                var t = thr[g] = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int a = Math.Max(0, i - preAvg), b = Math.Min(n - 1, i + postAvg);
                    double mean = (pre[b + 1] - pre[a]) / (b - a + 1);
                    t[i] = (float)(spec.thresholdRatio * mean + spec.thresholdDelta);
                }

                int minIntFr = Math.Max(1, (int)Math.Round(spec.minIntervalSeconds / hopT));
                var list = new List<GroupPeak>();
                for (int i = 0; i < n; i++)
                {
                    float v = o[i];
                    float vPrev = i > 0 ? o[i - 1] : 0f, vNext = i < n - 1 ? o[i + 1] : 0f; // outside = silence
                    if (v < t[i] || v <= vPrev) continue;
                    bool isMax = true;
                    int a = Math.Max(0, i - preMax), b = Math.Min(n - 1, i + postMax);
                    for (int j = a; j <= b && isMax; j++)
                    {
                        if (j < i && o[j] >= v) isMax = false;      // strictly greater than the past
                        else if (j > i && o[j] > v) isMax = false;  // ≥ the future (plateau → first frame)
                    }
                    if (!isMax) continue;

                    float lvMax = float.NegativeInfinity;
                    for (int j = i; j <= Math.Min(n - 1, i + gateFr); j++) if (lev[j] > lvMax) lvMax = lev[j];
                    if (lvMax < spec.floorDb) continue;

                    // Parabolic refinement of the peak position.
                    float y0 = vPrev, y1 = v, y2 = vNext;
                    float den = y0 - 2f * y1 + y2;
                    double off = den < 0f ? 0.5 * (y0 - y2) / den : 0.0;
                    if (off > 0.5) off = 0.5; else if (off < -0.5) off = -0.5;

                    var gp = new GroupPeak { group = g, frame = i, time = (i + off) * hopT, odf = v, thr = t[i], salience = v / Math.Max(1e-9f, t[i]) };

                    if (list.Count > 0 && i - list[list.Count - 1].frame < minIntFr)
                    {
                        if (gp.odf > list[list.Count - 1].odf) list[list.Count - 1] = gp;
                        continue;
                    }
                    list.Add(gp);
                }
                perGroupPeakFrames[g] = new List<int>(list.Count);
                foreach (var gp in list)
                {
                    perGroupPeakFrames[g].Add(gp.frame);
                    rawOut?.Add(new RawPeak { group = g, rawTime = gp.time, salience = gp.salience });
                    var c = gp; c.time = gp.time - spec.timeOffsetSeconds;
                    peaks.Add(c);
                }
            }

            peaks.Sort((x, y) => x.time != y.time ? x.time.CompareTo(y.time) : x.group.CompareTo(y.group));

            // ── Merge simultaneous group peaks into events, classify ───────────────────────────
            var events = new List<OnsetEvent>(peaks.Count);
            var clusterIdx = new int[ng];
            int pi = 0;
            while (pi < peaks.Count)
            {
                ct.ThrowIfCancellationRequested();
                for (int g = 0; g < ng; g++) clusterIdx[g] = -1;
                double anchor = peaks[pi].time;
                int pj = pi;
                while (pj < peaks.Count && peaks[pj].time - anchor <= p.MergeWindowSeconds && clusterIdx[peaks[pj].group] < 0)
                {
                    clusterIdx[peaks[pj].group] = pj;
                    pj++;
                }
                if (Classify(peaks, clusterIdx, F, thr, perGroupPeakFrames, L, p, hopT, out var ev)) events.Add(ev);
                pi = pj;
            }

            ApplyFamilyIntervals(events, p);
            return events.ToArray();
        }

        // ── Family heuristics ─────────────────────────────────────────────────────────────────

        private static float Clamp01(double v) => v < 0 ? 0f : (v > 1 ? 1f : (float)v);
        private static float Ramp(double v, double a, double b) => Clamp01((v - a) / (b - a));
        /// <summary>1 inside [b, c], ramps 0→1 on [a, b] and 1→0 on [c, d].</summary>
        private static float Trapezoid(double v, double a, double b, double c, double d) => Math.Min(Ramp(v, a, b), 1f - Ramp(v, c, d));

        /// <summary>Attack shape of one group around frame f, relative to what was sounding before it,
        /// so it still works on top of a sustained mix (a kick over a held bass note).</summary>
        private struct GroupShape
        {
            public float pre, peak, incDb, ring; // dB, dB, dB of the ADDED power, seconds
            public bool ringValid;               // false when the rise is too small to measure a decay
        }

        private static GroupShape Shape(float[] lev, int f, int nextPeak, double hopT, double groupWindowSec)
        {
            int n = lev.Length;
            var s = new GroupShape();
            // Pre-attack reference: 30 ms ending just before this group's window starts to see the attack.
            int preEnd = f - (int)Math.Ceiling((0.5 * groupWindowSec + 0.005) / hopT);
            int preStart = preEnd - Math.Max(1, (int)Math.Round(0.030 / hopT));
            float pre = float.PositiveInfinity;
            for (int j = preStart; j <= preEnd; j++) { float v = j >= 0 && j < n ? lev[j] : -200f; if (v < pre) pre = v; }
            int atkEnd = Math.Min(n - 1, f + Math.Max(1, (int)Math.Round((0.5 * groupWindowSec + 0.010) / hopT)));
            float peak = float.NegativeInfinity; int pk = f;
            for (int j = f; j <= atkEnd; j++) if (lev[j] > peak) { peak = lev[j]; pk = j; }
            s.pre = pre; s.peak = peak;
            double added = Math.Pow(10, peak / 10.0) - Math.Pow(10, pre / 10.0);
            s.incDb = added > 1e-20 ? (float)(10 * Math.Log10(added)) : -200f;
            float rise = peak - pre;
            s.ringValid = rise >= 3f;
            float drop = Math.Max(3f, Math.Min(10f, 0.6f * rise));
            int capFr = (int)Math.Round(0.300 / hopT);
            int end = Math.Min(n - 1, pk + capFr);
            if (nextPeak > 0 && nextPeak - 1 < end) end = nextPeak - 1;
            s.ring = (float)((end - f + 1) * hopT);
            for (int j = pk + 1; j <= end; j++)
                if (lev[j] < peak - drop) { s.ring = (float)((j - f) * hopT); break; }
            return s;
        }

        private static int NextPeakAfter(List<int> frames, int f)
        {
            int lo = 0, hi = frames.Count;
            while (lo < hi) { int m = (lo + hi) >> 1; if (frames[m] <= f) lo = m + 1; else hi = m; }
            return lo < frames.Count ? frames[lo] : -1;
        }

        private static bool Classify(List<GroupPeak> peaks, int[] idx, OnsetFunctions F, float[][] thr,
                                     List<int>[] groupFrames, OnsetLayout L, AnalysisParams p, double hopT, out OnsetEvent ev)
        {
            int ng = L.groups.Length;
            int best = -1; float bestSal = 0f;
            for (int g = 0; g < ng; g++)
                if (idx[g] >= 0 && peaks[idx[g]].salience > bestSal) { bestSal = peaks[idx[g]].salience; best = g; }
            var bp = peaks[idx[best]];
            int fe = bp.frame;

            var present = new bool[ng];
            var sal = new float[ng];
            var cov = new float[ng];
            var sh = new GroupShape[ng];
            for (int g = 0; g < ng; g++)
            {
                present[g] = idx[g] >= 0;
                int f = present[g] ? peaks[idx[g]].frame : fe;
                if (present[g]) sal[g] = peaks[idx[g]].salience;
                else
                {
                    float m = 0f;
                    for (int j = Math.Max(0, f - 2); j <= Math.Min(F.frames - 1, f + 2); j++) m = Math.Max(m, F.odf[g][j] / Math.Max(1e-9f, thr[g][j]));
                    sal[g] = m;
                }
                double winSec = (double)L.resolutions[L.groupRes[g]].windowLength * L.resolutions[L.groupRes[g]].factor / L.sampleRate;
                sh[g] = Shape(F.levelDb[g], f, NextPeakAfter(groupFrames[g], f), hopT, winSec);
                float c = 0f;
                for (int j = Math.Max(0, f - 1); j <= Math.Min(F.frames - 1, f + 1); j++) c = Math.Max(c, F.cover[g][j]);
                cov[g] = c;
            }
            int lo = L.lowGroup, mi = L.midGroup, hi = L.highGroup;

            // Decision 1 (is it an attack at all?): some group that peaked must actually gain power,
            // that gain must matter for the whole mix (within MinRelativeGainDb of it), and the mix must
            // not be collapsing (spectral splatter of a sound ending into a silent band is no attack).
            float maxRise = float.NegativeInfinity, maxInc = -200f;
            for (int g = 0; g < ng; g++)
                if (present[g]) { maxRise = Math.Max(maxRise, sh[g].peak - sh[g].pre); maxInc = Math.Max(maxInc, sh[g].incDb); }
            double mixPre = 0, mixPeak = 0;
            for (int g = 0; g < ng; g++) { mixPre += Math.Pow(10, sh[g].pre / 10.0); mixPeak += Math.Pow(10, sh[g].peak / 10.0); }
            float mixPeakDb = (float)(10 * Math.Log10(mixPeak + 1e-20)), mixPreDb = (float)(10 * Math.Log10(mixPre + 1e-20));
            int presentCount = 0; float maxSalP = 0f;
            for (int g = 0; g < ng; g++) if (present[g]) { presentCount++; maxSalP = Math.Max(maxSalP, sal[g]); }
            float relGain = maxInc - mixPeakDb;
            bool collapsing = mixPeakDb - mixPreDb < -1f && relGain < -20f;
            bool weakLone = presentCount == 1 && maxSalP < 1.2f; // a lone band barely over its threshold
            if (maxRise < p.MinRiseDb || relGain < p.MinRelativeGainDb || collapsing || weakLone)
            {
                ev = default;
                return false;
            }

            // Kick: the low band gains at least as much power as the mid band (leakage from a snare body
            // or a tonal note does not), and the added low energy dies away like a drum (≈40–250 ms),
            // not like a held bass note.
            float kick = 0f;
            if (present[lo])
            {
                double dom = Ramp(sh[lo].incDb - sh[mi].incDb, -9.0, -3.0);
                double dur = sh[lo].ringValid ? Trapezoid(sh[lo].ring, 0.025, 0.045, 0.250, 0.300) : 0.0;
                kick = Clamp01(dom * (0.25 * Ramp(sal[lo], 1.0, 2.5) + 0.75 * dur));
            }

            // Snare: a broadband (many mid bins jump at once) mid attack that rings tens of ms, the mid
            // band carrying the most added power, usually with high-band activity.
            float snare = 0f;
            if (present[mi])
            {
                double noisy = Ramp(cov[mi], 0.25, 0.50);
                double dur = sh[mi].ringValid ? Trapezoid(sh[mi].ring, 0.020, 0.040, 0.220, 0.300) : 0.0;
                double highAct = present[hi] ? 1.0 : Ramp(sal[hi], 0.5, 1.0);
                // The mid band must carry a real share of the added power: a hi-hat's faint spill into
                // the mid band can look broadband too, but it is 30+ dB under the high band.
                double midGate = Ramp(sh[mi].incDb - sh[hi].incDb, -10.0, -4.0);
                snare = Clamp01(noisy * dur * midGate * (0.6 + 0.4 * highAct));
            }

            // Hi-hat: a broadband high attack that rings longer than a click (≥ ~15 ms) but not like a
            // cymbal swell, whose added power is at least as BRIGHT (per Hz) above 5 kHz as in the
            // 2–4.5 kHz reference band. Densities, not totals: the bands have different widths.
            // Explaining away: when the same event is already a confident snare, its broadband noise
            // also explains the high band, so a co-occurring hi-hat must be clearly brighter than that.
            float hat = 0f;
            float refInc = -200f;
            if (present[hi])
            {
                double winSec = (double)L.resolutions[L.groupRes[mi]].windowLength * L.resolutions[L.groupRes[mi]].factor / L.sampleRate;
                refInc = Shape(F.refDb, peaks[idx[hi]].frame, -1, hopT, winSec).incDb;
                double noisy = Ramp(cov[hi], 0.25, 0.50);
                double dur = sh[hi].ringValid ? Trapezoid(sh[hi].ring, 0.008, 0.016, 0.200, 0.300) : 0.0;
                // density (per Hz) difference: 0 dB = spectrally flat added noise (snare-like), hi-hats ≫ 0
                double densDiff = (sh[hi].incDb - L.highWidthDb) - (refInc - L.refWidthDb);
                bool snareExplains = snare >= p.FamilyConfidenceThreshold;
                double alone = snareExplains ? Ramp(densDiff, 1.0, 7.0) : Ramp(densDiff, -4.0, 2.0);
                hat = Clamp01(noisy * dur * (0.30 + 0.70 * alone));
            }

            DebugEvidence?.Add(new Evidence
            {
                time = (float)bp.time, lowP = present[lo], midP = present[mi], highP = present[hi],
                lowSal = sal[lo], midSal = sal[mi], highSal = sal[hi],
                lowRing = sh[lo].ringValid ? sh[lo].ring : -1, midRing = sh[mi].ringValid ? sh[mi].ring : -1, highRing = sh[hi].ringValid ? sh[hi].ring : -1,
                lowInc = sh[lo].incDb, midInc = sh[mi].incDb, highInc = sh[hi].incDb,
                lowCov = cov[lo], midCov = cov[mi], highCov = cov[hi], kick = kick, snare = snare, hat = hat, refInc = refInc, rise = maxRise,
            });

            float maxSal = 0f;
            for (int g = 0; g < ng; g++) if (present[g]) maxSal = Math.Max(maxSal, sal[g]);

            ev = new OnsetEvent
            {
                time = (float)bp.time,
                strength = Clamp01(1.0 - Math.Exp(-(maxSal - 1.0))),
                origin = OnsetOrigin.Detected,
                kickConfidence = kick,
                snareConfidence = snare,
                hihatConfidence = hat,
                genericConfidence = Ramp(maxSal, 1.0, 2.5),
            };
            float th = p.FamilyConfidenceThreshold;
            if (kick >= th) ev.families |= OnsetFamily.Kick;
            if (snare >= th) ev.families |= OnsetFamily.Snare;
            if (hat >= th) ev.families |= OnsetFamily.HiHat;
            if (ev.families == OnsetFamily.None) ev.families = OnsetFamily.Generic;
            if (ev.time < 0f) ev.time = 0f;
            return true;
        }

        /// <summary>Same-family events closer than the family's minimum interval are duplicates: the
        /// weaker one loses that label; an event left with no label is removed.</summary>
        private static void ApplyFamilyIntervals(List<OnsetEvent> ev, AnalysisParams p)
        {
            var fams = new[] { OnsetFamily.Kick, OnsetFamily.Snare, OnsetFamily.HiHat, OnsetFamily.Generic };
            var mins = new[] { p.KickMinIntervalSeconds, p.SnareMinIntervalSeconds, p.HihatMinIntervalSeconds, p.GenericMinIntervalSeconds };
            for (int fi = 0; fi < fams.Length; fi++)
            {
                var fam = fams[fi];
                int last = -1;
                for (int i = 0; i < ev.Count; i++)
                {
                    if ((ev[i].families & fam) == 0) continue;
                    if (last >= 0 && ev[i].time - ev[last].time < mins[fi])
                    {
                        float si = Score(ev[i], fam), sl = Score(ev[last], fam);
                        if (si > sl) { ev[last] = Strip(ev[last], fam); last = i; }
                        else ev[i] = Strip(ev[i], fam);
                        continue;
                    }
                    last = i;
                }
            }
            ev.RemoveAll(e => e.families == OnsetFamily.None);
        }

        private static float Score(OnsetEvent e, OnsetFamily f)
        {
            float c = f == OnsetFamily.Kick ? e.kickConfidence : f == OnsetFamily.Snare ? e.snareConfidence
                    : f == OnsetFamily.HiHat ? e.hihatConfidence : e.genericConfidence;
            return c * (0.5f + e.strength);
        }

        private static OnsetEvent Strip(OnsetEvent e, OnsetFamily f) { e.families &= ~f; return e; }
    }
}
