using System;
using System.Collections.Generic;
using System.Linq;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public static class Calibration
    {
        public static void Run(string[] args)
        {
            // Raw (uncalibrated) group-peak bias per group & stimulus, for both sample rates.
            var s = new AnalysisSettings();
            for (int g = 0; g < s.onsetGroups.Length; g++) s.onsetGroups[g].timeOffsetSeconds = 0;
            var p = AnalysisParams.Create(s);
            foreach (int sr in new[] { 48000, 44100 })
            {
                Console.WriteLine($"\n--- raw peak bias @ {sr} Hz (center-time convention, no offset) ---");
                foreach (var stim in new[] { "kick", "snare", "hihat", "click" })
                {
                    var b = new SignalBuilder(sr, 14);
                    for (int k = 0; k < 24; k++)
                    {
                        double t = 0.5 + k * 0.5 + k * 0.00071; // sweep the sub-hop phase
                        if (stim == "kick") b.Kick(t); else if (stim == "snare") b.Snare(t); else if (stim == "hihat") b.Hat(t); else b.Click(t);
                    }
                    var raw = new List<OnsetDetector.RawPeak>();
                    MusicAnalyzer.Analyze(b.x, sr, p, new AnalysisRunOptions { maxWorkers = 1 }, raw, null);
                    var truth = b.MergedTruth().Select(t => t.time).ToArray();
                    for (int g = 0; g < 3; g++)
                    {
                        var errs = new List<double>();
                        foreach (var tt in truth)
                        {
                            var cand = raw.Where(r => r.group == g && Math.Abs(r.rawTime - tt) < 0.06).OrderBy(r => Math.Abs(r.rawTime - tt)).ToList();
                            if (cand.Count > 0) errs.Add((cand[0].rawTime - tt) * 1000);
                        }
                        string role = ((OnsetBandRole)p.GetOnsetGroup(g).role).ToString();
                        if (errs.Count == 0) { Console.WriteLine($"{stim,-6} group {role,-4}: no peaks"); continue; }
                        double mean = errs.Average(), sd = Math.Sqrt(errs.Select(e => (e - mean) * (e - mean)).Average());
                        Console.WriteLine($"{stim,-6} group {role,-4}: n={errs.Count,2}/{truth.Length}  bias {mean,7:F2} ms  sd {sd,5:F2}  min {errs.Min(),7:F2} max {errs.Max(),7:F2}");
                    }
                }
                // Legacy detector bias (window-start timestamps, global flux) on the same stimuli.
                foreach (var stim in new[] { "kick", "snare", "hihat", "click" })
                {
                    var b = new SignalBuilder(sr, 14);
                    for (int k = 0; k < 24; k++)
                    {
                        double t = 0.5 + k * 0.5 + k * 0.00071;
                        if (stim == "kick") b.Kick(t); else if (stim == "snare") b.Snare(t); else if (stim == "hihat") b.Hat(t); else b.Click(t);
                    }
                    var leg = LegacyAnalyzer.Analyze(b.x, sr);
                    var truth = b.MergedTruth().Select(t => t.time).ToArray();
                    var errs = new List<double>(); int fp = leg.onsets.Length;
                    foreach (var tt in truth)
                    {
                        var c = leg.onsets.Where(o => Math.Abs(o - tt) < 0.06).OrderBy(o => Math.Abs(o - tt)).ToList();
                        if (c.Count > 0) { errs.Add((c[0] - tt) * 1000); fp--; }
                    }
                    if (errs.Count == 0) { Console.WriteLine($"LEGACY {stim}: none"); continue; }
                    double mean = errs.Average(), sd = Math.Sqrt(errs.Select(e => (e - mean) * (e - mean)).Average());
                    Console.WriteLine($"LEGACY {stim,-6}: n={errs.Count,2}/{truth.Length} extra={fp,2}  bias {mean,7:F2} ms  sd {sd,5:F2}  min {errs.Min(),7:F2} max {errs.Max(),7:F2}");
                }
            }
        }
    }
}
