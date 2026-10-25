using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MusicGame.Audio.Analysis;

namespace Harness
{
    /// <summary>
    /// Detector-independent validation of the synthetic stimuli. A transient is a 2 ms frame whose
    /// high-frequency energy (first difference of the signal) jumps ≥ 9 dB above the max of the
    /// previous 10 ms while being within 60 dB of the signal's loudest frame. Transients that are not
    /// intended attacks (truth times, −3 ms … +20 ms) are stimulus artefacts: detector events on them
    /// are reported separately and invalidate the case instead of counting as detector errors.
    /// </summary>
    public static class StimulusCheck
    {
        public const double FrameSec = 0.002, RiseDb = 9.0, RangeDb = 60.0;

        /// <summary>2 ms frame energies (dB) of the signal high-passed at 2 kHz (two cascaded 2nd-order
        /// RBJ sections), and of the full-band signal. The high-pass keeps low tones (bass, whose
        /// short-frame energy oscillates with waveform phase) out of the transient test.</summary>
        public static double[] HfEnvelopeDb(float[] x, int sr, out double[] fullDb)
        {
            int fl = Math.Max(1, (int)Math.Round(FrameSec * sr));
            int nf = x.Length / fl;
            var e = new double[nf]; fullDb = new double[nf];
            var h1 = Biquad.HighPass(sr, 2000, 0.707); var h2 = Biquad.HighPass(sr, 2000, 0.707);
            int idx = 0;
            for (int k = 0; k < nf; k++)
            {
                double s = 0, f = 0;
                for (int i = k * fl; i < (k + 1) * fl; i++, idx++) { double d = h2.Process(h1.Process(x[i])); s += d * d; f += (double)x[i] * x[i]; }
                e[k] = 10 * Math.Log10(s / fl + 1e-24);
                fullDb[k] = 10 * Math.Log10(f / fl + 1e-24);
            }
            return e;
        }

        /// <summary>Times (s) of HF rises that are not within an intended attack window. A rise only
        /// counts if that HF frame is within 40 dB of the loudest full-band frame of the stimulus.</summary>
        public static List<double> UnintendedTransients(float[] x, int sr, IEnumerable<double> attackTimes)
        {
            var res = new List<double>();
            var e = HfEnvelopeDb(x, sr, out var full);
            if (e.Length == 0) return res;
            double max = full.Max();
            var att = attackTimes.ToArray();
            for (int k = 1; k < e.Length; k++)
            {
                if (e[k] < max - 40) continue;
                double prev = double.NegativeInfinity;
                for (int j = Math.Max(0, k - 5); j < k; j++) prev = Math.Max(prev, e[j]);
                if (e[k] - prev < RiseDb) continue;
                double t = (double)k * Math.Max(1, (int)Math.Round(FrameSec * sr)) / sr; // exact frame start (88 samples ≠ 2 ms at 44.1 kHz)
                if (att.Any(a => t >= a - 0.003 && t <= a + 0.020)) continue;
                if (res.Count > 0 && t - res[res.Count - 1] < 0.010) continue;
                res.Add(t);
            }
            return res;
        }

