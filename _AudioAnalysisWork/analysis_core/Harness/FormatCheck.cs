using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public static partial class Program
    {
        static partial void Extra8(string cmd, string[] rest, ref int handled)
        {
            if (cmd == "format") { FormatCheck(rest); handled = 0; return; }
            if (cmd == "halfcheck") { HalfCheck(); Console.WriteLine("crc32 self-test: " + (CrcSelfTest.Run() ? "OK" : "FAIL")); handled = 0; return; }
            if (cmd == "misc") { Misc.Run(); handled = 0; return; }
            Console.WriteLine("unknown command " + cmd);
        }

        static void HalfCheck()
        {
            // exhaustive round trip of all 65536 halves (finite) and a few known floats
            int bad = 0;
            for (int h = 0; h < 65536; h++)
            {
                float f = HalfConv.ToFloat((ushort)h);
                if (float.IsNaN(f)) continue;
                if (HalfConv.ToHalf(f) != h) bad++;
            }
            Console.WriteLine($"half round-trip mismatches over all finite halves: {bad}; 1.0→{HalfConv.ToHalf(1f):X4} 65504→{HalfConv.ToHalf(65504f):X4} 6e-8→{HalfConv.ToFloat(HalfConv.ToHalf(6e-8f)):E3}");
        }

        static double Time(Action a, int reps = 5)
        {
            a(); double best = double.MaxValue;
            for (int i = 0; i < reps; i++) { var sw = Stopwatch.StartNew(); a(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds); }
            return best;
        }

        static long Compressed(byte[] d, bool brotli)
        {
            using var ms = new MemoryStream();
            using (Stream z = brotli ? new BrotliStream(ms, CompressionLevel.Optimal, true) : new DeflateStream(ms, CompressionLevel.Optimal, true)) z.Write(d, 0, d.Length);
            return ms.Length;
        }

        /// <summary>Writes the legacy SongProfileData JSON shape (pretty, 4-space) for the same result, so
        /// size/time are compared on equal content. JsonUtility formats floats with up to 9-17 digits;
        /// this uses "R" (shortest round-trip), so the real files are a bit bigger — see the cache column.</summary>
        static byte[] LegacyJson(AnalysisResult r)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("clipName", "song"); w.WriteNumber("clipSamples", r.sampleCount);
                w.WriteNumber("duration", r.duration); w.WriteNumber("sampleRate", r.sampleRate); w.WriteNumber("estimatedBPM", r.estimatedBpm);
                w.WriteNumber("averageEnergy", r.averageEnergy); w.WriteNumber("maxEnergy", r.maxEnergy); w.WriteNumber("analysisHopTime", r.hopTime);
                void Arr(string n, float[] a) { w.WritePropertyName(n); w.WriteStartArray(); if (a != null) foreach (var v in a) w.WriteNumberValue(v); w.WriteEndArray(); }
                Arr("energyEnvelope", r.energy); Arr("spectralCentroid", r.centroid); Arr("spectralFlux", r.flux); Arr("onsetTimes", r.OnsetTimes());
                w.WritePropertyName("bandEnvelopes"); w.WriteStartArray(); foreach (var b in r.bands) { w.WriteStartObject(); Arr("values", b); w.WriteEndObject(); } w.WriteEndArray();
                w.WritePropertyName("visualBandEnvelopes"); w.WriteStartArray(); foreach (var b in r.visualBands) { w.WriteStartObject(); Arr("values", b); w.WriteEndObject(); } w.WriteEndArray();
                Arr("spectralFlatness", r.flatness); Arr("chromaFlat", r.chroma); Arr("voiceProbability", r.voice);
                w.WriteEndObject();
            }
            return ms.ToArray();
        }

        /// <summary>Proxy for JsonUtility.FromJson: parse + materialise every float array.</summary>
        static int ParseJson(byte[] json)
        {
            using var doc = JsonDocument.Parse(json);
            int total = 0;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in p.Value.EnumerateArray())
                    {
                        if (e.ValueKind == JsonValueKind.Number) { total++; e.GetSingle(); }
                        else if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("values", out var vals))
                        { var a = new float[vals.GetArrayLength()]; int i = 0; foreach (var v in vals.EnumerateArray()) a[i++] = v.GetSingle(); total += a.Length; }
                    }
                }
            }
            return total;
        }

        static void FormatCheck(string[] args)
        {
            var tags = new[] { new TagScore("electronic", 0.61f), new TagScore("dance", 0.42f), new TagScore("pop", 0.33f) };
            Console.WriteLine("song | real cache JSON MB (STJ parse ms) | same-content JSON MB (write/parse ms) | binary f32 MB (write/read ms) deflate MB brotli MB | f16 MB");
            long sumBin = 0, sumJson = 0;
            foreach (var wav in ResolveWavs(args.Length > 0 ? args : new[] { "first", "4" }))
            {
                var mono = LoadMono(wav, out int sr, out int ch, out int frames);
                var r = MusicAnalyzer.Analyze(mono, sr, AnalysisParams.Default, new AnalysisRunOptions());
                var h = new ProfileHeader
                {
                    pipelineVersion = AnalysisVersions.Pipeline, paramsHash = AnalysisParams.Default.Hash, tagModelVersion = 1, tagReuseKey = 0x1234,
                    songId = Path.GetFileNameWithoutExtension(wav), audioIdentity = StableHash.HashPcm(mono, 1, sr), clipSamples = frames, clipChannels = ch, clipFrequency = sr,
                };
                byte[] bin = null;
                double tW = Time(() => bin = ProfileBinary.Write(h, r, tags));
                AnalysisResult back = null; TagScore[] tb = null;
                double tR = Time(() => back = ProfileBinary.Read(bin, out _, out tb));
                double tRnoCrc = Time(() => ProfileBinary.Read(bin, out _, out _, false));
                double tHdr = Time(() => ProfileBinary.ReadHeader(bin, out _), 20);
                string rt = Diff(r, back);
                bool tagsOk = tb != null && tb.Length == tags.Length && tb.Zip(tags, (a, b) => a.tag == b.tag && a.score == b.score).All(x => x) && back.beatTimes.SequenceEqual(r.beatTimes);
                byte[] half = ProfileBinary.Write(new ProfileHeader { songId = h.songId }, r, tags, true);

                byte[] json = null;
                double tJw = Time(() => json = LegacyJson(r), 2);
                double tJp = Time(() => ParseJson(json), 2);
                string cache = CacheFor(wav); string cacheCol = "-";
                if (cache != null) { var cb = File.ReadAllBytes(cache); double tc = Time(() => ParseJson(cb), 2); cacheCol = $"{cb.Length / 1048576.0,5:F1} ({tc,5:F0})"; sumJson += cb.Length; }
                sumBin += bin.Length;
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(wav),-36} | {cacheCol} | {json.Length / 1048576.0,5:F1} ({tJw,4:F0}/{tJp,4:F0}) | {bin.Length / 1048576.0,5:F2} ({tW,4:F1}/{tR,4:F1}; no-CRC {tRnoCrc,4:F1}; header {tHdr * 1000,4:F0} µs) {Compressed(bin, false) / 1048576.0,5:F2} {Compressed(bin, true) / 1048576.0,5:F2} | {half.Length / 1048576.0,5:F2} | round-trip {(rt.Length == 0 && tagsOk ? "lossless OK" : "DIFF " + rt)}");

                // half precision error report, per array family
                var hb = ProfileBinary.Read(half, out _, out _);
                void Err(string n, float[] a, float[] b)
                {
                    double maxRel = 0, maxAbsOverMax = 0, max = a.Max(Math.Abs);
                    for (int i = 0; i < a.Length; i++)
                    {
                        double d = Math.Abs(a[i] - b[i]);
                        if (Math.Abs(a[i]) > 1e-6) maxRel = Math.Max(maxRel, d / Math.Abs(a[i]));
                        if (max > 0) maxAbsOverMax = Math.Max(maxAbsOverMax, d / max);
                    }
                    int flushed = 0; for (int i = 0; i < a.Length; i++) if (a[i] != 0 && b[i] == 0) flushed++;
                    Console.WriteLine($"      f16 {n,-12} max rel err {maxRel:E2}  max |err|/max {maxAbsOverMax:E2}  values flushed to 0: {flushed}");
                }
                if (wav == ResolveWavs(args.Length > 0 ? args : new[] { "first", "4" })[0])
                {
                    Err("energy", r.energy, hb.energy); Err("centroid", r.centroid, hb.centroid); Err("flux", r.flux, hb.flux);
                    Err("flatness", r.flatness, hb.flatness); Err("voice", r.voice, hb.voice); Err("chroma", r.chroma, hb.chroma);
                    Err("band0", r.bands[0], hb.bands[0]); Err("band5", r.bands[5], hb.bands[5]); Err("visual0", r.visualBands[0], hb.visualBands[0]); Err("visual23", r.visualBands[23], hb.visualBands[23]);
                }
            }
            Console.WriteLine($"TOTAL binary {sumBin / 1048576.0:F1} MB vs real cache JSON {sumJson / 1048576.0:F1} MB for the same songs");

            // Robustness
            {
                var mono = LoadMono(ResolveWavs(new[] { "first", "1" })[0], out int sr, out _, out int frames);
                var r = MusicAnalyzer.Analyze(mono, sr, AnalysisParams.Default, new AnalysisRunOptions());
                var bin = ProfileBinary.Write(new ProfileHeader { songId = "x", paramsHash = AnalysisParams.Default.Hash, pipelineVersion = AnalysisVersions.Pipeline, audioIdentity = 42, clipSamples = frames, clipFrequency = sr }, r, null);
                string Try(byte[] d) { try { ProfileBinary.Read(d, out _, out _); return "accepted"; } catch (Exception e) { return e.GetType().Name + ": " + e.Message; } }
                var flip = (byte[])bin.Clone(); flip[flip.Length / 2] ^= 0x10;
                var trunc = bin.Take(bin.Length - 100).ToArray();
                var magic = (byte[])bin.Clone(); magic[0] = (byte)'X';
                var ver = (byte[])bin.Clone(); ver[4] = 99;
                Console.WriteLine("corrupt body byte  → " + Try(flip));
                Console.WriteLine("truncated          → " + Try(trunc));
                Console.WriteLine("bad magic          → " + Try(magic));
                Console.WriteLine("future format ver  → " + Try(ver));
                var hdr = ProfileBinary.ReadHeader(bin, out _);
                Console.WriteLine($"compat same params/audio: {hdr.IsCompatible(AnalysisParams.Default, 42, frames, sr)}; other audio: {hdr.IsCompatible(AnalysisParams.Default, 43, frames, sr)}; other params: {hdr.IsCompatible(AnalysisParams.Create(new AnalysisSettings { minBpm = 61 }), 42, frames, sr)}");
                Console.WriteLine($"params hash default {AnalysisParams.Default.Hash:X16}; same settings again {AnalysisParams.Create(new AnalysisSettings()).Hash:X16}");
            }
        }
    }
}
namespace Harness
{
    public static class CrcSelfTest
    {
        public static bool Run()
        {
            // "123456789" → 0xCBF43926 (standard check value), plus random lengths vs bytewise reference
            var b = System.Text.Encoding.ASCII.GetBytes("123456789");
            if (MusicGame.Audio.Analysis.Crc32.Compute(b, 0, b.Length) != 0xCBF43926u) return false;
            var rng = new Rng(9);
            for (int n = 0; n < 300; n++)
            {
                var d = new byte[n]; for (int i = 0; i < n; i++) d[i] = (byte)rng.Next();
                uint c = 0xFFFFFFFFu;
                for (int i = 0; i < n; i++) { c ^= d[i]; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; }
                if ((c ^ 0xFFFFFFFFu) != MusicGame.Audio.Analysis.Crc32.Compute(d, 0, n)) return false;
            }
            return true;
        }
    }
}
