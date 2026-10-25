using System;
using System.Threading;
using MusicGame.Audio.Analysis;
using UnityEngine;

/// <summary>
/// Immutable copy of the AudioAnalysisConfig fields the derived analyzers (IOfflineAnalyzer) read.
/// Taken on the main thread so the derived pass can run on a worker without touching the
/// ScriptableObject (which an editor tweak could change mid-analysis). Not part of the profile
/// identity: derived data is never persisted, it is rebuilt from the raw data on every load.
/// </summary>
public sealed class DerivedAnalysisSettings
{
    public readonly bool  advancedEnabled;
    public readonly bool  advancedLoudness;
    public readonly bool  advancedDynamics;
    public readonly float silenceThreshold;
    public readonly float breakThreshold;
    public readonly float impactThreshold;
    public readonly float buildupMinLength;
    public readonly float crescendoWindow;
    public readonly bool  advancedTimbre;
    public readonly bool  advancedStructure;
    public readonly bool  advancedHarmony;
    public readonly bool  advancedVoice;
    public readonly float voiceThreshold;
    public readonly float voiceMinDuration;
    public readonly bool  advancedSimilarity;
    public readonly float similaritySegmentSize;
    public readonly float similarityThreshold;

    public DerivedAnalysisSettings(AudioAnalysisConfig c)
    {
        if (c == null) throw new ArgumentNullException(nameof(c));
        advancedEnabled       = c.advancedEnabled;
        advancedLoudness      = c.advancedLoudness;
        advancedDynamics      = c.advancedDynamics;
        silenceThreshold      = c.silenceThreshold;
        breakThreshold        = c.breakThreshold;
        impactThreshold       = c.impactThreshold;
        buildupMinLength      = c.buildupMinLength;
        crescendoWindow       = c.crescendoWindow;
        advancedTimbre        = c.advancedTimbre;
        advancedStructure     = c.advancedStructure;
        advancedHarmony       = c.advancedHarmony;
        advancedVoice         = c.advancedVoice;
        voiceThreshold        = c.voiceThreshold;
        voiceMinDuration      = c.voiceMinDuration;
        advancedSimilarity    = c.advancedSimilarity;
        similaritySegmentSize = c.similaritySegmentSize;
        similarityThreshold   = c.similarityThreshold;
    }
}

/// <summary>
/// GLOBAL concurrency budget of the song analysis (runtime imports and the editor bake alike):
///   - at most ONE analysis does heavy work at a time (<see cref="Gate"/>), so two requests never
///     double the peak memory or fight for cores;
///   - inside it, at most <see cref="WorkerBudget"/> worker threads. While the Sentis inference runs
///     (on Unity's job workers) the rhythm branch overlaps it with one worker less
///     (<see cref="OverlapWorkers"/>), so the overlap never exceeds the budget by more than the
///     inference itself.
/// Mobile: 2 workers (thermal/memory headroom, the UI thread must stay smooth); desktop: half the
/// logical cores, capped at 4 — the measured point of diminishing returns of the core.
/// </summary>
public static class AnalysisConcurrency
{
    public static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Main thread only (reads Application/SystemInfo).</summary>
    public static int ComputeWorkerBudget()
    {
        int cores = Mathf.Max(1, SystemInfo.processorCount);
        int cap = Application.isMobilePlatform ? 2 : 4;
        return Mathf.Clamp(cores / 2, 1, cap);
    }

    public static int OverlapWorkers(int budget) => Mathf.Max(1, budget - 1);

    /// <summary>Tools/benchmarks only: forces the Sentis chunk size (0 = platform default).</summary>
    public static int TagPatchesPerChunkOverride;

    /// <summary>Main thread only. musicnn patches per Sentis Schedule: the CPU backend keeps a scheduled
    /// batch's intermediate tensors alive until it completes (~44 MB of native memory per patch,
    /// measured in the editor: 2 → +89 MB, 4 → +177 MB, 8 → +355 MB, whole song → 2.3–5.3 GB), so the chunk size
    /// bounds the inference's native-memory peak; wall time barely changes (it overlaps the rhythm branch) on
    /// desktop 4 vs 8 (+0.26 s inference wall, max frame 25 vs 56 ms). Tags are identical for any chunk size.</summary>
    public static int TagPatchesPerChunk()
        => TagPatchesPerChunkOverride > 0 ? TagPatchesPerChunkOverride : Application.isMobilePlatform ? 2 : 4;
}