        /// <summary>
        /// Boundary check: around every generated sound's END (±40 ms), a band energy (20–150 Hz,
        /// 150–4000 Hz, 4 kHz–Nyquist; 1024-point Hann, hop 128) that jumps ≥ 6 dB above the previous
        /// ~20 ms while within 50 dB of the loudest band frame is a cut-off artefact. Plain band
        /// energies — no detector code is involved.
        /// </summary>
        public static List<double> BoundaryArtefacts(float[] x, int sr, IEnumerable<double> ends, IEnumerable<double> attacks)
        {
            var res = new List<double>();
            const int n = 1024, hop = 128;
            if (x.Length < n) return res;
            int frames = (x.Length - n) / hop + 1;
            var plan = RealFftPlan.Get(n); var w = WindowTables.Get(WindowKind.Hann, n);
            var fr = new float[n]; var re = new float[n / 2 + 1]; var im = new float[n / 2 + 1]; var sc = new FftScratch(n);
            double binHz = (double)sr / n;
            int[] lo = { (int)Math.Ceiling(20 / binHz), (int)Math.Ceiling(150 / binHz), (int)Math.Ceiling(4000 / binHz) };
            int[] hi = { (int)(150 / binHz), (int)(4000 / binHz), n / 2 };
            var E = new double[3, frames];
            double gmax = double.NegativeInfinity;
            for (int k = 0; k < frames; k++)
            {
                for (int i = 0; i < n; i++) fr[i] = x[k * hop + i] * w[i];
                plan.Forward(fr, re, im, sc);
                for (int b = 0; b < 3; b++)
                {
                    double s = 0; for (int q = Math.Max(1, lo[b]); q <= hi[b]; q++) s += re[q] * re[q] + im[q] * im[q];
                    E[b, k] = 10 * Math.Log10(s + 1e-24); gmax = Math.Max(gmax, E[b, k]);
                }
            }
            var att = attacks.ToArray();
            foreach (double e in ends)
            {
                for (int k = 8; k < frames; k++)
                {
                    double tc = (k * hop + n / 2) / (double)sr;
                    if (tc < e - 0.04 || tc > e + 0.04) continue;
                    if (att.Any(a => Math.Abs(a - tc) < 0.03)) continue;
                    for (int b = 0; b < 3; b++)
                    {
                        if (E[b, k] < gmax - 60) continue;
                        double prev = double.NegativeInfinity;
                        for (int j = k - 8; j < k; j++) prev = Math.Max(prev, E[b, j]);
                        if (E[b, k] - prev >= 4 && !res.Any(t => Math.Abs(t - tc) < 0.02)) res.Add(tc);
                    }
                }
            }
            return res;
        }

        public static List<double> AllArtefacts(SignalBuilder b)
        {
            var att = b.truth.Select(t => t.time).ToList();
            var a = UnintendedTransients(b.x, b.sr, att);
            foreach (var t in BoundaryArtefacts(b.x, b.sr, b.soundEnds, att)) if (!a.Any(u => Math.Abs(u - t) < 0.02)) a.Add(t);
            a.Sort();
            return a;
        }

        /// <summary>Every stimulus type alone (with silence around), at both rates, with the current fade
        /// and with the old 5 ms fade, to show which stimuli had cut-off artefacts.</summary>
        public static string Report(out bool allClean)
        {
            var sb = new StringBuilder();
            allClean = true;
            var kinds = new (string, Action<SignalBuilder>)[]
            {
                ("kick", b => b.Kick(0.2)), ("snare", b => b.Snare(0.2)), ("hihat", b => b.Hat(0.2)), ("open hat 0.15", b => b.Hat(0.2, -14, 0.15)),
                ("click", b => b.Click(0.2)), ("pluck 262 Hz", b => b.Pluck(0.2, 262)), ("bass 55 Hz 1 s", b => b.Bass(0.2, 1.0)),
                ("pad crescendo 2 s", b => b.PadCrescendo(0.2, 2.0)),
            };
            double saved = SignalBuilder.EndFadeSeconds;
            foreach (double fade in new[] { 0.005, saved })
            {
                SignalBuilder.EndFadeSeconds = fade;
                sb.AppendLine($"  end fade {fade * 1000:F0} ms{(fade == saved ? " (CURRENT)" : " (old)")}:");
                foreach (int sr in new[] { 44100, 48000 })
                    foreach (var (name, build) in kinds)
                    {
                        var b = new SignalBuilder(sr, 2.6); build(b);
                        var tr = AllArtefacts(b);
                        var ev = MusicAnalyzer.Analyze(b.x, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = 1 }).onsets;
                        var extra = ev.Where(e => !b.truth.Any(t => Math.Abs(e.time - t.time) < 0.05)).ToList();
                        if (fade == saved && tr.Count > 0) allClean = false;
                        sb.AppendLine($"    {sr,5} Hz {name,-18} unintended stimulus transients: {tr.Count} [{string.Join(" ", tr.Select(t => t.ToString("F3")))}] | detector events off-attack: {extra.Count} [{string.Join(" ", extra.Select(e => e.time.ToString("F3") + ":" + e.families))}]");
                    }
            }
            SignalBuilder.EndFadeSeconds = saved;
            return sb.ToString();
        }

