using System;
using System.Collections.Generic;
using System.Linq;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public static partial class Program
    {
        static partial void ExtraImpl(string cmd, string[] rest, ref int handled)
        {
            if (cmd == "dbg") { DebugCase(rest[0], rest.Length > 1 ? int.Parse(rest[1]) : 48000); handled = 0; return; }
            Extra2(cmd, rest, ref handled);
        }
        static partial void Extra2(string cmd, string[] rest, ref int handled);

        static void DebugCase(string name, int sr)
        {
            var c = SynthTests.Cases().First(x => x.name.StartsWith(name));
            var b = new SignalBuilder(sr, c.seconds); c.build(b);
            OnsetDetector.DebugEvidence = new List<OnsetDetector.Evidence>();
            var r = MusicAnalyzer.Analyze(b.x, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = 1 });
            Console.WriteLine("truth: " + string.Join(" ", b.MergedTruth().Select(t => t.time.ToString("F3") + ":" + t.label)));
            foreach (var e in OnsetDetector.DebugEvidence) Console.WriteLine("  ev " + e);
            Console.WriteLine("final: " + string.Join(" ", r.onsets.Select(e => e.time.ToString("F3") + ":" + e.families)));
            OnsetDetector.DebugEvidence = null;
        }
    }
}
