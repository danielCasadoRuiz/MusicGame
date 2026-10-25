using System;

namespace MusicGame.Audio.Analysis
{
    /// <summary>Versions of the analysis pipeline. Bump <see cref="Pipeline"/> whenever the CODE changes
    /// results for identical params (a detector fix, a new feature formula) so persisted profiles
    /// become stale even though their params hash still matches.</summary>
    public static class AnalysisVersions
    {
        public const int Pipeline = 3; // 2: hi-hat test on power densities + snare explaining-away; 3: tempo only from a periodicity peak (no BPM without evidence)
    }

    /// <summary>A frequency band in Hz (inclusive edges by overlap, see FrameLayout).</summary>
    [Serializable]
    public struct BandSpec
    {
        public string label;
        public float  minHz;
        public float  maxHz;
        public BandSpec(string label, float minHz, float maxHz) { this.label = label; this.minHz = minHz; this.maxHz = maxHz; }
    }

    /// <summary>The three spectral regions the onset detector watches. The family heuristics rely
    /// on these roles (kick lives in Low, snare body/crack in Mid, hi-hat in High), not on indices.</summary>
    public enum OnsetBandRole : byte { Low = 0, Mid = 1, High = 2 }

    /// <summary>Settings of one onset band group (all times in seconds, all frequencies in Hz).</summary>
    [Serializable]
    public struct OnsetGroupSpec
    {
        public OnsetBandRole role;
        public float  minHz;
        public float  maxHz;
        /// <summary>STFT window length used for this group. Groups sharing a length share one FFT.</summary>
        public double windowSeconds;
        /// <summary>Duplicate suppression inside the group: of two peaks closer than this, the weaker is dropped.</summary>
        public double minIntervalSeconds;
        /// <summary>Additive threshold (log-flux units per bin) above the local mean.</summary>
        public float  thresholdDelta;
        /// <summary>Multiplier of the local (recent-context) mean.</summary>
        public float  thresholdRatio;
        /// <summary>Noise floor: the group's TOTAL power (dB; a full-scale sine ≈ +1.8 dB) right after the peak must exceed this.</summary>
        public float  floorDb;
        /// <summary>Measured detector latency for this group (seconds), subtracted from the peak time
        /// so event times land on the audible attack. Calibrated with synthetic attacks.</summary>
        public double timeOffsetSeconds;
    }

    /// <summary>
    /// Mutable, plain settings bag (defaults = recommended values). Build it on the main thread
    /// from AudioAnalysisConfig, then freeze it with <see cref="AnalysisParams.Create"/>; workers
    /// only ever see the immutable AnalysisParams.
    /// </summary>
    public sealed class AnalysisSettings
    {
        // ── Frame features (energy, centroid, flux, flatness, bands, visual bands, chroma, voice) ──
        // Same time/frequency resolution as the legacy pipeline (1024 / 512 samples at 48 kHz), but
        // expressed in seconds so 44.1 kHz clips get the same time resolution.
        public double featureWindowSeconds = 1024.0 / 48000.0;
        public double featureHopSeconds    = 512.0 / 48000.0;
        public bool   computeFlatness = true;
        public bool   computeChroma   = true;
        public bool   computeVoice    = true;

        public BandSpec[] classificationBands =
        {
            new BandSpec("Sub Bass", 20f, 60f),
            new BandSpec("Bass", 60f, 250f),
            new BandSpec("Low Mid", 250f, 500f),
            new BandSpec("Mid", 500f, 2000f),
            new BandSpec("High Mid", 2000f, 4000f),
            new BandSpec("Treble", 4000f, 20000f),
        };

        public int   visualBandCount = 24;
        public float visualMinHz     = 20f;
        /// <summary>0 = Nyquist (legacy behaviour: the top band always reaches sampleRate/2).</summary>
        public float visualMaxHz     = 0f;
        /// <summary>Visual bands entirely below <see cref="lowBandCrossoverHz"/> are measured on a
        /// longer window (better frequency resolution) so the lowest log bands stop collapsing onto
        /// the same one or two FFT bins. 0 disables (all bands from the feature spectrum).</summary>
        public double lowBandLongWindowSeconds = 4096.0 / 48000.0;
        public float  lowBandCrossoverHz       = 250f;
        /// <summary>Low-frequency analyses (long-window visual bands, the Low onset group) run on a
        /// decimated copy of the signal whose rate is the lowest power-of-two division ≥ this (Hz).
        /// 0 = full rate (slower, for reference).</summary>
        public float  lowRateMinHz             = 2400f;

