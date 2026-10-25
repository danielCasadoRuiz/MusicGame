using System;
using System.Collections.Generic;

namespace MusicGame.Audio.Analysis
{
    /// <summary>
    /// Tempo: autocorrelation of the summed, context-normalised onset evidence over the allowed BPM
    /// range with a broad log-normal prior, parabolic lag refinement. Phase: the grid offset that best
    /// fits the calibrated onset EVENTS (strength-weighted, Gaussian tolerance), so it lives in the
    /// same time domain as the events.
    /// </summary>
    public static class TempoEstimator
    {
        /// <summary>Fewer detected events than this = no tempo evidence (estimatedBpm 0).</summary>
        public const int MinEventsForTempo = 4;

        public static void Estimate(OnsetFunctions F, OnsetEvent[] events, AnalysisParams p, out float bpm, out float confidence, out float phase)
        {
            bpm = 0f; confidence = 0f; phase = 0f;
            int n = F.frames;
            double hopT = F.hopTime;
            if (n < 16) return;
            // A tempo needs repeated attacks: with fewer events there is no evidence (bpm stays 0 —
            // never an arbitrary default).
            if (events == null || events.Length < MinEventsForTempo) return;

            // Novelty: Σ groups of odf / mean(odf), then subtract a ~1 s moving mean and rectify.
            var nov = new float[n];
            for (int g = 0; g < F.odf.Length; g++)
            {
                double m = 0; var o = F.odf[g];
                for (int i = 0; i < n; i++) m += o[i];
                m = m / n + 1e-9;
                for (int i = 0; i < n; i++) nov[i] += (float)(o[i] / m);
            }
            int half = Math.Max(1, (int)Math.Round(0.5 / hopT));
            var pre = new double[n + 1];
            for (int i = 0; i < n; i++) pre[i + 1] = pre[i] + nov[i];
            var c = new float[n];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - half), b = Math.Min(n - 1, i + half);
                double v = nov[i] - (pre[b + 1] - pre[a]) / (b - a + 1);
                c[i] = v > 0 ? (float)v : 0f;
            }

            int lagMin = Math.Max(1, (int)Math.Floor(60.0 / p.MaxBpm / hopT));
            int lagMax = Math.Min(n - 2, (int)Math.Ceiling(60.0 / p.MinBpm / hopT));
            if (lagMax <= lagMin + 1) return;
            var score = new double[lagMax + 2];
            var acf = new double[lagMax + 2];
            double r0 = 0; for (int i = 0; i < n; i++) r0 += (double)c[i] * c[i];
            if (r0 <= 0) return;
            double sigma = Math.Max(0.05, p.TempoPriorOctaves);
            for (int lag = lagMin; lag <= lagMax + 1 && lag < n; lag++)
            {
                double r = 0;
                for (int i = 0; i + lag < n; i++) r += (double)c[i] * c[i + lag];
                r /= (n - lag);
                double bpmL = 60.0 / (lag * hopT);
                double oct = Math.Log(bpmL / p.TempoPriorCenterBpm, 2.0) / sigma;
                acf[lag] = r;
                score[lag] = r * Math.Exp(-0.5 * oct * oct);
            }
            // Only a PERIODICITY PEAK (an interior local maximum of the autocorrelation itself) is tempo
            // evidence; the prior only chooses between peaks. An isolated attack, or attacks with no
            // repetition in range, give an autocorrelation that just decays with lag → no tempo.
            int best = -1;
            for (int lag = lagMin + 1; lag < lagMax; lag++)
                if (acf[lag] > 0 && acf[lag] >= acf[lag - 1] && acf[lag] >= acf[lag + 1] && (best < 0 || score[lag] > score[best])) best = lag;
            if (best < 0) return;
            double refined = best;
            if (best > lagMin && best < lagMax)
            {
                double y0 = score[best - 1], y1 = score[best], y2 = score[best + 1];
                double den = y0 - 2 * y1 + y2;
                if (den < 0) refined = best + Math.Max(-0.5, Math.Min(0.5, 0.5 * (y0 - y2) / den));
            }
            double period = refined * hopT;
            bpm = (float)(60.0 / period);

