using System;
using System.Collections.Generic;
using MusicGame.Audio.Analysis;
using Unity.InferenceEngine;
using UnityEngine;

/// <summary>
/// Semantic music tagging with musicnn's "MSD_musicnn" model (Jordi Pons, ISC License —
/// https://github.com/jordipons/musicnn), converted offline to ONNX and run locally via Unity
/// Inference Engine (com.unity.ai.inference), CPU backend. 100% on-device, no network.
///
/// Split by thread affinity (the song analysis pipeline drives it, see SongAnalysisPipeline):
///   - preprocessing (resample to 16 kHz, centred STFT, the model's own 96×257 mel filterbank,
///     log10(10000·mel+1), non-overlapping 187-frame patches) is pure C#: MusicnnPreprocessor, run on
///     a WORKER thread;
///   - the Model is loaded ONCE and cached for the app lifetime (<see cref="TryGetModel"/>);
///   - each request creates its OWN Worker + input tensor on the MAIN thread (<see cref="Inference"/>),
///     schedules it and polls the readback without blocking (IsReadbackRequestDone); the Worker,
///     input and output are disposed once the result is read — or, when the request is cancelled
///     while the jobs still run, handed to a deferred-disposal list pumped every frame, so
///     cancelling never blocks the main thread on in-flight jobs;
///   - tag selection (mean over patches, top-N, min score) is pure and runs anywhere.
/// The only deliberate deviation from musicnn's preprocessing is the linear-interpolation resample
/// (instead of librosa's), negligible after mel averaging over 3 s patches.
/// </summary>
public static class SentisMusicTagger
{
    /// <summary>Bump when the model or the tag post-processing changes: part of the tag reuse key, so
    /// cached tags are recomputed (without re-running the rhythm analysis).</summary>
    public const int ModelVersion = 1;

    private const string ModelResource      = "MusicTagging/msd_musicnn";
    private const string FilterbankResource = "MusicTagging/mel_filterbank_96x257";

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

    /// <summary>The fixed 50-tag vocabulary, in model order (SongSimilarityVector's tag block).</summary>
    public static IReadOnlyList<string> TagLabels => Labels;

    /// <summary>Diagnostics: model loads / Workers created in this process (probes assert that loading a
    /// published catalog song never touches the semantic model).</summary>
    public static int ModelLoadCount { get; private set; }
    public static int WorkerCreateCount { get; private set; }

    private static Model               s_model;
    private static bool                s_modelFailed;
    private static MusicnnPreprocessor s_preprocessor;
    private static bool                s_preprocessorFailed;
    private static readonly List<Inference> s_deferred = new();

