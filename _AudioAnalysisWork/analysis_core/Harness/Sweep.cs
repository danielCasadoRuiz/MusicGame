using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public static partial class Program
    {
        static partial void Extra5(string cmd, string[] rest, ref int handled)
        {
            if (cmd == "sweep") { Sweep(rest); handled = 0; return; }
            Extra6(cmd, rest, ref handled);
        }
        static partial void Extra6(string cmd, string[] rest, ref int handled);

        static AnalysisSettings Variant(string v)
        {
            var s = new AnalysisSettings();
            const double k = 1.0 / 48000.0;
            switch (v)
            {
                case "base": break;
                case "rel35": s.minRelativeGainDb = -35f; break;
                case "rel30": s.minRelativeGainDb = -30f; break;
                case "hop512": s.onsetHopSeconds = 512 * k; s.onsetDiffLagSeconds = 512 * k; break;
                case "lag256": s.onsetDiffLagSeconds = 256 * k; break;
                case "lag1024": s.onsetDiffLagSeconds = 1024 * k; break;
                case "all1024": for (int i = 0; i < 3; i++) s.onsetGroups[i].windowSeconds = 1024 * k; break;
                case "all2048": for (int i = 0; i < 3; i++) s.onsetGroups[i].windowSeconds = 2048 * k; break;
                case "all512": for (int i = 0; i < 3; i++) s.onsetGroups[i].windowSeconds = 512 * k; break;
                case "low1024": s.onsetGroups[0].windowSeconds = 1024 * k; break;
                case "low4096": s.onsetGroups[0].windowSeconds = 4096 * k; break;
                case "high1024": s.onsetGroups[2].windowSeconds = 1024 * k; break;
                case "high256": s.onsetGroups[2].windowSeconds = 256 * k; break;
                default: throw new ArgumentException("variant " + v);
            }
            // Variants that change windows/hops change latency: recalibrate offsets the same way the
            // defaults were calibrated (Low on kicks, Mid on snares, High on hi-hats) so the comparison
            // is about detection quality, not about a stale offset.
            if (v != "base" && v != "rel35" && v != "rel30") Recalibrate(s);
            return s;
        }

        static void Recalibrate(AnalysisSettings s)
        {
            for (int g = 0; g < 3; g++) s.onsetGroups[g].timeOffsetSeconds = 0;
            var p = AnalysisParams.Create(s);
            string[] stim = { "kick", "snare", "hihat" };
            for (int g = 0; g < 3; g++)
            {
                var b = new SignalBuilder(48000, 14);
                for (int k = 0; k < 24; k++) { double t = 0.5 + k * 0.5 + k * 0.00071; if (g == 0) b.Kick(t); else if (g == 1) b.Snare(t); else b.Hat(t); }
                var raw = new List<OnsetDetector.RawPeak>();
                MusicAnalyzer.Analyze(b.x, 48000, p, new AnalysisRunOptions { maxWorkers = 1 }, raw, null);
                var errs = new List<double>();
                foreach (var tt in b.MergedTruth().Select(t => t.time))
                {
                    var c = raw.Where(r => r.group == g && Math.Abs(r.rawTime - tt) < 0.06).OrderBy(r => Math.Abs(r.rawTime - tt)).ToList();
                    if (c.Count > 0) errs.Add(c[0].rawTime - tt);
                }
                s.onsetGroups[g].timeOffsetSeconds = errs.Count > 0 ? Math.Round(errs.Average(), 4) : 0;
            }
        }

        static void Sweep(string[] args)
        {
            var variants = args.Length > 0 && args[0] != "-" ? args[0].Split(',') : new[] { "base", "rel35", "rel30", "hop512", "lag256", "lag1024", "all512", "all1024", "all2048", "low1024", "low4096", "high1024", "high256" };
            int songs = args.Length > 1 ? int.Parse(args[1]) : 6;
            var wavs = ResolveWavs(new[] { "first", songs.ToString() });
            var monos = wavs.Select(w => (LoadMono(w, out int sr, out _, out _), sr)).ToList();
            Console.WriteLine($"variant  | offsets ms L/M/H | synth: fails miss FP fam-ok/checked mean|err| max|err| ms | real ({songs} songs): events kick snare hat generic | ms/song");
            foreach (var v in variants)
            {
                var s = Variant(v);
                var p = AnalysisParams.Create(s);
                int fails = 0, miss = 0, fp = 0, fo = 0, fc = 0; double sumAbs = 0; int nAbs = 0; double maxAbs = 0;
                foreach (int sr in new[] { 48000, 44100 })
                    foreach (var r in SynthTests.RunAll(p, sr, new AnalysisRunOptions { maxWorkers = 1 }, false))
                    {
                        if (!r.pass) fails++;
                        if (r.informational) continue;
                        miss += r.misses; fp += r.fps; fo += r.famOk; fc += r.famChecked;
                        if (r.hits > 0 && !r.name.StartsWith("clicks")) { sumAbs += r.meanAbsMs * r.hits; nAbs += r.hits; maxAbs = Math.Max(maxAbs, r.maxAbsMs); }
                    }
                int ev = 0, k = 0, sn = 0, h = 0, g = 0; double ms = 0;
                foreach (var (x, sr) in monos)
                {
                    var sw = Stopwatch.StartNew();
                    var r = MusicAnalyzer.Analyze(x, sr, p, new AnalysisRunOptions());
                    ms += sw.Elapsed.TotalMilliseconds;
                    ev += r.onsets.Length; k += r.onsets.Count(e => e.Has(OnsetFamily.Kick)); sn += r.onsets.Count(e => e.Has(OnsetFamily.Snare));
                    h += r.onsets.Count(e => e.Has(OnsetFamily.HiHat)); g += r.onsets.Count(e => e.Has(OnsetFamily.Generic));
                }
                string off = string.Join("/", s.onsetGroups.Select(o => (o.timeOffsetSeconds * 1000).ToString("F1")));
                Console.WriteLine($"{v,-8} | {off,-16} | {fails,2} {miss,3} {fp,3} {fo,3}/{fc,-3} {sumAbs / Math.Max(1, nAbs),5:F2} {maxAbs,6:F2} | {ev,6} {k,5} {sn,5} {h,5} {g,5} | {ms / monos.Count,5:F0}");
            }
        }
    }
}
