using System;

namespace MusicGame.Audio.Analysis
{
    /// <summary>Instrument-like families an attack MAY belong to. Multi-label (a kick and a hi-hat
    /// on the same instant set both). Generic = an attack with no convincing family evidence.</summary>
    [Flags]
    public enum OnsetFamily : byte
    {
        None    = 0,
        Kick    = 1,
        Snare   = 2,
        HiHat   = 4,
        Generic = 8,
    }

    /// <summary>Where an event came from. Only Detected is produced by the analyzer; the others are
    /// reserved for gameplay-side filling (e.g. a BPM grid) so they are never confused with real hits.</summary>
    public enum OnsetOrigin : byte
    {
        Detected      = 0,
        SyntheticGrid = 1,
    }

    /// <summary>
    /// One detected attack. <see cref="time"/> is CLIP time in seconds (no warmup), calibrated to the
    /// audible attack. Confidences are heuristic 0..1 scores of "how much the evidence looks like
    /// this family" — NOT calibrated probabilities, and they do not sum to 1.
    /// </summary>
    [Serializable]
    public struct OnsetEvent
    {
        public float       time;
        /// <summary>0..1 heuristic attack salience relative to the recent context.</summary>
        public float       strength;
        public OnsetFamily families;
        public OnsetOrigin origin;
        public float       kickConfidence;
        public float       snareConfidence;
        public float       hihatConfidence;
        public float       genericConfidence;

        public bool Has(OnsetFamily f) => (families & f) != 0;
    }

    public enum SegmentLevel : byte { Low = 0, Mid = 1, High = 2, Drop = 3 } // same order as legacy EnergyLevel

    [Serializable]
    public struct AnalysisSegment
    {
        public float        startTime;
        public float        endTime;
        public float        averageEnergy;
        public float        maxEnergy;
        public SegmentLevel level;
    }

    [Serializable]
    public struct TagScore
    {
        public string tag;
        public float  score;
        public TagScore(string tag, float score) { this.tag = tag; this.score = score; }
    }

    /// <summary>Per-stage wall time in milliseconds (instrumentation, not persisted).</summary>
    public sealed class AnalysisTimings
    {
        public double decimateMs;
        public double featuresMs;
        public double onsetOdfMs;
        public double onsetPickMs;
        public double tempoMs;
        public double segmentsMs;
        public double totalMs;
        public override string ToString() =>
            $"decimate {decimateMs:F1} ms | features {featuresMs:F1} ms | onsetODF {onsetOdfMs:F1} ms | pick+classify {onsetPickMs:F1} ms | tempo {tempoMs:F1} ms | segments {segmentsMs:F1} ms | total {totalMs:F1} ms";
    }

    /// <summary>
    /// RAW analysis output (what gets persisted). Frame arrays are center-aligned: frame f describes
    /// the window centred on clip time f·hopTime, so the existing floor(t / hopTime) lookups return
    /// the frame centred at most one hop before t. Derived analyzers must copy, never mutate these.
    /// </summary>
    public sealed class AnalysisResult
    {
        public int   sampleRate;
        public int   sampleCount;     // mono samples analysed
        public float duration;        // seconds
        public float hopTime;         // seconds per frame (exact hopSamples / sampleRate)
        public int   frameCount;

        public float averageEnergy;
        public float maxEnergy;

        public float estimatedBpm;    // 0 if unknown
        public float bpmConfidence;   // heuristic 0..1
        public float beatPhase;       // seconds, first beat-grid position in [0, 60/bpm)
        /// <summary>Tracked beat times (s), following drift; empty if no tempo. Prefer these over a
        /// constant 60/bpm grid.</summary>
        public float[] beatTimes;

        public float[]   energy;      // RMS of the raw window
        public float[]   centroid;    // Hz
        public float[]   flux;        // legacy-compatible linear positive spectral flux
        public float[]   flatness;    // null if disabled
        public float[]   voice;       // raw heuristic (never smoothed here)
        public float[][] bands;       // [band][frame]
        public float[][] visualBands; // [band][frame]
        public float[]   chroma;      // frame*12 + pitchClass; null if disabled

        public BandSpec[] classificationBandSpecs;
        public BandSpec[] visualBandSpecs;

        public OnsetEvent[]      onsets;
        public AnalysisSegment[] segments;

        public AnalysisTimings timings; // not persisted

        /// <summary>All detected onset times (legacy SongProfile.onsetTimes equivalent).</summary>
        public float[] OnsetTimes()
        {
            var t = new float[onsets?.Length ?? 0];
            for (int i = 0; i < t.Length; i++) t[i] = onsets[i].time;
            return t;
        }
    }
}