        // ── Onsets ─────────────────────────────────────────────────────────────────────────────
        public double onsetHopSeconds      = 256.0 / 48000.0;
        /// <summary>Flux compares frame f with frame f-lag (lag in seconds, rounded to hops ≥ 1).</summary>
        public double onsetDiffLagSeconds  = 512.0 / 48000.0;
        /// <summary>log(1 + gamma·amplitude) compression; amplitude normalised so a full-scale sine is 1.</summary>
        public float  logCompressionGamma  = 1000f;

        public OnsetGroupSpec[] onsetGroups =
        {
            new OnsetGroupSpec { role = OnsetBandRole.Low,  minHz = 30f,   maxHz = 150f,   windowSeconds = 2048.0 / 48000.0,
                                 minIntervalSeconds = 0.060, thresholdDelta = 0.20f, thresholdRatio = 1.0f, floorDb = -70f, timeOffsetSeconds = -0.0096 },
            new OnsetGroupSpec { role = OnsetBandRole.Mid,  minHz = 150f,  maxHz = 4000f,  windowSeconds = 1024.0 / 48000.0,
                                 minIntervalSeconds = 0.040, thresholdDelta = 0.06f, thresholdRatio = 1.0f, floorDb = -70f, timeOffsetSeconds = 0.0 },
            new OnsetGroupSpec { role = OnsetBandRole.High, minHz = 5000f, maxHz = 16000f, windowSeconds = 512.0 / 48000.0,
                                 minIntervalSeconds = 0.030, thresholdDelta = 0.06f, thresholdRatio = 1.0f, floorDb = -70f, timeOffsetSeconds = 0.0028 },
        };

        // Peak picking windows around a candidate frame (seconds).
        public double peakPreMaxSeconds  = 0.030;
        public double peakPostMaxSeconds = 0.030;
        /// <summary>Recent context for the adaptive threshold (mostly past, a little future).</summary>
        public double peakPreAvgSeconds  = 0.250;
        public double peakPostAvgSeconds = 0.050;

        /// <summary>Group peaks closer than this are one musical event (e.g. kick + hi-hat together).</summary>
        public double mergeWindowSeconds = 0.030;

        // Per-family minimum interval: a second label of the same family inside the interval is a
        // duplicate (the weaker event loses that label). Hi-hat is short so fast 16th/32nd patterns survive.
        public double kickMinIntervalSeconds    = 0.090;
        public double snareMinIntervalSeconds   = 0.090;
        public double hihatMinIntervalSeconds   = 0.045;
        public double genericMinIntervalSeconds = 0.045;

        /// <summary>Reference band (inside the Mid group's range) the hi-hat test compares the high-band
        /// gain against: snares/claps put a lot of noise here, hi-hats very little.</summary>
        public float hatReferenceMinHz = 2000f;
        public float hatReferenceMaxHz = 4500f;
        /// <summary>An event needs at least this power rise (dB) in one of its groups; spectral
        /// reshuffles without added energy (sound tails, legato glides) are not attacks.</summary>
        public float minRiseDb = 1.5f;
        /// <summary>The added power of an event must be within this many dB of the whole mix's power.</summary>
        public float minRelativeGainDb = -35f;

        /// <summary>A family label is assigned when its heuristic confidence reaches this value.
        /// Confidences are heuristic scores, NOT calibrated probabilities.</summary>
        public float familyConfidenceThreshold = 0.5f;

        // ── Tempo ──────────────────────────────────────────────────────────────────────────────
        public float minBpm = 60f;
        public float maxBpm = 200f;
        public float tempoPriorCenterBpm = 120f;
        /// <summary>Width (in octaves) of the log-normal tempo prior; large = flat prior.</summary>
        public float tempoPriorOctaves   = 1.0f;
        /// <summary>Beat tracker: how strongly inter-beat intervals are held to the global period.</summary>
        public float beatTightness       = 100f;

