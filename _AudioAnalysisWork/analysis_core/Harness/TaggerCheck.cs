using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MusicGame.Audio.Analysis;

namespace Harness
{
    /// <summary>Verbatim port of SentisMusicTagger.Tag's preprocessing (up to the batch tensor data).</summary>
    public static class LegacyTaggerPreprocessing
    {
        const int TargetSampleRate = 16000, FftSize = 512, HopSize = 256, MelBins = 96, FreqBins = 257, PatchFrames = 187;

        public static float[] Batch(float[] monoSamples, int sourceSampleRate, float[] melFilterbank, out int numPatches)
        {
            float[] resampled = Resample(monoSamples, sourceSampleRate, TargetSampleRate);
            int pad = FftSize / 2;
            float[] padded = ReflectPad(resampled, pad);
            int numFrames = 1 + Mathf.Max(0, (padded.Length - FftSize) / HopSize);
            numPatches = 0;
            if (numFrames < PatchFrames) return null;
            var melLog = new float[numFrames * MelBins];
            var frame = new float[FftSize];
            for (int f = 0; f < numFrames; f++)
            {
                Array.Copy(padded, f * HopSize, frame, 0, FftSize);
                float[] power = LegacyFFT.ComputePowerSpectrum(frame, LegacyWindow.HannPeriodic);
                int rowOffset = f * MelBins;
                for (int m = 0; m < MelBins; m++)
                {
                    float sum = 0f; int fbOffset = m * FreqBins;
                    for (int b = 0; b < FreqBins; b++) sum += melFilterbank[fbOffset + b] * power[b];
                    melLog[rowOffset + m] = Mathf.Log10(10000f * sum + 1f);
                }
            }
            var patchStarts = new List<int>();
            int lastFrame = numFrames - PatchFrames + 1;
            for (int t = 0; t < lastFrame; t += PatchFrames) patchStarts.Add(t);
            numPatches = patchStarts.Count;
            if (numPatches == 0) return null;
            var batch = new float[numPatches * PatchFrames * MelBins];
            for (int p = 0; p < numPatches; p++) Array.Copy(melLog, patchStarts[p] * MelBins, batch, p * PatchFrames * MelBins, PatchFrames * MelBins);
            return batch;
        }

        static float[] Resample(float[] input, int srcRate, int dstRate)
        {
            if (srcRate == dstRate || input.Length == 0) return input;
            int dstLen = (int)((long)input.Length * dstRate / srcRate);
            var output = new float[dstLen];
            double ratio = (double)srcRate / dstRate;
            for (int i = 0; i < dstLen; i++)
            {
                double srcPos = i * ratio;
                int i0 = Mathf.Clamp((int)srcPos, 0, input.Length - 1);
                int i1 = Mathf.Min(i0 + 1, input.Length - 1);
                float frac = (float)(srcPos - i0);
                output[i] = Mathf.Lerp(input[i0], input[i1], frac);
            }
            return output;
        }

        static float[] ReflectPad(float[] x, int pad)
        {
            int n = x.Length;
            var padded = new float[n + 2 * pad];
            for (int i = 0; i < pad; i++) padded[i] = x[Mathf.Clamp(pad - i, 0, n - 1)];
            Array.Copy(x, 0, padded, pad, n);
            for (int i = 0; i < pad; i++) padded[pad + n + i] = x[Mathf.Clamp(n - 2 - i, 0, n - 1)];
            return padded;
        }
    }

    public static partial class Program
    {
        public const string FilterbankPath = @"C:\Users\VISYON\Documents\MusicGame\MusicGame\Assets\_Project\Resources\MusicTagging\mel_filterbank_96x257.bytes";

        static partial void Extra7(string cmd, string[] rest, ref int handled)
        {
            if (cmd == "tagger") { TaggerCheck(rest); handled = 0; return; }
            Extra8(cmd, rest, ref handled);
        }
        static partial void Extra8(string cmd, string[] rest, ref int handled);

