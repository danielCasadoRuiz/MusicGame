using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicGame.Audio.Analysis;

namespace Harness
{
    /// <summary>Full mandatory suite with aggregated exit code; writes results\suite_*.txt.</summary>
    public static class Suite
    {
        public static int Synth()
        {
            int failed = 0;
            foreach (int sr in new[] { 44100, 48000 })
                failed += SynthTests.RunAll(AnalysisParams.Default, sr, new AnalysisRunOptions { maxWorkers = 1 }, true).Count(r => !r.pass && !r.informational);
            Console.WriteLine($"\nSYNTH: {failed} mandatory case(s) failed -> exit code {(failed > 0 ? 1 : 0)}");
            return failed > 0 ? 1 : 0;
        }

        public static int Stimuli()
        {
            Console.WriteLine(StimulusCheck.Report(out bool clean));
            Console.WriteLine(StimulusCheck.MaskingReport());
            Console.WriteLine(StimulusCheck.LegatoReport());
            Console.WriteLine("STIMULI: current stimuli " + (clean ? "clean" : "HAVE UNINTENDED TRANSIENTS"));
            return clean ? 0 : 1;
        }

        public static int Run()
        {
            Directory.CreateDirectory("results");
            var failures = new List<string>();
            var orig = Console.Out;

            void Capture(Func<bool> body, string file)
            {
                var sw = new StringWriter();
                Console.SetOut(sw);
                bool ok;
                try { ok = body(); }
                catch (Exception e) { Console.WriteLine("EXCEPTION " + e); ok = false; }
                finally { Console.SetOut(orig); }
                File.WriteAllText(Path.Combine("results", file), sw.ToString());
                if (!ok) failures.Add(file);
                Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {file}");
            }

            Capture(() => Stimuli() == 0, "suite_stimuli.txt");

            foreach (int sr in new[] { 44100, 48000 })
                Capture(() =>
                {
                    var rs = SynthTests.RunAll(AnalysisParams.Default, sr, new AnalysisRunOptions { maxWorkers = 1 }, true);
                    var m = rs.Where(r => !r.informational).ToList();
                    int f = m.Count(r => !r.pass);
                    Console.WriteLine($"\nTOTALS @ {sr} Hz: mandatory cases {m.Count}, failed {f}; omissions {m.Sum(r => r.misses)}, extra events {m.Sum(r => r.fps)}, missing families {m.Sum(r => r.famMissing)}, extra families {m.Sum(r => r.famExtra)}, stimulus artefacts {rs.Sum(r => r.stimulusArtefacts)}, max |timing error| {m.Select(r => r.maxAbsMs).DefaultIfEmpty(0).Max():F2} ms; informational cases {rs.Count - m.Count}");
                    return f == 0;
                }, $"suite_synth_{sr}.txt");

            Capture(() =>
            {
                bool ok = true;
                var inputs = new List<(string, float[], int)>();
                foreach (int sr in new[] { 44100, 48000 })
                {
                    var b = new SignalBuilder(sr, 17); SynthTests.Cases().First(c => c.name.StartsWith("drum loop")).build(b);
                    inputs.Add(($"synthetic drum loop {sr}", b.x, sr));
                }
                var wav = Program.ResolveWavs(new[] { "first", "1" })[0];
                inputs.Add((Path.GetFileNameWithoutExtension(wav), Program.LoadMono(wav, out int wsr, out _, out _), wsr));
                foreach (var (name, x, sr) in inputs)
                {
                    var refR = MusicAnalyzer.Analyze(x, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = 1, chunkFrames = 1 << 30 });
                    foreach (var (w, ch) in new[] { (1, 1024), (2, 1024), (4, 1024), (8, 333), (16, 64), (4, 4096) })
                    {
                        var r = MusicAnalyzer.Analyze(x, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = w, chunkFrames = ch });
                        string d = Program.Diff(refR, r);
                        if (d.Length > 0) ok = false;
                        Console.WriteLine($"EQUIV {name,-36} workers {w,2} chunk {ch,5}: {(d.Length == 0 ? "BITWISE IDENTICAL" : "DIFF " + d)}");
                    }
                }
                return ok;
            }, "suite_equivalence.txt");

            Capture(() =>
            {
                bool ok = true;
                var mono = Program.LoadMono(Program.ResolveWavs(new[] { "first", "1" })[0], out int sr, out _, out _);
                foreach (int w in new[] { 1, 4 })
                {
                    var vals = new List<float>();
                    MusicAnalyzer.Analyze(mono, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = w, progress = v => { lock (vals) vals.Add(v); } });
                    bool mono_ = true; for (int i = 1; i < vals.Count; i++) if (vals[i] < vals[i - 1]) mono_ = false;
                    bool good = mono_ && vals.Count > 10 && vals[vals.Count - 1] == 1f;
                    ok &= good;
                    Console.WriteLine($"progress w={w}: {vals.Count} updates, monotonic {mono_}, last {vals[vals.Count - 1]:F3} -> {(good ? "OK" : "FAIL")}");
                }
                foreach (int w in new[] { 1, 4 })
                    foreach (int delayMs in new[] { 0, 50, 200, 400 })
                    {
                        var cts = new CancellationTokenSource();
                        var t = Task.Run(() => MusicAnalyzer.Analyze(mono, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = w, cancellation = cts.Token }));
                        Thread.Sleep(delayMs);
                        var sw = System.Diagnostics.Stopwatch.StartNew(); cts.Cancel();
                        string outcome; bool good;
                        try { t.GetAwaiter().GetResult(); outcome = "completed before cancel"; good = delayMs > 0; }
                        catch (OperationCanceledException) { outcome = "OperationCanceledException (no result)"; good = sw.Elapsed.TotalMilliseconds < 500; }
                        catch (Exception e) { outcome = "OTHER " + e.GetType().Name; good = false; }
                        ok &= good;
                        Console.WriteLine($"cancel w={w} after {delayMs,3} ms: {outcome}, returned {sw.Elapsed.TotalMilliseconds,5:F0} ms after Cancel() -> {(good ? "OK" : "FAIL")}");
                    }
                return ok;
            }, "suite_cancel_progress.txt");

            Console.WriteLine(failures.Count == 0 ? "SUITE: all mandatory parts passed (exit 0)" : "SUITE: FAILED parts: " + string.Join(", ", failures) + " (exit 1)");
            return failures.Count == 0 ? 0 : 1;
        }
    }
}
