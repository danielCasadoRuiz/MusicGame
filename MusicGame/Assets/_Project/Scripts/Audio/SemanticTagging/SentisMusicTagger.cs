using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Unity.InferenceEngine;

/// <summary>
/// IMusicTagger implementation backed by musicnn's "MSD_musicnn" model (Jordi Pons, ISC
/// License — https://github.com/jordipons/musicnn), converted offline to ONNX and run locally
/// via Unity Sentis / Inference Engine (com.unity.ai.inference). 100% on-device: no server, no
/// network call, no per-inference cost.
///
/// Everything here is a byte-for-byte reproduction of musicnn's own preprocessing
/// (musicnn/extractor.py's batch_data): resample to 16 kHz, centered STFT (reflect-padded,
/// periodic Hann, n_fft=512, hop=256), project through the model's own trained 96-band mel
/// filterbank (baked from librosa.filters.mel — see mel_filterbank_96x257.bytes — so its exact
/// triangular-filter/normalization math never has to be reimplemented), then
/// log10(10000*mel+1) compression, batched into non-overlapping 187-frame (~3s) patches exactly
/// like musicnn's own extractor. The ONLY deliberate deviation from bit-exact parity is the
/// initial resample step, which uses simple linear interpolation instead of librosa's
/// higher-order resampler — negligible after mel-band averaging over many 3s patches, and
/// verified against real songs (see conversion notes) to produce sensible, stable tags.
/// </summary>
public class SentisMusicTagger : IMusicTagger
{
    /// <summary>Bump this when the model or preprocessing changes, so SongCache knows to
    /// re-tag a song even though its other cached analysis is still perfectly valid.</summary>
    public const int ModelVersion = 1;

    private const int TargetSampleRate = 16000;
    private const int FftSize          = 512;
    private const int HopSize          = 256;
    private const int MelBins          = 96;
    private const int FreqBins         = FftSize / 2 + 1; // 257
    private const int PatchFrames      = 187;             // 3s @ 16kHz/hop256 — MSD_musicnn/config.json "n_frames"

    // MSD_musicnn's own 50-tag vocabulary (musicnn/configuration.py MSD_LABELS), trained on the
    // Million Song Dataset's community tags — NOT the same vocabulary as MTT_musicnn.
    private static readonly string[] Labels =
    {
        "rock","pop","alternative","indie","electronic","female vocalists","dance","00s",
        "alternative rock","jazz","beautiful","metal","chillout","male vocalists","classic rock",
        "soul","indie rock","Mellow","electronica","80s","folk","90s","chill","instrumental",
        "punk","oldies","blues","hard rock","ambient","acoustic","experimental","female vocalist",
        "guitar","Hip-Hop","70s","party","country","easy listening","sexy","catchy","funk",
        "electro","heavy metal","Progressive rock","60s","rnb","indie pop","sad","House","happy",
    };

    private readonly AudioAnalysisConfig _config;
    private readonly float[]  _melFilterbank; // flattened [MelBins * FreqBins], row-major
    private readonly Model    _model;
    private readonly Worker   _worker;

    public bool IsAvailable { get; }