        // ── Structure ──────────────────────────────────────────────────────────────────────────
        public float segmentSeconds = 4f;

        public AnalysisSettings Clone()
        {
            var c = (AnalysisSettings)MemberwiseClone();
            c.classificationBands = (BandSpec[])classificationBands?.Clone();
            c.onsetGroups         = (OnsetGroupSpec[])onsetGroups?.Clone();
            return c;
        }
    }

    /// <summary>
    /// Immutable snapshot of every setting that affects analysis results, plus a stable hash of
    /// them (<see cref="Hash"/>, includes <see cref="AnalysisVersions.Pipeline"/>). Safe to share
    /// across worker threads. Sample-rate dependent sizes are resolved by <see cref="FrameLayout"/>.
    /// </summary>
    public sealed class AnalysisParams
    {
        private readonly AnalysisSettings _s;

        public ulong Hash { get; }
        public int   PipelineVersion => AnalysisVersions.Pipeline;

        private AnalysisParams(AnalysisSettings s)
        {
            _s = s;
            Hash = ComputeHash(s);
        }

        public static AnalysisParams Default { get; } = Create(new AnalysisSettings());

        public static AnalysisParams Create(AnalysisSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var s = settings.Clone();
            Validate(s);
            return new AnalysisParams(s);
        }

        /// <summary>A defensive copy of the settings (edit it and Create() a new params object).</summary>
        public AnalysisSettings ToSettings() => _s.Clone();

        // ── Read-only accessors ───────────────────────────────────────────────
        public double FeatureWindowSeconds     => _s.featureWindowSeconds;
        public double FeatureHopSeconds        => _s.featureHopSeconds;
        public bool   ComputeFlatness          => _s.computeFlatness;
        public bool   ComputeChroma            => _s.computeChroma;
        public bool   ComputeVoice             => _s.computeVoice;
        public int    ClassificationBandCount  => _s.classificationBands.Length;
        public BandSpec GetClassificationBand(int i) => _s.classificationBands[i];
        public int    VisualBandCount          => _s.visualBandCount;
        public float  VisualMinHz              => _s.visualMinHz;
        public float  VisualMaxHz              => _s.visualMaxHz;
        public double LowBandLongWindowSeconds => _s.lowBandLongWindowSeconds;
        public float  LowBandCrossoverHz       => _s.lowBandCrossoverHz;
        public float  LowRateMinHz             => _s.lowRateMinHz;
        public int LowRateFactor(int sampleRate) => _s.lowRateMinHz > 0f ? LowRateSignal.ChooseFactor(sampleRate, _s.lowRateMinHz) : 1;

        public double OnsetHopSeconds          => _s.onsetHopSeconds;
        public double OnsetDiffLagSeconds      => _s.onsetDiffLagSeconds;
        public float  LogCompressionGamma      => _s.logCompressionGamma;
        public int    OnsetGroupCount          => _s.onsetGroups.Length;
        public OnsetGroupSpec GetOnsetGroup(int i) => _s.onsetGroups[i];
        public double PeakPreMaxSeconds        => _s.peakPreMaxSeconds;
        public double PeakPostMaxSeconds       => _s.peakPostMaxSeconds;
        public double PeakPreAvgSeconds        => _s.peakPreAvgSeconds;
        public double PeakPostAvgSeconds       => _s.peakPostAvgSeconds;
        public double MergeWindowSeconds       => _s.mergeWindowSeconds;
        public double KickMinIntervalSeconds   => _s.kickMinIntervalSeconds;
        public double SnareMinIntervalSeconds  => _s.snareMinIntervalSeconds;
        public double HihatMinIntervalSeconds  => _s.hihatMinIntervalSeconds;
        public double GenericMinIntervalSeconds=> _s.genericMinIntervalSeconds;
        public float  FamilyConfidenceThreshold=> _s.familyConfidenceThreshold;
        public float  HatReferenceMinHz        => _s.hatReferenceMinHz;
        public float  HatReferenceMaxHz        => _s.hatReferenceMaxHz;
        public float  MinRiseDb                => _s.minRiseDb;
        public float  MinRelativeGainDb        => _s.minRelativeGainDb;