            // Confidence: normalised autocorrelation of the (mean-free) novelty at the chosen lag —
            // how periodic the attacks are at that period (heuristic 0..1, not a probability).
            {
                double mu = 0; for (int i = 0; i < n; i++) mu += c[i]; mu /= n;
                double num = 0, den = 0;
                for (int i = 0; i < n; i++) { double v = c[i] - mu; den += v * v; if (i + best < n) num += v * (c[i + best] - mu); }
                confidence = den > 0 ? (float)Math.Max(0, Math.Min(1, num / den * n / (n - best))) : 0f;
            }

            // Phase from events: beats are where low/backbeat attacks are, so kick- and snare-like
            // events weigh more than hi-hat-only ones (which often sit on off-beats).
            if (events == null || events.Length == 0) return;
            const double tol = 0.015;
            var w = new double[events.Length];
            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                w[i] = (0.25 + e.strength) * (0.5 + 1.5 * e.kickConfidence + 0.75 * e.snareConfidence + 0.25 * e.genericConfidence * (e.hihatConfidence < 0.5f ? 1 : 0));
            }
            double bestS = -1; double bestPh = 0;
            for (double ph = 0; ph < period; ph += 0.001)
            {
                double s = 0;
                for (int i = 0; i < events.Length; i++)
                {
                    double d = (events[i].time - ph) / period;
                    d = (d - Math.Round(d)) * period;
                    s += w[i] * Math.Exp(-0.5 * (d / tol) * (d / tol));
                }
                if (s > bestS) { bestS = s; bestPh = ph; }
            }
            // Refine period + phase together: weighted least-squares fit time = phase + k·period over the
            // events that sit near the grid, so a 0.1 % BPM error does not drift the grid over a song.
            double per = period, ph0 = bestPh;
            for (int iter = 0; iter < 3; iter++)
            {
                double sw = 0, sk = 0, st = 0, skk = 0, skt = 0;
                for (int i = 0; i < events.Length; i++)
                {
                    double kf = (events[i].time - ph0) / per;
                    double k = Math.Round(kf);
                    double d = (kf - k) * per;
                    if (Math.Abs(d) > 2 * tol) continue;
                    double wi = w[i];
                    sw += wi; sk += wi * k; st += wi * events[i].time; skk += wi * k * k; skt += wi * k * events[i].time;
                }
                double det = sw * skk - sk * sk;
                if (sw <= 0 || det <= 1e-9) break;
                double newPer = (sw * skt - sk * st) / det;
                double newPh = (st - newPer * sk) / sw;
                if (newPer <= 0 || Math.Abs(newPer / per - 1) > 0.02) break; // never jump to another tempo
                per = newPer; ph0 = newPh;
            }
            ph0 = ph0 - Math.Floor(ph0 / per) * per;
            bpm = (float)(60.0 / per);
            phase = (float)ph0;
        }
    }

    /// <summary>
    /// Beat tracking by dynamic programming (Ellis 2007): beat positions that sit on strong, beat-like
    /// events while keeping inter-beat intervals close to the global period. Follows slow drift and
    /// small tempo changes that a single constant grid cannot. Input: the calibrated events, so beats
    /// live in the same (attack-calibrated) time domain.
    /// </summary>
    public static class BeatTracker
    {
        public static float[] Track(OnsetEvent[] events, float bpm, float duration, double hop, float tightness)
        {
            if (events == null || events.Length == 0 || bpm <= 0f || duration <= 0f) return Array.Empty<float>();
            int n = (int)Math.Ceiling(duration / hop) + 1;
            double period = 60.0 / bpm / hop; // frames
            if (period < 4 || n < 2 * period) return Array.Empty<float>();

            // Beat-likeness envelope: weighted events (kick/snare-like heavier), Gaussian σ = 10 ms.
            var c = new double[n];
            int rad = Math.Max(1, (int)Math.Round(0.025 / hop));
            double sig = 0.010 / hop;
            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                double w = (0.25 + e.strength) * (0.5 + 1.5 * e.kickConfidence + 0.75 * e.snareConfidence);
                double pos = e.time / hop;
                int f0 = (int)Math.Round(pos);
                for (int f = Math.Max(0, f0 - rad); f <= Math.Min(n - 1, f0 + rad); f++)
                {
                    double d = (f - pos) / sig;
                    c[f] += w * Math.Exp(-0.5 * d * d);
                }
            }
            double mean = 0; for (int i = 0; i < n; i++) mean += c[i]; mean /= n;
            double sd = 0; for (int i = 0; i < n; i++) sd += (c[i] - mean) * (c[i] - mean); sd = Math.Sqrt(sd / n) + 1e-9;
            for (int i = 0; i < n; i++) c[i] /= sd;

            var D = new double[n];
            var back = new int[n];
            int lo = (int)Math.Round(period * 0.5), hi = (int)Math.Round(period * 2.0);
            var penalty = new double[hi + 1]; // depends only on the interval → tabulated once
            for (int d = lo; d <= hi; d++) { double l = Math.Log(d / period); penalty[d] = tightness * l * l; }
            for (int t = 0; t < n; t++)
            {
                double best = double.NegativeInfinity; int arg = -1;
                for (int tau = Math.Max(0, t - hi); tau <= t - lo; tau++)
                {
                    double v = D[tau] - penalty[t - tau];
                    if (v > best) { best = v; arg = tau; }
                }
                if (arg >= 0 && best > 0) { D[t] = c[t] + best; back[t] = arg; }
                else { D[t] = c[t]; back[t] = -1; }
            }
            // End on the best-scoring frame within the last period.
            int end = n - 1; double bestEnd = double.NegativeInfinity;
            for (int t = Math.Max(0, n - 1 - (int)Math.Round(period)); t < n; t++) if (D[t] > bestEnd) { bestEnd = D[t]; end = t; }
            var beats = new System.Collections.Generic.List<float>();
            for (int t = end; t >= 0; t = back[t]) { beats.Add((float)(t * hop)); if (back[t] < 0) break; }
            beats.Reverse();
            return beats.ToArray();
        }
    }

    public static class StructureAnalyzer
    {
        /// <summary>Legacy AudioPreAnalyzer.SegmentSong: fixed-length blocks classified by energy / max.</summary>
        public static AnalysisSegment[] Segment(float[] energy, float hopTime, float max, float segmentSeconds)
        {
            if (energy == null || energy.Length == 0 || hopTime <= 0f) return Array.Empty<AnalysisSegment>();
            int minFrames = Math.Max(1, (int)Math.Round(segmentSeconds / hopTime));
            var segs = new List<AnalysisSegment>();
            for (int start = 0; start < energy.Length; start += minFrames)
            {
                int end = Math.Min(start + minFrames, energy.Length);
                float avg = 0f, mx = 0f;
                for (int f = start; f < end; f++) { avg += energy[f]; if (energy[f] > mx) mx = energy[f]; }
                avg /= (end - start);
                segs.Add(new AnalysisSegment { startTime = start * hopTime, endTime = end * hopTime, averageEnergy = avg, maxEnergy = mx, level = Level(avg, max) });
            }
            return segs.ToArray();
        }

        private static SegmentLevel Level(float e, float max)
        {
            if (max <= 0f) return SegmentLevel.Low;
            float r = e / max;
            if (r < 0.15f) return SegmentLevel.Low;
            if (r < 0.40f) return SegmentLevel.Mid;
            if (r < 0.70f) return SegmentLevel.High;
            return SegmentLevel.Drop;
        }
    }
}