/// <summary>
/// Everything one analysis needs from Unity-side configuration, captured ONCE on the main thread
/// and immutable afterwards (workers only ever see this): the core AnalysisParams (+ its stable
/// hash), the derived-analyzer settings, the semantic-tagging settings and preprocessor, and the
/// concurrency budget. The Sentis model itself is NOT here — inference stays on the main thread
/// (SentisMusicTagger).
/// </summary>
public sealed class SongAnalysisSnapshot
{
    public readonly AnalysisParams          Params;
    public readonly DerivedAnalysisSettings Derived;
    public readonly bool                    TaggingEnabled;   // config on AND model + filterbank available
    public readonly int                     TagTopN;
    public readonly float                   TagMinScore;
    public readonly int                     TagModelVersion;
    public readonly MusicnnPreprocessor     TagPreprocessor;  // null when tagging is off/unavailable
    public readonly int                     WorkerBudget;
    public readonly int                     TagPatchesPerChunk;

    private SongAnalysisSnapshot(AnalysisParams p, DerivedAnalysisSettings d, bool tagging, int topN, float minScore,
                                 MusicnnPreprocessor pre, int budget)
    {
        Params = p; Derived = d; TaggingEnabled = tagging; TagTopN = topN; TagMinScore = minScore;
        TagModelVersion = SentisMusicTagger.ModelVersion; TagPreprocessor = pre; WorkerBudget = budget;
        TagPatchesPerChunk = AnalysisConcurrency.TagPatchesPerChunk();
    }

    /// <summary>MAIN THREAD. `workerBudget` ≤ 0 = AnalysisConcurrency.ComputeWorkerBudget().</summary>
    public static SongAnalysisSnapshot Create(AudioAnalysisConfig config, int workerBudget = 0)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        var p = AnalysisParams.Create(ToSettings(config));
        bool wantsTags = config.advancedEnabled && config.advancedSemanticTagging;
        MusicnnPreprocessor pre = wantsTags ? SentisMusicTagger.GetPreprocessor() : null;
        bool tagging = pre != null && SentisMusicTagger.TryGetModel(out _);
        int topN = Mathf.Clamp(config.semanticTagTopN, 1, SentisMusicTagger.TagLabels.Count);
        return new SongAnalysisSnapshot(p, new DerivedAnalysisSettings(config), tagging, topN, config.semanticTagMinScore,
                                        tagging ? pre : null, workerBudget > 0 ? workerBudget : AnalysisConcurrency.ComputeWorkerBudget());
    }

    /// <summary>The tag reuse key of this audio under these tagging settings (0 when tagging is off).</summary>
    public ulong TagReuseKey(ulong audioIdentity)
        => TaggingEnabled ? TagPreprocessor.TagReuseKey(audioIdentity, TagModelVersion, TagTopN, TagMinScore) : 0UL;

    /// <summary>AudioAnalysisConfig → core settings. Everything that changes results goes into the
    /// params hash (bands, visual band count, feature window, the flatness/chroma/voice switches);
    /// the core's own onset/tempo defaults are versioned by AnalysisVersions.Pipeline.</summary>
    public static AnalysisSettings ToSettings(AudioAnalysisConfig config)
    {
        var s = new AnalysisSettings();
        if (config.bands != null && config.bands.Length > 0)
        {
            s.classificationBands = new BandSpec[config.bands.Length];
            for (int i = 0; i < config.bands.Length; i++)
                s.classificationBands[i] = new BandSpec(config.bands[i].label, config.bands[i].minHz, config.bands[i].maxHz);
        }
        s.visualBandCount = Mathf.Max(1, config.visualBandCount);
        // Legacy spectrumSize was a sample count at the catalog's 48 kHz; the core works in seconds
        // so a 44.1 kHz import gets the same time resolution (window, hop = window / 2).
        int window = Mathf.NextPowerOfTwo(Mathf.Max(64, config.spectrumSize));
        s.featureWindowSeconds = window / 48000.0;
        s.featureHopSeconds    = window / 2 / 48000.0;
        s.computeFlatness = config.advancedEnabled && (config.advancedTimbre || config.advancedVoice || config.advancedStructure);
        s.computeChroma   = config.advancedEnabled && config.advancedHarmony;
        s.computeVoice    = config.advancedEnabled && config.advancedVoice;
        return s;
    }
}
