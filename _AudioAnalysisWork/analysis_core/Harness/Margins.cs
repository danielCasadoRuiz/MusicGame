using System;
using System.Collections.Generic;
using System.Linq;
using MusicGame.Audio.Analysis;

namespace Harness
{
    /// <summary>Decision margins on the synthetic suite: for each family, the lowest confidence among
    /// matched events that REQUIRE it and the highest among matched events where it is NOT permitted.
    /// Threshold is 0.5; a robust classifier keeps both well away from it.</summary>
    public static class Margins
    {
        public static void Run()
        {
            var fams = new[] { OnsetFamily.Kick, OnsetFamily.Snare, OnsetFamily.HiHat };
            foreach (int sr in new[] { 44100, 48000 })
            {
                var minReq = fams.ToDictionary(f => f, f => (1.0, ""));
                var maxNot = fams.ToDictionary(f => f, f => (0.0, ""));
                foreach (var c in SynthTests.Cases())
                {
                    if (c.informational) continue;
                    var b = new SignalBuilder(sr, c.seconds); c.build(b);
                    var ev = MusicAnalyzer.Analyze(b.x, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = 1 }).onsets;
                    foreach (var t in b.MergedTruth())
                    {
                        if (ev.Length == 0) break;
                        var e = ev.OrderBy(x => Math.Abs(x.time - t.time)).First();
                        if (Math.Abs(e.time - t.time) > 0.05) continue;
                        foreach (var f in fams)
                        {
                            double conf = f == OnsetFamily.Kick ? e.kickConfidence : f == OnsetFamily.Snare ? e.snareConfidence : e.hihatConfidence;
                            if ((t.expected & f) != 0) { if (conf < minReq[f].Item1) minReq[f] = (conf, c.name + " @" + t.time.ToString("F2")); }
                            else if ((t.optional & f) == 0) { if (conf > maxNot[f].Item1) maxNot[f] = (conf, c.name + " @" + t.time.ToString("F2")); }
                        }
                    }
                }
                Console.WriteLine($"--- {sr} Hz (threshold 0.50)");
                foreach (var f in fams)
                    Console.WriteLine($"  {f,-6} min conf where required {minReq[f].Item1:F2} [{minReq[f].Item2}] | max conf where not permitted {maxNot[f].Item1:F2} [{maxNot[f].Item2}]");
            }
        }
    }
}
