using System;
using System.Collections.Generic;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public sealed class Rng
    {
        private uint _s;
        public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }
        public uint Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
        public float Uniform() => (Next() >> 8) * (1f / 16777216f) * 2f - 1f;
    }

    /// <summary>RBJ biquad (Direct Form I), fresh state per instance.</summary>
    public sealed class Biquad
    {
        private double b0, b1, b2, a1, a2, x1, x2, y1, y2;
        public static Biquad HighPass(double sr, double f, double q) => Make(sr, f, q, 0);
        public static Biquad LowPass(double sr, double f, double q) => Make(sr, f, q, 1);
        private static Biquad Make(double sr, double f, double q, int type)
        {
            double w = 2 * Math.PI * Math.Min(f, sr * 0.45) / sr, cs = Math.Cos(w), al = Math.Sin(w) / (2 * q);
            double a0 = 1 + al;
            var bq = new Biquad();
            if (type == 0) { bq.b0 = (1 + cs) / 2; bq.b1 = -(1 + cs); bq.b2 = (1 + cs) / 2; }
            else { bq.b0 = (1 - cs) / 2; bq.b1 = 1 - cs; bq.b2 = (1 - cs) / 2; }
            bq.a1 = -2 * cs; bq.a2 = 1 - al;
            bq.b0 /= a0; bq.b1 /= a0; bq.b2 /= a0; bq.a1 /= a0; bq.a2 /= a0;
            return bq;
        }
        public double Process(double x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y; return y;
        }
    }

    public struct Truth
    {
        public double time;
        public OnsetFamily expected; // None = "any attack is fine" (used for tonal/bass starts → expect Generic but not enforced)
        public OnsetFamily optional; // families allowed but not required (justified per case)
        public bool optionalEvent;   // the event itself may be missed without failing (justified per case)
        public string label;
    }

    /// <summary>Builds a mono test signal with exactly known attack sample positions.</summary>
    public sealed class SignalBuilder
    {
        public readonly int sr;
        public readonly float[] x;
        public readonly List<Truth> truth = new List<Truth>();
        public readonly List<double> soundEnds = new List<double>(); // end time of every generated sound (boundary validation)
        private readonly Rng _rng;

        public SignalBuilder(int sampleRate, double seconds, uint seed = 12345)
        {
            sr = sampleRate;
            x = new float[Math.Max(0, (int)Math.Round(seconds * sampleRate))];
            _rng = new Rng(seed);
        }

        private int Start(double t) => (int)Math.Round(t * sr);
        /// <summary>5 ms raised-cosine fade at the end of every generated sound, so truncation never
        /// creates an artificial click (which would be a legitimate onset).</summary>
        /// <summary>End-of-sound release (raised cosine) so a sound that stops never creates an
        /// artificial click — that would be a legitimate onset, not a detector error. 30 ms default
        /// (the previous 5 ms produced measurable HF transients; see StimulusCheck). Settable only to
        /// reproduce that old behaviour.</summary>
        public static double EndFadeSeconds = 0.030;
        private double Fade(int i, int len, double sec = -1) { int f = Math.Max(1, (int)((sec < 0 ? EndFadeSeconds : sec) * sr)); int r = len - 1 - i; return r >= f ? 1.0 : 0.5 - 0.5 * Math.Cos(Math.PI * r / f); }
        private void Mark(double t, OnsetFamily f, string label) => truth.Add(new Truth { time = Start(t) / (double)sr, expected = f, label = label });

        public void Kick(double t, double gainDb = -6, bool mark = true)
        {
            int s0 = Start(t); double g = Math.Pow(10, gainDb / 20); double ph = 0;
            int len = (int)(0.40 * sr); soundEnds.Add((s0 + len) / (double)sr);
            for (int i = 0; i < len && s0 + i < x.Length; i++)
            {
                double tt = (double)i / sr;
                double f = 45 + 105 * Math.Exp(-tt / 0.030);
                ph += 2 * Math.PI * f / sr;
                double env = Math.Exp(-tt / 0.12) * Math.Min(1.0, tt / 0.0005);
                double click = tt < 0.004 ? _rng.Uniform() * Math.Exp(-tt / 0.0008) * 0.25 : 0;
                if (s0 + i >= 0) x[s0 + i] += (float)(g * (Math.Sin(ph) * env + click) * Fade(i, len));
            }
            if (mark) Mark(t, OnsetFamily.Kick, "kick");
        }

        public void Snare(double t, double gainDb = -8, bool mark = true)
        {
            int s0 = Start(t); double g = Math.Pow(10, gainDb / 20); double ph = 0;
            var hp = Biquad.HighPass(sr, 300, 0.707); var lp = Biquad.LowPass(sr, 9000, 0.707);
            int len = (int)(0.35 * sr); soundEnds.Add((s0 + len) / (double)sr);
            for (int i = 0; i < len && s0 + i < x.Length; i++)
            {
                double tt = (double)i / sr;
                ph += 2 * Math.PI * 185 / sr;
                double atk = Math.Min(1.0, tt / 0.0005);
                double tone = Math.Sin(ph) * Math.Exp(-tt / 0.05) * 0.5;
                double noise = lp.Process(hp.Process(_rng.Uniform())) * Math.Exp(-tt / 0.09) * 1.2;
                if (s0 + i >= 0) x[s0 + i] += (float)(g * atk * (tone + noise) * Fade(i, len));
            }
            if (mark) Mark(t, OnsetFamily.Snare, "snare");
        }

        public void Hat(double t, double gainDb = -14, double decay = 0.03, bool mark = true, bool optional = false)
        {
            int s0 = Start(t); double g = Math.Pow(10, gainDb / 20);
            var h1 = Biquad.HighPass(sr, 7000, 0.707); var h2 = Biquad.HighPass(sr, 7000, 0.707);
            int len = (int)(Math.Max(0.12, decay * 6) * sr); soundEnds.Add((s0 + len) / (double)sr);
            for (int i = 0; i < len && s0 + i < x.Length; i++)
            {
                double tt = (double)i / sr;
                double atk = Math.Min(1.0, tt / 0.0003);
                double v = h2.Process(h1.Process(_rng.Uniform())) * Math.Exp(-tt / decay) * 2.0;
                if (s0 + i >= 0) x[s0 + i] += (float)(g * atk * v * Fade(i, len));
            }
            if (mark) Mark(t, OnsetFamily.HiHat, "hihat");
            else if (optional) truth.Add(new Truth { time = Start(t) / (double)sr, expected = OnsetFamily.None, optional = OnsetFamily.HiHat, optionalEvent = true, label = "(hihat)" });
        }

        /// <summary>Single-sample broadband impulse (timing reference).</summary>
        public void Click(double t, double gainDb = -6, bool mark = true)
        {
            int s0 = Start(t);
            if (s0 >= 0 && s0 < x.Length) x[s0] += (float)Math.Pow(10, gainDb / 20);
            if (mark) Mark(t, OnsetFamily.None, "click");
        }

        /// <summary>Piano-like tonal note: fast attack, harmonic, slow decay → expected Generic.</summary>
        public void Pluck(double t, double f0, double gainDb = -12, bool mark = true)
        {
            int s0 = Start(t); double g = Math.Pow(10, gainDb / 20);
            int len = (int)(1.5 * sr); soundEnds.Add((s0 + len) / (double)sr);
            for (int i = 0; i < len && s0 + i < x.Length; i++)
            {
                double tt = (double)i / sr, v = 0;
                for (int h = 1; h <= 6; h++) v += Math.Sin(2 * Math.PI * f0 * h * tt) / h;
                double env = Math.Min(1.0, tt / 0.003) * Math.Exp(-tt / 0.6);
                if (s0 + i >= 0) x[s0 + i] += (float)(g * v * env * 0.5 * Fade(i, len));
            }
            if (mark) Mark(t, OnsetFamily.Generic, "pluck");
        }

        /// <summary>Held bass note (soft 20 ms attack, constant sustain): ONE onset at most.</summary>
        public void Bass(double t, double dur, double f0 = 55, double gainDb = -8, bool mark = true)
        {
            int s0 = Start(t); double g = Math.Pow(10, gainDb / 20);
            int len = (int)((dur + 0.05) * sr); soundEnds.Add((s0 + len) / (double)sr);
            for (int i = 0; i < len && s0 + i < x.Length; i++)
            {
                double tt = (double)i / sr;
                double v = Math.Sin(2 * Math.PI * f0 * tt) + 0.4 * Math.Sin(2 * Math.PI * 2 * f0 * tt) + 0.2 * Math.Sin(2 * Math.PI * 3 * f0 * tt);
                double env = Math.Min(1.0, tt / 0.020) * (tt > dur ? Math.Max(0, 1 - (tt - dur) / 0.05) : 1.0);
                if (s0 + i >= 0) x[s0 + i] += (float)(g * v * env * 0.6);
            }
            if (mark) Mark(t, OnsetFamily.None, "bass-start");
        }

        /// <summary>Slow linear crescendo of a chord (no attacks after the first second).</summary>
        public void PadCrescendo(double t, double dur, double gainDb = -10)
        {
            int s0 = Start(t); double g = Math.Pow(10, gainDb / 20);
            double[] f = { 220, 277.18, 329.63, 440 };
            int len = (int)(dur * sr); soundEnds.Add((s0 + len) / (double)sr);
            for (int i = 0; i < len && s0 + i < x.Length; i++)
            {
                double tt = (double)i / sr, v = 0;
                for (int k = 0; k < f.Length; k++) v += Math.Sin(2 * Math.PI * f[k] * tt);
                if (s0 + i >= 0) x[s0 + i] += (float)(g * v * 0.25 * (tt / dur) * Fade(i, len, EndFadeSeconds < 0.03 ? EndFadeSeconds : 0.3));
            }
        }

        public void NoiseFloor(double db)
        {
            double g = Math.Pow(10, db / 20);
            for (int i = 0; i < x.Length; i++) x[i] += (float)(g * _rng.Uniform());
        }

        /// <summary>Merges truths closer than `eps` into one with the union of expected families.</summary>
        public List<Truth> MergedTruth(double eps = 0.002)
        {
            var t = new List<Truth>(truth);
            t.Sort((a, b) => a.time.CompareTo(b.time));
            var o = new List<Truth>();
            foreach (var tr in t)
            {
                if (o.Count > 0 && tr.time - o[o.Count - 1].time < eps)
                {
                    var last = o[o.Count - 1];
                    last.expected |= tr.expected;
                    last.optional |= tr.optional;
                    last.optionalEvent = last.optionalEvent && tr.optionalEvent;
                    last.label += "+" + tr.label;
                    o[o.Count - 1] = last;
                }
                else o.Add(tr);
            }
            return o;
        }
    }
}