        public float  MinBpm                   => _s.minBpm;
        public float  MaxBpm                   => _s.maxBpm;
        public float  TempoPriorCenterBpm      => _s.tempoPriorCenterBpm;
        public float  TempoPriorOctaves        => _s.tempoPriorOctaves;
        public float  BeatTightness            => _s.beatTightness;
        public float  SegmentSeconds           => _s.segmentSeconds;

        public int FindOnsetGroup(OnsetBandRole role)
        {
            for (int i = 0; i < _s.onsetGroups.Length; i++) if (_s.onsetGroups[i].role == role) return i;
            return -1;
        }

        private static void Validate(AnalysisSettings s)
        {
            if (s.featureWindowSeconds <= 0 || s.featureHopSeconds <= 0) throw new ArgumentException("feature window/hop must be > 0");
            if (s.onsetHopSeconds <= 0) throw new ArgumentException("onset hop must be > 0");
            if (s.classificationBands == null || s.classificationBands.Length == 0) throw new ArgumentException("classification bands required");
            if (s.visualBandCount < 1) throw new ArgumentException("visualBandCount must be >= 1");
            if (s.onsetGroups == null || s.onsetGroups.Length != 3) throw new ArgumentException("exactly three onset groups (Low, Mid, High) required");
            bool lo = false, mid = false, hi = false;
            foreach (var g in s.onsetGroups)
            {
                if (g.windowSeconds <= 0 || g.maxHz <= g.minHz) throw new ArgumentException("invalid onset group " + g.role);
                lo |= g.role == OnsetBandRole.Low; mid |= g.role == OnsetBandRole.Mid; hi |= g.role == OnsetBandRole.High;
            }
            if (!(lo && mid && hi)) throw new ArgumentException("onset groups must cover the Low, Mid and High roles once each");
            if (s.minBpm <= 0 || s.maxBpm <= s.minBpm) throw new ArgumentException("invalid BPM range");
        }

        private static ulong ComputeHash(AnalysisSettings s)
        {
            var h = StableHash.Create();
            h.Add("MusicGame.Analysis");
            h.Add(AnalysisVersions.Pipeline);
            h.Add(s.featureWindowSeconds); h.Add(s.featureHopSeconds);
            h.Add(s.computeFlatness); h.Add(s.computeChroma); h.Add(s.computeVoice);
            h.Add(s.classificationBands.Length);
            foreach (var b in s.classificationBands) { h.Add(b.minHz); h.Add(b.maxHz); } // labels are cosmetic
            h.Add(s.visualBandCount); h.Add(s.visualMinHz); h.Add(s.visualMaxHz);
            h.Add(s.lowBandLongWindowSeconds); h.Add(s.lowBandCrossoverHz); h.Add(s.lowRateMinHz);
            h.Add(s.onsetHopSeconds); h.Add(s.onsetDiffLagSeconds); h.Add(s.logCompressionGamma);
            h.Add(s.onsetGroups.Length);
            foreach (var g in s.onsetGroups)
            {
                h.Add((int)g.role); h.Add(g.minHz); h.Add(g.maxHz); h.Add(g.windowSeconds); h.Add(g.minIntervalSeconds);
                h.Add(g.thresholdDelta); h.Add(g.thresholdRatio); h.Add(g.floorDb); h.Add(g.timeOffsetSeconds);
            }
            h.Add(s.peakPreMaxSeconds); h.Add(s.peakPostMaxSeconds); h.Add(s.peakPreAvgSeconds); h.Add(s.peakPostAvgSeconds);
            h.Add(s.mergeWindowSeconds);
            h.Add(s.kickMinIntervalSeconds); h.Add(s.snareMinIntervalSeconds); h.Add(s.hihatMinIntervalSeconds); h.Add(s.genericMinIntervalSeconds);
            h.Add(s.familyConfidenceThreshold); h.Add(s.hatReferenceMinHz); h.Add(s.hatReferenceMaxHz); h.Add(s.minRiseDb); h.Add(s.minRelativeGainDb);
            h.Add(s.minBpm); h.Add(s.maxBpm); h.Add(s.tempoPriorCenterBpm); h.Add(s.tempoPriorOctaves); h.Add(s.beatTightness);
            h.Add(s.segmentSeconds);
            return h.Value;
        }
    }
}
