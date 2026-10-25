using System;
using System.Threading;

namespace MusicGame.Audio.Analysis
{
    /// <summary>
    /// Pure-C# port of SentisMusicTagger's preprocessing (musicnn MSD_musicnn input): linear resample to
    /// 16 kHz, reflect padding, centred STFT (periodic Hann, n_fft 512, hop 256), power spectrum, the
    /// model's 96×257 mel filterbank, log10(10000·mel + 1), non-overlapping 187-frame patches.
    /// Same math as the current implementation (verified to float tolerance by the harness), but:
    /// precomputed window/FFT tables, sparse filterbank rows, resampling + reflection done on the fly
    /// per frame (no resampled/padded copies), only the frames that end up in a patch are computed,
    /// written straight into the model batch, chunk-parallel with per-worker scratch.
    /// Runs on any thread; the Sentis inference itself must stay on the main thread.
    /// </summary>
    public sealed class MusicnnPreprocessor
    {
        public const int TargetSampleRate = 16000;
        public const int FftSize = 512;
        public const int HopSize = 256;
        public const int MelBins = 96;
        public const int FreqBins = FftSize / 2 + 1; // 257
        public const int PatchFrames = 187;
        /// <summary>Bump when the preprocessing math changes (part of the tag reuse key).</summary>
        public const int PreprocessingVersion = 1;

        private readonly float[] _fb;          // dense, row-major [mel][bin] (kept for hashing/reference)
        private readonly int[] _rowStart, _rowLen;
        private readonly float[] _window;
        private readonly RealFftPlan _plan;

        /// <summary>Stable hash of the filterbank values (part of the tag reuse key).</summary>
        public ulong FilterbankHash { get; }

        public MusicnnPreprocessor(float[] filterbank)
        {
            if (filterbank == null || filterbank.Length != MelBins * FreqBins)
                throw new ArgumentException("filterbank must be 96×257 floats");
            _fb = (float[])filterbank.Clone();
            _rowStart = new int[MelBins];
            _rowLen = new int[MelBins];
            for (int m = 0; m < MelBins; m++)
            {
                int first = -1, last = -1;
                for (int b = 0; b < FreqBins; b++)
                    if (_fb[m * FreqBins + b] != 0f) { if (first < 0) first = b; last = b; }
                _rowStart[m] = Math.Max(0, first);
                _rowLen[m] = first < 0 ? 0 : last - first + 1;
            }
            _window = WindowTables.Get(WindowKind.HannPeriodic, FftSize);
            _plan = RealFftPlan.Get(FftSize);
            var h = StableHash.Create();
            for (int i = 0; i < _fb.Length; i++) h.Add(_fb[i]);
            FilterbankHash = h.Value;
        }

        /// <summary>Raw little-endian float32 bytes (the Resources/MusicTagging/mel_filterbank_96x257 asset).</summary>
        public static float[] ParseFilterbank(byte[] bytes)
        {
            int count = MelBins * FreqBins;
            if (bytes == null || bytes.Length < count * 4) throw new ArgumentException("filterbank asset too small");
            var flat = new float[count];
            Buffer.BlockCopy(bytes, 0, flat, 0, count * 4);
            return flat;
        }

        public static int ResampledLength(int monoLength, int sourceRate)
            => sourceRate == TargetSampleRate ? monoLength : (int)((long)monoLength * TargetSampleRate / sourceRate);

        public static int FrameCount(int monoLength, int sourceRate)
        {
            int padded = ResampledLength(monoLength, sourceRate) + FftSize;
            return 1 + Math.Max(0, (padded - FftSize) / HopSize);
        }

        /// <summary>Number of non-overlapping 187-frame patches (0 = too short to tag).</summary>
        public static int PatchCount(int monoLength, int sourceRate)
        {
            int frames = FrameCount(monoLength, sourceRate);
            return frames < PatchFrames ? 0 : (frames - PatchFrames) / PatchFrames + 1;
        }