        static void TaggerCheck(string[] args)
        {
            var fb = MusicnnPreprocessor.ParseFilterbank(File.ReadAllBytes(FilterbankPath));
            var pre = new MusicnnPreprocessor(fb);
            int nz = 0; for (int i = 0; i < fb.Length; i++) if (fb[i] != 0f) nz++;
            Console.WriteLine($"filterbank: {fb.Length} floats, {nz} non-zero ({100.0 * nz / fb.Length:F1} %), hash {pre.FilterbankHash:X16}");

            var inputs = new List<(string, float[], int)>();
            var rng = new Rng(3);
            foreach (int sr in new[] { 48000, 44100, 16000, 22050 })
            {
                var b = new SignalBuilder(sr, 7.3); for (int k = 0; k < 12; k++) { b.Kick(0.3 + k * 0.5); b.Hat(0.55 + k * 0.5); b.Pluck(0.4 + k * 0.5, 220 + 20 * k); }
                inputs.Add(($"synthetic {sr} Hz 7.3 s", b.x, sr));
            }
            inputs.Add(("too short (2.9 s @48k)", new float[(int)(2.9 * 48000)], 48000));
            foreach (var wav in ResolveWavs(args.Length > 0 ? args : new[] { "first", "2" }))
            {
                var mono = LoadMono(wav, out int sr, out _, out _);
                inputs.Add((Path.GetFileNameWithoutExtension(wav), mono, sr));
            }

            foreach (var (name, x, sr) in inputs)
            {
                long a0 = GC.GetTotalAllocatedBytes(true);
                var sw = Stopwatch.StartNew();
                var refB = LegacyTaggerPreprocessing.Batch(x, sr, fb, out int np0);
                double tRef = sw.Elapsed.TotalMilliseconds; long aRef = GC.GetTotalAllocatedBytes(true) - a0;

                a0 = GC.GetTotalAllocatedBytes(true); sw.Restart();
                var seqB = pre.BuildBatch(x, sr, new AnalysisRunOptions { maxWorkers = 1 }, out int np1);
                double tSeq = sw.Elapsed.TotalMilliseconds; long aSeq = GC.GetTotalAllocatedBytes(true) - a0;

                sw.Restart();
                var parB = pre.BuildBatch(x, sr, new AnalysisRunOptions { maxWorkers = 4 }, out int np2);
                double tPar = sw.Elapsed.TotalMilliseconds;

                if (refB == null || seqB == null)
                {
                    Console.WriteLine($"{name,-40} ref patches {np0} new {np1}/{np2} (null batches: ref {refB == null} new {seqB == null})");
                    continue;
                }
                double maxAbs = 0, sumAbs = 0, maxRel = 0;
                bool parSame = parB.Length == seqB.Length;
                for (int i = 0; i < refB.Length; i++)
                {
                    double d = Math.Abs(refB[i] - seqB[i]);
                    maxAbs = Math.Max(maxAbs, d); sumAbs += d;
                    if (Math.Abs(refB[i]) > 1e-3) maxRel = Math.Max(maxRel, d / Math.Abs(refB[i]));
                    if (parSame && BitConverter.SingleToInt32Bits(parB[i]) != BitConverter.SingleToInt32Bits(seqB[i])) parSame = false;
                }
                double range = refB.Max() - refB.Min();
                Console.WriteLine($"{name,-40} patches {np0}/{np1} | log-mel max|Δ| {maxAbs:E2} mean|Δ| {sumAbs / refB.Length:E2} (value range {range:F2}) max rel {maxRel:E2} | par==seq bitwise {parSame} | ref {tRef,6:F0} ms {aRef / 1048576.0,6:F1} MB | new seq {tSeq,5:F0} ms {aSeq / 1048576.0,5:F1} MB | new 4w {tPar,5:F0} ms");
            }
        }
    }
}