    /// <summary>MAIN THREAD. The cached model (loaded on first use, kept for the app lifetime: a
    /// few MB, reused by every analysis). False if the asset is missing or failed to load.</summary>
    public static bool TryGetModel(out Model model)
    {
        model = s_model;
        if (model != null) return true;
        if (s_modelFailed) return false;
        try
        {
            var asset = Resources.Load<ModelAsset>(ModelResource);
            if (asset == null)
            {
                Debug.LogWarning($"[MUSIC TAGGER] Resources/{ModelResource} missing — semantic tagging disabled.");
                s_modelFailed = true;
                return false;
            }
            s_model = model = ModelLoader.Load(asset);
            ModelLoadCount++;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MUSIC TAGGER] Failed to load the Sentis model: {e}");
            s_modelFailed = true;
            return false;
        }
    }

    /// <summary>MAIN THREAD. The (immutable, thread-safe) preprocessor built from the model's mel
    /// filterbank asset, cached. Null if the asset is missing.</summary>
    public static MusicnnPreprocessor GetPreprocessor()
    {
        if (s_preprocessor != null || s_preprocessorFailed) return s_preprocessor;
        try
        {
            var fb = Resources.Load<TextAsset>(FilterbankResource);
            if (fb == null)
            {
                Debug.LogWarning($"[MUSIC TAGGER] Resources/{FilterbankResource} missing — semantic tagging disabled.");
                s_preprocessorFailed = true;
                return null;
            }
            s_preprocessor = new MusicnnPreprocessor(MusicnnPreprocessor.ParseFilterbank(fb.bytes));
            Resources.UnloadAsset(fb);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MUSIC TAGGER] Invalid mel filterbank: {e.Message}");
            s_preprocessorFailed = true;
        }
        return s_preprocessor;
    }

    /// <summary>
    /// One inference request: its own Worker, created on the MAIN thread, running the patches in
    /// CHUNKS of at most `patchesPerChunk` (one Schedule per chunk). Sentis' CPU backend keeps every
    /// intermediate tensor of a scheduled batch alive until the batch completes (~44 MB per musicnn
    /// patch, measured), so a whole-song batch held 2–5 GB of native memory while running; a chunk
    /// bounds that peak to the chunk's activations. Poll <see cref="IsDone"/> once per frame: it never
    /// blocks — it collects a finished chunk (DownloadToArray of a completed tensor) and schedules the
    /// next one — then call <see cref="TakeTaggram"/>.
    /// </summary>
    public sealed class Inference : IPipelineWait
    {
        private const int NumLabels = 50;

        private Worker        _worker;
        private Tensor<float> _input;
        private Tensor<float> _output;
        private float[]       _batch;      // [patches × 187 × 96], released when the last chunk is scheduled
        private float[]       _slice;      // one chunk of _batch (reused)
        private readonly float[] _taggram; // [patches × 50]
        private readonly int  _numPatches, _chunk;
        private int  _next, _curStart, _curCount;
        private bool _abandoned;

        public int    NumPatches => _numPatches;
        public int    PatchesPerChunk => _chunk;
        /// <summary>Main-thread time spent creating the worker and scheduling chunks: total and the
        /// largest single frame (ms).</summary>
        public double ScheduleMs { get; private set; }
        public double MaxScheduleMs { get; private set; }

        /// <summary>MAIN THREAD. Takes ownership of `batch` (not copied; dropped once scheduled).</summary>
        public Inference(Model model, float[] batch, int numPatches, int patchesPerChunk)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _numPatches = numPatches;
            _chunk = Math.Max(1, Math.Min(patchesPerChunk, numPatches));
            _batch = batch;
            _taggram = new float[numPatches * NumLabels];
            _worker = new Worker(model, BackendType.CPU);
            WorkerCreateCount++;
            ScheduleNext(sw);
        }

        private void ScheduleNext(System.Diagnostics.Stopwatch sw = null)
        {
            sw ??= System.Diagnostics.Stopwatch.StartNew();
            const int patchSize = MusicnnPreprocessor.PatchFrames * MusicnnPreprocessor.MelBins;
            _curStart = _next;
            _curCount = Math.Min(_chunk, _numPatches - _next);
            _next += _curCount;
            // Exact-size slice: Inference Engine 2.6.1's Tensor(shape, T[], dataStartIndex) copies every
            // element after dataStartIndex (not just the shape's), so an offset into the batch overflows.
            int len = _curCount * patchSize;
            if (_slice == null || _slice.Length != len) _slice = new float[len];
            Array.Copy(_batch, _curStart * patchSize, _slice, 0, len);
            _input = new Tensor<float>(new TensorShape(_curCount, MusicnnPreprocessor.PatchFrames, MusicnnPreprocessor.MelBins), _slice);
            if (_next >= _numPatches) { _batch = null; _slice = null; }
            _worker.Schedule(_input);
            // PeekOutput() is owned by the worker (disposed with it), never disposed here.
            _output = _worker.PeekOutput() as Tensor<float>;
            _output.ReadbackRequest(); // no-op on CPU; keeps the non-blocking contract on GPU backends
            double ms = sw.Elapsed.TotalMilliseconds;
            ScheduleMs += ms;
            MaxScheduleMs = Math.Max(MaxScheduleMs, ms);
        }

        /// <summary>The current chunk's jobs are finished (or nothing is scheduled). Never blocks.</summary>
        private bool ChunkIdle => _output == null || _output.IsReadbackRequestDone();

        private void Collect()
        {
            var data = _output.DownloadToArray(); // completed: a copy, no wait
            Array.Copy(data, 0, _taggram, _curStart * NumLabels, Math.Min(data.Length, _curCount * NumLabels));
            _input.Dispose();
            _input = null;
            _output = null;
        }

        /// <summary>MAIN THREAD, never blocks: advances finished chunks; true once every patch is scored.</summary>
        public bool IsDone
        {
            get
            {
                if (_worker == null) return true;
                if (_output != null && _output.IsReadbackRequestDone())
                {
                    Collect();
                    if (!_abandoned && _next < _numPatches) ScheduleNext();
                }
                return _output == null && (_abandoned || _next >= _numPatches);
            }
        }

        /// <summary>Blocking completion — ONLY for the editor's synchronous entry point.</summary>
        public void WaitBlocking()
        {
            while (_worker != null && !_abandoned)
            {
                if (_output == null) { if (_next >= _numPatches) return; ScheduleNext(); }
                _output.CompleteAllPendingOperations();
                Collect();
            }
        }

        /// <summary>MAIN THREAD, after IsDone (or WaitBlocking): the [patches × 50] scores; disposes the worker.</summary>
        public float[] TakeTaggram()
        {
            Dispose();
            return _taggram;
        }

        /// <summary>MAIN THREAD. Release without waiting: no further chunk is scheduled; disposed now if the
        /// running chunk is done, otherwise on the first frame it is (see PumpDeferred) — disposing
        /// in-flight Sentis data would complete its jobs synchronously, i.e. block the main thread.</summary>
        public void Abandon()
        {
            if (_worker == null) return;
            _abandoned = true;
            _batch = null;
            if (ChunkIdle) Dispose();
            else if (!s_deferred.Contains(this)) s_deferred.Add(this);
        }

        internal bool CanDisposeWithoutBlocking => ChunkIdle;

        internal void Dispose()
        {
            _input?.Dispose();
            _worker?.Dispose();
            _input = null; _worker = null; _output = null; _batch = null; _slice = null;
        }
    }


    /// <summary>MAIN THREAD, once per frame (AudioPreAnalyzer.Update): disposes abandoned inferences
    /// whose jobs have finished. `force` disposes all of them now (shutdown), blocking if needed.</summary>
    public static void PumpDeferred(bool force = false)
    {
        for (int i = s_deferred.Count - 1; i >= 0; i--)
        {
            if (!force && !s_deferred[i].CanDisposeWithoutBlocking) continue;
            s_deferred[i].Dispose();
            s_deferred.RemoveAt(i);
        }
    }

    public static int DeferredCount => s_deferred.Count;

    /// <summary>Pure (any thread): mean score per label over all patches, sorted descending, top-N above
    /// minScore. Same selection as the previous implementation.</summary>
    public static MusicTagScore[] SelectTags(float[] taggram, int numPatches, int topN, float minScore)
    {
        int numLabels = Labels.Length;
        if (taggram == null || numPatches <= 0 || taggram.Length < numPatches * numLabels) return Array.Empty<MusicTagScore>();
        var mean = new float[numLabels];
        for (int p = 0; p < numPatches; p++)
            for (int l = 0; l < numLabels; l++)
                mean[l] += taggram[p * numLabels + l];
        for (int l = 0; l < numLabels; l++) mean[l] /= numPatches;

        var order = new int[numLabels];
        for (int l = 0; l < numLabels; l++) order[l] = l;
        Array.Sort(order, (a, b) => mean[b].CompareTo(mean[a]));

        topN = Math.Max(1, Math.Min(topN, numLabels));
        var result = new List<MusicTagScore>(topN);
        for (int i = 0; i < topN; i++)
        {
            int idx = order[i];
            if (mean[idx] < minScore) continue;
            result.Add(new MusicTagScore(Labels[idx], mean[idx]));
        }
        return result.ToArray();
    }

    public static TagScore[] ToCore(MusicTagScore[] tags)
    {
        if (tags == null) return null;
        var o = new TagScore[tags.Length];
        for (int i = 0; i < tags.Length; i++) o[i] = new TagScore(tags[i].tag, tags[i].score);
        return o;
    }

    public static MusicTagScore[] FromCore(TagScore[] tags)
    {
        if (tags == null) return null;
        var o = new MusicTagScore[tags.Length];
        for (int i = 0; i < tags.Length; i++) o[i] = new MusicTagScore(tags[i].tag, tags[i].score);
        return o;
    }
}
