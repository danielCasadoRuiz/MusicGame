using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public static class Misc
    {
        public static void Run()
        {
            var mono = Program.LoadMono(Program.ResolveWavs(new[] { "first", "1" })[0], out int sr, out _, out _);
            // Progress: monotonic, ends at 1, many updates.
            foreach (int w in new[] { 1, 4 })
            {
                var vals = new List<float>();
                MusicAnalyzer.Analyze(mono, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = w, progress = v => { lock (vals) vals.Add(v); } });
                bool mono_ = true; for (int i = 1; i < vals.Count; i++) if (vals[i] < vals[i - 1]) mono_ = false;
                Console.WriteLine($"progress w={w}: {vals.Count} updates, monotonic {mono_}, first {vals[0]:F3}, last {vals[vals.Count - 1]:F3}");
            }
            // Cancellation at several points: must throw OperationCanceledException quickly, never return a result.
            foreach (int w in new[] { 1, 4 })
                foreach (int delayMs in new[] { 0, 50, 200, 400 })
                {
                    var cts = new CancellationTokenSource();
                    var sw = Stopwatch.StartNew();
                    var t = Task.Run(() => MusicAnalyzer.Analyze(mono, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = w, cancellation = cts.Token }));
                    Thread.Sleep(delayMs);
                    double tc = sw.Elapsed.TotalMilliseconds; cts.Cancel();
                    string outcome;
                    try { var r = t.GetAwaiter().GetResult(); outcome = "COMPLETED (finished before cancel)"; }
                    catch (OperationCanceledException) { outcome = "OperationCanceledException"; }
                    catch (Exception e) { outcome = "OTHER " + e.GetType().Name; }
                    Console.WriteLine($"cancel w={w} after {delayMs,3} ms: {outcome}, returned {sw.Elapsed.TotalMilliseconds - tc,5:F0} ms after Cancel()");
                }
        }
    }

    public static partial class Program
    {
        static partial void Extra9(string cmd, string[] rest, ref int handled);
    }
}
