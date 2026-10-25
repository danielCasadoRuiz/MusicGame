using System;
using System.IO;
using System.Linq;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public static partial class Program
    {
        static partial void Extra4(string cmd, string[] rest, ref int handled)
        {
            if (cmd == "gridalign") { GridAlign(rest); handled = 0; return; }
            Extra5(cmd, rest, ref handled);
        }
        static partial void Extra5(string cmd, string[] rest, ref int handled);

        /// <summary>Fraction of kick/snare events within ±tol of a beat grid (best phase searched for the
        /// given BPM; for the new core its own phase is ALSO reported). A same-events comparison of two
        /// tempo hypotheses — not a ground-truth accuracy.</summary>
        static double GridHit(float[] t, double bpm, double phase, double tol)
        {
            double per = 60.0 / bpm; int hit = 0;
            foreach (var x in t) { double d = (x - phase) / per; d = Math.Abs(d - Math.Round(d)) * per; if (d <= tol) hit++; }
            return t.Length > 0 ? (double)hit / t.Length : 0;
        }
        static double BestPhaseHit(float[] t, double bpm, double tol, out double bestPh)
        {
            double per = 60.0 / bpm, best = -1; bestPh = 0;
            for (double ph = 0; ph < per; ph += 0.002) { double h = GridHit(t, bpm, ph, tol); if (h > best) { best = h; bestPh = ph; } }
            return best;
        }

        static double BeatHit(float[] t, float[] beats, double tol)
        {
            if (t.Length == 0 || beats.Length == 0) return 0;
            int hit = 0;
            foreach (var x in t)
            {
                int i = Array.BinarySearch(beats, x); if (i < 0) i = ~i;
                double d = double.MaxValue;
                if (i < beats.Length) d = Math.Min(d, Math.Abs(beats[i] - x));
                if (i > 0) d = Math.Min(d, Math.Abs(beats[i - 1] - x));
                if (d <= tol) hit++;
            }
            return (double)hit / t.Length;
        }

        static void GridAlign(string[] args)
        {
            Console.WriteLine("song | legacy BPM: hit% (best phase) | new BPM: hit% own phase / best phase | (kick+snare events, ±25 ms; first and last third separately for drift)");
            foreach (var wav in ResolveWavs(args))
            {
                var mono = LoadMono(wav, out int sr, out _, out _);
                var leg = LegacyAnalyzer.Analyze(mono, sr);
                var r = MusicAnalyzer.Analyze(mono, sr, AnalysisParams.Default, new AnalysisRunOptions());
                var ks = r.onsets.Where(e => e.Has(OnsetFamily.Kick) || e.Has(OnsetFamily.Snare)).Select(e => e.time).ToArray();
                float dur = r.duration;
                var first = ks.Where(x => x < dur / 3).ToArray(); var last = ks.Where(x => x > 2 * dur / 3).ToArray();
                double hl = BestPhaseHit(ks, leg.bpm, 0.025, out _);
                double hn = GridHit(ks, r.estimatedBpm, r.beatPhase, 0.025);
                double hnb = BestPhaseHit(ks, r.estimatedBpm, 0.025, out _);
                double hnF = GridHit(first, r.estimatedBpm, r.beatPhase, 0.025), hnL = GridHit(last, r.estimatedBpm, r.beatPhase, 0.025);
                double hb = BeatHit(ks, r.beatTimes, 0.025), hbF = BeatHit(first, r.beatTimes, 0.025), hbL = BeatHit(last, r.beatTimes, 0.025);
                double ibi = r.beatTimes.Length > 1 ? 60.0 * (r.beatTimes.Length - 1) / (r.beatTimes[r.beatTimes.Length - 1] - r.beatTimes[0]) : 0;
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(wav),-40} | {leg.bpm,5:F0}: {hl,5:P0} | {r.estimatedBpm,6:F2}: {hn,5:P0} / {hnb,5:P0} [1/3 {hnF,4:P0} 3/3 {hnL,4:P0}] | tracked beats {r.beatTimes.Length} (mean {ibi,6:F2} BPM): {hb,5:P0} [1/3 {hbF,4:P0} 3/3 {hbL,4:P0}]");
            }
        }
    }
}
