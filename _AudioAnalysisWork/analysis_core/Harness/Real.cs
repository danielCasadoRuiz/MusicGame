using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public static partial class Program
    {
        public const string MusicDir = @"C:\Users\VISYON\Documents\MusicGame\MusicGame\Assets\_Project\Audio\Music";
        public static string CacheDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\DefaultCompany\MusicGame\SongCache");

        static partial void Extra2(string cmd, string[] rest, ref int handled)
        {
            switch (cmd)
            {
                case "real": RealSongs(rest); handled = 0; return;
                case "featcmp": FeatureCompare(rest[0]); handled = 0; return;
                case "bench": Bench(rest[0], rest[1], rest.Length > 2 ? int.Parse(rest[2]) : AnalysisRunOptions.DefaultWorkers()); handled = 0; return;
                case "equiv": Equivalence(rest); handled = 0; return;
            }
            Extra3(cmd, rest, ref handled);
        }
        static partial void Extra3(string cmd, string[] rest, ref int handled);

        public static string[] ResolveWavs(string[] names)
        {
            var all = Directory.GetFiles(MusicDir, "*.wav").OrderBy(f => f).ToArray();
            if (names.Length == 0) return all;
            if (names[0] == "first" && names.Length > 1) return all.Take(int.Parse(names[1])).ToArray();
            return names.Select(n => File.Exists(n) ? n : all.First(f => Path.GetFileName(f).StartsWith(n))).ToArray();
        }

        public static float[] LoadMono(string wav, out int sr, out int channels, out int frames)
        {
            var w = WavReader.Read(wav);
            WavReader.PeakNormalize(w.interleaved);
            sr = w.sampleRate; channels = w.channels; frames = w.Frames;
            return MusicAnalyzer.MixToMono(w.interleaved, w.channels);
        }

        static string CacheFor(string wav)
        {
            string name = Path.GetFileNameWithoutExtension(wav);
            if (!Directory.Exists(CacheDir)) return null;
            return Directory.GetFiles(CacheDir, "*.json").FirstOrDefault(f => Path.GetFileName(f).StartsWith(name + "_"));
        }

        static void RealSongs(string[] args)
        {
            var wavs = ResolveWavs(args);
            Console.WriteLine("song | dur s | legacy onsets (port / Unity cache) | legacy BPM (port/cache) | new events | /min | kick snare hat generic | multi | new BPM conf phase | new ms (par) ");
            var p = AnalysisParams.Default;
            var tot = new int[6];
            foreach (var wav in wavs)
            {
                var mono = LoadMono(wav, out int sr, out _, out _);
                var leg = LegacyAnalyzer.Analyze(mono, sr);
                var r = MusicAnalyzer.Analyze(mono, sr, p, new AnalysisRunOptions());
                int k = r.onsets.Count(e => e.Has(OnsetFamily.Kick)), s = r.onsets.Count(e => e.Has(OnsetFamily.Snare)),
                    h = r.onsets.Count(e => e.Has(OnsetFamily.HiHat)), g = r.onsets.Count(e => e.Has(OnsetFamily.Generic));
                int multi = r.onsets.Count(e => (e.families & (e.families - 1)) != 0);
                string cacheOn = "-", cacheBpm = "-";
                var cache = CacheFor(wav);
                if (cache != null)
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(cache));
                    cacheOn = doc.RootElement.GetProperty("onsetTimes").GetArrayLength().ToString();
                    cacheBpm = doc.RootElement.GetProperty("estimatedBPM").GetSingle().ToString("F0");
                }
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(wav),-40} | {r.duration,5:F0} | {leg.onsets.Length,5} / {cacheOn,5} | {leg.bpm,4:F0}/{cacheBpm,4} | {r.onsets.Length,5} | {r.onsets.Length / (r.duration / 60f),5:F0} | {k,4} {s,4} {h,4} {g,4} | {multi,4} | {r.estimatedBpm,6:F1} {r.bpmConfidence:F2} {r.beatPhase:F3} | {r.timings.totalMs,6:F0}");
                tot[0] += r.onsets.Length; tot[1] += k; tot[2] += s; tot[3] += h; tot[4] += g; tot[5] += leg.onsets.Length;
            }
            Console.WriteLine($"TOTAL new events {tot[0]} (kick {tot[1]}, snare {tot[2]}, hat {tot[3]}, generic {tot[4]}); legacy onsets {tot[5]}");
        }

        static double Corr(float[] a, float[] b, int offB, int n)
        {
            double ma = 0, mb = 0; for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i + offB]; }
            ma /= n; mb /= n; double num = 0, da = 0, db = 0;
            for (int i = 0; i < n; i++) { double x = a[i] - ma, y = b[i + offB] - mb; num += x * y; da += x * x; db += y * y; }
            return da > 0 && db > 0 ? num / Math.Sqrt(da * db) : double.NaN;
        }
        static double Mean(float[] a, int off, int n) { double m = 0; for (int i = 0; i < n; i++) m += a[i + off]; return m / n; }

        /// <summary>Legacy frame f (window START f·hop) ≡ new frame f + W/(2·hop) (window CENTRE).</summary>
        static void FeatureCompare(string name)
        {
            var wav = ResolveWavs(new[] { name })[0];
            var mono = LoadMono(wav, out int sr, out _, out _);
            var leg = LegacyAnalyzer.Analyze(mono, sr);
            var r = MusicAnalyzer.Analyze(mono, sr, AnalysisParams.Default, new AnalysisRunOptions());
            int off = (int)Math.Round(512.0 / (r.hopTime * sr)); // 1024/2 samples
            int n = Math.Min(leg.frames, r.frameCount - off);
            Console.WriteLine($"{Path.GetFileName(wav)} sr={sr} legacy frames {leg.frames} new frames {r.frameCount} (offset {off})");
            void Row(string label, float[] a, float[] b) => Console.WriteLine($"  {label,-14} corr {Corr(a, b, off, n),7:F4}   mean legacy {Mean(a, 0, n),10:G4}  new {Mean(b, off, n),10:G4}  ratio {Mean(b, off, n) / Mean(a, 0, n),6:F3}");
            Row("energy", leg.energy, r.energy);
            Row("centroid", leg.centroid, r.centroid);
            Row("flux", leg.flux, r.flux);
            Row("flatness", leg.flatness, r.flatness);
            Row("voice", leg.voice, r.voice);
            for (int b = 0; b < 6; b++) Row("band" + b, leg.bands[b], r.bands[b]);
            for (int b = 0; b < 24; b++) Row("visual" + b, leg.visual[b], r.visualBands[b]);
            var lc = new float[n]; var nc = new float[n + off];
            for (int c = 0; c < 12; c += 5) { for (int i = 0; i < n; i++) lc[i] = leg.chroma[i * 12 + c]; for (int i = 0; i < n + off; i++) nc[i] = r.chroma[i * 12 + c]; Row("chroma" + c, lc, nc); }
            // distinctness of the lowest visual bands (the DC/low-band collapse)
            for (int b = 0; b < 5; b++)
                Console.WriteLine($"  vis{b}-vs-vis{b + 1}: legacy corr {Corr(leg.visual[b], leg.visual[b + 1], 0, n),7:F4} identical-frames {Ident(leg.visual[b], leg.visual[b + 1], n),6:P0} | new corr {Corr(r.visualBands[b], r.visualBands[b + 1], 0, n),7:F4} identical {Ident(r.visualBands[b], r.visualBands[b + 1], n),6:P0}");
        }
        static double Ident(float[] a, float[] b, int n) { int c = 0; for (int i = 0; i < n; i++) if (a[i] == b[i]) c++; return (double)c / n; }

        static void Bench(string mode, string name, int workers)
        {
            var wav = ResolveWavs(new[] { name })[0];
            var mono = LoadMono(wav, out int sr, out int ch, out int frames);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            // Peak memory: managed heap sampled every 2 ms by a background thread (process working set
            // peaks are dominated by the WAV decode that precedes the measured region).
            double bestMs = double.MaxValue; long alloc = 0; long peakHeap = 0; long heap0 = 0; string extra = "";
            for (int run = 0; run < 3; run++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                heap0 = GC.GetTotalMemory(false);
                long localPeak = heap0; bool stop = false;
                var sampler = new System.Threading.Thread(() => { while (!System.Threading.Volatile.Read(ref stop)) { long m = GC.GetTotalMemory(false); if (m > localPeak) localPeak = m; System.Threading.Thread.Sleep(2); } });
                sampler.IsBackground = true; sampler.Start();
                long alloc0 = GC.GetTotalAllocatedBytes(true);
                var sw = Stopwatch.StartNew();
                if (mode == "legacy") { var leg = LegacyAnalyzer.Analyze(mono, sr); extra = $"onsets {leg.onsets.Length}"; }
                else
                {
                    var r = MusicAnalyzer.Analyze(mono, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = mode == "newseq" ? 1 : workers });
                    extra = $"events {r.onsets.Length} | {r.timings}";
                }
                double ms = sw.Elapsed.TotalMilliseconds;
                long a = GC.GetTotalAllocatedBytes(true) - alloc0;
                System.Threading.Volatile.Write(ref stop, true); sampler.Join();
                if (ms < bestMs) { bestMs = ms; }
                alloc = a; peakHeap = Math.Max(peakHeap, localPeak - heap0);
            }
            Console.WriteLine($"BENCH {mode,-7} w={(mode == "legacy" ? 1 : (mode == "newseq" ? 1 : workers))} {Path.GetFileNameWithoutExtension(wav),-36} dur {mono.Length / (double)sr,5:F0}s | wall(min of 3) {bestMs,7:F0} ms | alloc {alloc / 1048576.0,7:F1} MB | peak managed heap above baseline +{peakHeap / 1048576.0,6:F1} MB | {extra}");
        }

        static void Equivalence(string[] args)
        {
            var cases = new List<(string, float[], int)>();
            var b = new SignalBuilder(48000, 17); SynthTests.Cases().First(c => c.name.StartsWith("drum loop")).build(b);
            cases.Add(("synthetic drum loop 48k", b.x, 48000));
            var b2 = new SignalBuilder(44100, 17); SynthTests.Cases().First(c => c.name.StartsWith("drum loop")).build(b2);
            cases.Add(("synthetic drum loop 44.1k", b2.x, 44100));
            foreach (var wav in ResolveWavs(args.Length > 0 ? args : new[] { "first", "1" }))
            {
                var mono = LoadMono(wav, out int sr, out _, out _);
                cases.Add((Path.GetFileNameWithoutExtension(wav), mono, sr));
            }
            foreach (var (name, x, sr) in cases)
            {
                var refR = MusicAnalyzer.Analyze(x, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = 1, chunkFrames = 1 << 30 });
                foreach (var (w, ch) in new[] { (1, 1024), (2, 1024), (4, 1024), (8, 333), (16, 64), (4, 4096) })
                {
                    var r = MusicAnalyzer.Analyze(x, sr, AnalysisParams.Default, new AnalysisRunOptions { maxWorkers = w, chunkFrames = ch });
                    string diff = Diff(refR, r);
                    Console.WriteLine($"EQUIV {name,-36} workers {w,2} chunk {ch,5}: {(diff.Length == 0 ? "BITWISE IDENTICAL" : "DIFF " + diff)} ({r.timings.totalMs:F0} ms)");
                }
            }
        }

        public static string Diff(AnalysisResult a, AnalysisResult b)
        {
            var d = new List<string>();
            void A(string n, float[] x, float[] y)
            {
                if (x == null && y == null) return;
                if (x == null || y == null || x.Length != y.Length) { d.Add(n + " length"); return; }
                for (int i = 0; i < x.Length; i++) if (BitConverter.SingleToInt32Bits(x[i]) != BitConverter.SingleToInt32Bits(y[i])) { d.Add($"{n}[{i}]"); return; }
            }
            A("energy", a.energy, b.energy); A("centroid", a.centroid, b.centroid); A("flux", a.flux, b.flux); A("flatness", a.flatness, b.flatness);
            A("voice", a.voice, b.voice); A("chroma", a.chroma, b.chroma);
            for (int i = 0; i < a.bands.Length; i++) A("band" + i, a.bands[i], b.bands[i]);
            for (int i = 0; i < a.visualBands.Length; i++) A("visual" + i, a.visualBands[i], b.visualBands[i]);
            if (a.onsets.Length != b.onsets.Length) d.Add($"onset count {a.onsets.Length} vs {b.onsets.Length}");
            else for (int i = 0; i < a.onsets.Length; i++)
                if (!a.onsets[i].Equals(b.onsets[i])) { d.Add("onset " + i); break; }
            if (a.estimatedBpm != b.estimatedBpm || a.beatPhase != b.beatPhase) d.Add("tempo");
            return string.Join(", ", d);
        }
    }
}