    public SentisMusicTagger(AudioAnalysisConfig config)
    {
        _config = config;

        try
        {
            var modelAsset      = Resources.Load<ModelAsset>("MusicTagging/msd_musicnn");
            var filterbankAsset = Resources.Load<TextAsset>("MusicTagging/mel_filterbank_96x257");

            if (modelAsset == null || filterbankAsset == null)
            {
                Debug.LogWarning("[MUSIC TAGGER] Model or mel-filterbank asset missing under " +
                                 "Resources/MusicTagging — semantic tagging disabled.");
                IsAvailable = false;
                return;
            }

            _melFilterbank = LoadFilterbank(filterbankAsset.bytes);
            _model         = ModelLoader.Load(modelAsset);
            _worker        = new Worker(_model, BackendType.CPU);
            IsAvailable    = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MUSIC TAGGER] Failed to initialize Sentis model: {e}");
            IsAvailable = false;
        }
    }

    private static float[] LoadFilterbank(byte[] bytes)
    {
        int count = MelBins * FreqBins;
        var flat  = new float[count];
        Buffer.BlockCopy(bytes, 0, flat, 0, count * sizeof(float));
        return flat;
    }

    public void Dispose()
    {
        _worker?.Dispose();
    }

    // ── Main pipeline ─────────────────────────────────────────────────────────

    public IEnumerator Tag(float[] monoSamples, int sourceSampleRate, Action<MusicTagScore[]> onComplete)
    {
        if (!IsAvailable)
        {
            onComplete?.Invoke(Array.Empty<MusicTagScore>());
            yield break;
        }

        float t0 = Time.realtimeSinceStartup;

        float[] resampled = Resample(monoSamples, sourceSampleRate, TargetSampleRate);

        int pad    = FftSize / 2;
        float[] padded    = ReflectPad(resampled, pad);
        int numFrames     = 1 + Mathf.Max(0, (padded.Length - FftSize) / HopSize);

        if (numFrames < PatchFrames)
        {
            onComplete?.Invoke(Array.Empty<MusicTagScore>()); // song too short for a single 3s patch
            yield break;
        }

        var melLog = new float[numFrames * MelBins];
        var frame  = new float[FftSize];

        const int yieldEveryFrames = 300;
        for (int f = 0; f < numFrames; f++)
        {
            Array.Copy(padded, f * HopSize, frame, 0, FftSize);
            float[] power = OfflineFFT.ComputePowerSpectrum(frame, FFTWindowType.HannPeriodic);

            int rowOffset = f * MelBins;
            for (int m = 0; m < MelBins; m++)
            {
                float sum       = 0f;
                int   fbOffset = m * FreqBins;
                for (int b = 0; b < FreqBins; b++)
                    sum += _melFilterbank[fbOffset + b] * power[b];
                melLog[rowOffset + m] = Mathf.Log10(10000f * sum + 1f);
            }

            if (f % yieldEveryFrames == 0) yield return null;
        }

        // Non-overlapping 187-frame patches — matches musicnn's own batch_data (overlap == n_frames)
        var patchStarts = new List<int>();
        int lastFrame   = numFrames - PatchFrames + 1;
        for (int t = 0; t < lastFrame; t += PatchFrames) patchStarts.Add(t);

        int numPatches = patchStarts.Count;
        if (numPatches == 0)
        {
            onComplete?.Invoke(Array.Empty<MusicTagScore>());
            yield break;
        }

        var batch = new float[numPatches * PatchFrames * MelBins];
        for (int p = 0; p < numPatches; p++)
            Array.Copy(melLog, patchStarts[p] * MelBins, batch, p * PatchFrames * MelBins, PatchFrames * MelBins);

        yield return null;

        float tInferStart = Time.realtimeSinceStartup;
        float[] taggram;
        using (var input = new Tensor<float>(new TensorShape(numPatches, PatchFrames, MelBins), batch))
        {
            _worker.Schedule(input);
            // PeekOutput() returns a tensor owned by the worker itself — must NOT Dispose it
            // here (that's only for ReadbackAndClone()'d/TakeOwnership()'d tensors); it lives
            // and dies with _worker, which the caller disposes once tagging is fully done.
            var output = _worker.PeekOutput() as Tensor<float>;
            taggram = output.DownloadToArray();
        }
        float inferenceTime = Time.realtimeSinceStartup - tInferStart;

        int numLabels = Labels.Length;
        var meanScores = new float[numLabels];
        for (int p = 0; p < numPatches; p++)
            for (int l = 0; l < numLabels; l++)
                meanScores[l] += taggram[p * numLabels + l];
        for (int l = 0; l < numLabels; l++) meanScores[l] /= numPatches;

        var order = new int[numLabels];
        for (int l = 0; l < numLabels; l++) order[l] = l;
        Array.Sort(order, (a, b) => meanScores[b].CompareTo(meanScores[a]));

        int   topN     = Mathf.Clamp(_config.semanticTagTopN, 1, numLabels);
        float minScore = _config.semanticTagMinScore;

        var result = new List<MusicTagScore>(topN);
        for (int i = 0; i < topN; i++)
        {
            int idx = order[i];
            if (meanScores[idx] < minScore) continue;
            result.Add(new MusicTagScore(Labels[idx], meanScores[idx]));
        }

        float totalTime = Time.realtimeSinceStartup - t0;
        LogResult(result, numPatches, inferenceTime, totalTime);

        onComplete?.Invoke(result.ToArray());
    }

    private static void LogResult(List<MusicTagScore> tags, int numPatches, float inferenceTime, float totalTime)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[MUSIC TAGGER]");
        sb.AppendLine();
        sb.AppendLine("Model: musicnn MSD_musicnn (Sentis/ONNX, CPU backend)");
        sb.AppendLine();
        for (int i = 0; i < tags.Count; i++)
            sb.AppendLine($"{i + 1}. {tags[i].tag,-15} {tags[i].score:F3}");
        sb.AppendLine();
        sb.AppendLine($"Patches: {numPatches}   Inference: {inferenceTime * 1000f:F0} ms   " +
                       $"Total (incl. preprocessing): {totalTime * 1000f:F0} ms");
        Debug.Log(sb.ToString());
    }

    // ── Preprocessing helpers ─────────────────────────────────────────────────

    private static float[] Resample(float[] input, int srcRate, int dstRate)
    {
        if (srcRate == dstRate || input.Length == 0) return input;

        int dstLen = (int)((long)input.Length * dstRate / srcRate);
        var output = new float[dstLen];
        double ratio = (double)srcRate / dstRate;

        for (int i = 0; i < dstLen; i++)
        {
            double srcPos = i * ratio;
            int    i0     = Mathf.Clamp((int)srcPos, 0, input.Length - 1);
            int    i1     = Mathf.Min(i0 + 1, input.Length - 1);
            float  frac   = (float)(srcPos - i0);
            output[i] = Mathf.Lerp(input[i0], input[i1], frac);
        }
        return output;
    }

    /// <summary>numpy/librosa "reflect" padding — output[i] = x[pad-i] on the left and the
    /// mirrored equivalent on the right, i.e. the edge sample itself is not duplicated.</summary>
    private static float[] ReflectPad(float[] x, int pad)
    {
        int n = x.Length;
        var padded = new float[n + 2 * pad];
        for (int i = 0; i < pad; i++)
            padded[i] = x[Mathf.Clamp(pad - i, 0, n - 1)];
        Array.Copy(x, 0, padded, pad, n);
        for (int i = 0; i < pad; i++)
            padded[pad + n + i] = x[Mathf.Clamp(n - 2 - i, 0, n - 1)];
        return padded;
    }
}