        private sealed class Scratch
        {
            public readonly float[] frame = new float[FftSize];
            public readonly float[] re = new float[FreqBins], im = new float[FreqBins], power = new float[FreqBins];
            public readonly FftScratch fft = new FftScratch(FftSize);
        }

        /// <summary>
        /// Builds the model input [numPatches, 187, 96] (row-major, frames of consecutive patches are
        /// contiguous because patches are non-overlapping and start at frame 0). Returns null if the
        /// audio is shorter than one patch.
        /// </summary>
        public float[] BuildBatch(float[] mono, int sourceRate, AnalysisRunOptions opt, out int numPatches)
        {
            numPatches = PatchCount(mono.Length, sourceRate);
            if (numPatches == 0) return null;
            int frames = numPatches * PatchFrames;
            var batch = new float[frames * MelBins];
            int n = ResampledLength(mono.Length, sourceRate);
            double ratio = (double)sourceRate / TargetSampleRate;
            bool same = sourceRate == TargetSampleRate;
            var ct = opt?.cancellation ?? CancellationToken.None;

            ParallelChunks.Run(frames, 512, opt, () => new Scratch(), (a, b, s) =>
            {
                for (int f = a; f < b; f++)
                {
                    // Frame f covers padded[f·hop, f·hop + 512) = resampled[f·hop − 256, …) with reflection.
                    int start = f * HopSize - FftSize / 2;
                    for (int i = 0; i < FftSize; i++)
                    {
                        int j = start + i;
                        // numpy "reflect" (edge not repeated), clamped like the reference for tiny inputs
                        if (j < 0) j = -j;
                        else if (j >= n) j = 2 * n - 2 - j;
                        if (j < 0) j = 0; else if (j > n - 1) j = n - 1;
                        float v = same ? mono[j] : ResampleAt(mono, j, ratio);
                        s.frame[i] = v * _window[i];
                    }
                    _plan.Forward(s.frame, s.re, s.im, s.fft);
                    for (int k = 0; k < FreqBins; k++) s.power[k] = s.re[k] * s.re[k] + s.im[k] * s.im[k];
                    int o = f * MelBins;
                    for (int m = 0; m < MelBins; m++)
                    {
                        float sum = 0f;
                        int fo = m * FreqBins + _rowStart[m], k0 = _rowStart[m], len = _rowLen[m];
                        for (int q = 0; q < len; q++) sum += _fb[fo + q] * s.power[k0 + q];
                        batch[o + m] = (float)Math.Log10(10000f * sum + 1f);
                    }
                }
            });
            ct.ThrowIfCancellationRequested();
            return batch;
        }

        /// <summary>Sample i of the linear-interpolation resampler, identical to the reference
        /// (double source position, float lerp with clamped indices).</summary>
        private static float ResampleAt(float[] input, int i, double ratio)
        {
            double srcPos = i * ratio;
            int i0 = (int)srcPos;
            if (i0 < 0) i0 = 0; else if (i0 > input.Length - 1) i0 = input.Length - 1;
            int i1 = Math.Min(i0 + 1, input.Length - 1);
            float frac = (float)(srcPos - i0);
            if (frac < 0f) frac = 0f; else if (frac > 1f) frac = 1f;
            float a = input[i0], b = input[i1];
            return a + (b - a) * frac;
        }

        /// <summary>Key under which semantic tags may be reused across rhythm-pipeline changes: same
        /// audio, same model, same preprocessing, same filterbank, same tag selection settings.</summary>
        public ulong TagReuseKey(ulong audioIdentity, int modelVersion, int topN, float minScore)
        {
            var h = StableHash.Create();
            h.Add("musicnn-tags");
            h.Add(audioIdentity);
            h.Add(modelVersion);
            h.Add(PreprocessingVersion);
            h.Add(FilterbankHash);
            h.Add(topN);
            h.Add(minScore);
            return h.Value;
        }
    }
}