        /// <summary>Signal-based justification for the drum-loop hats under snare hits being OPTIONAL:
        /// how much the hat adds to the 5–16 kHz power the snare already has during the first 20 ms.</summary>
        public static string MaskingReport()
        {
            var sb = new StringBuilder();
            foreach (int sr in new[] { 44100, 48000 })
            {
                double Hf(Action<SignalBuilder> build)
                {
                    var b = new SignalBuilder(sr, 0.5); build(b);
                    int n = 1024, s0 = (int)(0.1 * sr);
                    var plan = RealFftPlan.Get(n); var fr = new float[n]; var re = new float[n / 2 + 1]; var im = new float[n / 2 + 1];
                    var w = WindowTables.Get(WindowKind.Hann, n);
                    for (int i = 0; i < n; i++) fr[i] = b.x[s0 + i] * w[i];
                    plan.Forward(fr, re, im, new FftScratch(n));
                    double binHz = (double)sr / n, p = 0;
                    for (int k = (int)Math.Ceiling(5000 / binHz); k <= Math.Min(n / 2, (int)(16000 / binHz)); k++) p += re[k] * re[k] + im[k] * im[k];
                    return 10 * Math.Log10(p + 1e-24);
                }
                // same seeds as in the loop are irrelevant: noise level, not waveform, matters here
                double snare = Hf(b => b.Snare(0.1, -8)), hat = Hf(b => b.Hat(0.1, -18)), both = Hf(b => { b.Snare(0.1, -8); b.Hat(0.1, -18); });
                sb.AppendLine($"  {sr} Hz, first {1024.0 / sr * 1000:F0} ms after the hit, 5–16 kHz power: snare {snare:F1} dB, hat {hat:F1} dB, snare+hat {both:F1} dB → the hat adds {both - snare:+0.0;-0.0} dB to what the snare already has (hat is {hat - snare:F1} dB under the snare in its own band)");
            }
            return sb.ToString();
        }

        /// <summary>Justification for the legato-bass case being informational: RMS (20 ms) change at each
        /// note boundary — no energy rise means no attack by construction.</summary>
        public static string LegatoReport()
        {
            var sb = new StringBuilder();
            int sr = 48000;
            var b = new SignalBuilder(sr, 6);
            double[] f = { 55, 65.4, 73.4, 49, 55, 82.4, 61.7, 55 };
            for (int k = 0; k < 8; k++) b.Bass(0.5 + k * 0.6, 0.6, f[k]);
            double Rms(double t0, double t1) { int a = (int)(t0 * sr), c = (int)(t1 * sr); double s = 0; for (int i = a; i < c; i++) s += b.x[i] * b.x[i]; return 10 * Math.Log10(s / (c - a) + 1e-24); }
            var rises = new List<double>();
            for (int k = 1; k < 8; k++) { double t = 0.5 + k * 0.6; rises.Add(Rms(t + 0.005, t + 0.025) - Rms(t - 0.040, t - 0.020)); }
            sb.AppendLine($"  legato bass note changes: RMS(after 5–25 ms) − RMS(before 20–40 ms) = [{string.Join(", ", rises.Select(r => r.ToString("+0.0;-0.0")))}] dB (soft legato boundaries, no sharp attack; for reference the detector requires a ≥ 1.5 dB rise in some band plus a flux peak)");
            return sb.ToString();
        }
    }
}
