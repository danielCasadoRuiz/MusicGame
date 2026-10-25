using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MusicGame.Audio.Analysis
{
    /// <summary>
    /// Entry point of the deterministic analysis core. Pure C#: no UnityEngine, no shared mutable
    /// state; safe to call from a worker thread (Task.Run) with data captured on the main thread.
    /// Same input + same AnalysisParams ⇒ same output (bitwise), whatever maxWorkers is.
    /// </summary>
    public static class MusicAnalyzer
    {
        /// <summary>Weights of the stages in the 0..1 progress value.</summary>
        private const float FeatureShare = 0.55f, OnsetShare = 0.40f;

        /// <summary>Interleaved → mono by channel average (legacy AudioPreAnalyzer.MixToMono).
        /// Returns the input itself when channels == 1.</summary>
        public static float[] MixToMono(float[] interleaved, int channels)
        {
            if (channels <= 1) return interleaved;
            int len = interleaved.Length / channels;
            var mono = new float[len];
            for (int i = 0; i < len; i++)
            {
                float s = 0f;
                int o = i * channels;
                for (int c = 0; c < channels; c++) s += interleaved[o + c];
                mono[i] = s / channels;
            }
            return mono;
        }

        /// <summary>Wraps the full-rate signal when the long window is configured without decimation.</summary>
        private static LowRateSignal FullRate(float[] mono, int sampleRate, FeatureLayout fl)
            => fl.longWindowLength > 0 ? LowRateSignal.Wrap(mono, sampleRate) : null;

        public static AnalysisResult Analyze(float[] mono, int sampleRate, AnalysisParams p, AnalysisRunOptions opt = null)
            => Analyze(mono, sampleRate, p, opt, null, null);

        /// <param name="rawPeaks">optional: receives uncalibrated per-group peaks (calibration/diagnostics).</param>
        /// <param name="functionsOut">optional: receives the per-frame onset evidence.</param>
        public static AnalysisResult Analyze(float[] mono, int sampleRate, AnalysisParams p, AnalysisRunOptions opt,
                                             List<OnsetDetector.RawPeak> rawPeaks, Action<OnsetFunctions> functionsOut)
        {
            if (mono == null) throw new ArgumentNullException(nameof(mono));
            if (sampleRate <= 0) throw new ArgumentException("sampleRate must be > 0");
            if (p == null) p = AnalysisParams.Default;
            opt = opt ?? new AnalysisRunOptions();
            var ct = opt.cancellation;
            // Chunk completions arrive from several workers; keep the reported value monotonic.
            Action<float> prog = null;
            if (opt.progress != null)
            {
                var gate = new object(); float last = 0f; var user = opt.progress;
                prog = v => { lock (gate) { if (v <= last) return; last = v; user(v); } };
            }
            var timings = new AnalysisTimings();
            var total = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();

            var r = new AnalysisResult
            {
                sampleRate = sampleRate,
                sampleCount = mono.Length,
                duration = (float)mono.Length / sampleRate,
                timings = timings,
            };

            var fl = new FeatureLayout(p, sampleRate);
            var ol = new OnsetLayout(p, sampleRate);
            // One decimated copy shared by the low-frequency analyses (long-window visual bands, Low onset group).
            LowRateSignal low = null;
            if (fl.longWindowLength > 0 && fl.longFactor > 1 || OnsetDetector.NeedsLowRate(ol))
                low = LowRateSignal.Create(mono, sampleRate, Math.Max(fl.longFactor, ol.lowFactor), opt);
            timings.decimateMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
            FeatureExtractor.Compute(mono, fl.longFactor > 1 ? low : FullRate(mono, sampleRate, fl), fl, r, opt, prog == null ? (Action<float>)null : v => prog(v * FeatureShare));
            timings.featuresMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

            float avg = 0f, max = 0f;
            for (int f = 0; f < r.frameCount; f++) { avg += r.energy[f]; if (r.energy[f] > max) max = r.energy[f]; }
            r.averageEnergy = avg / Math.Max(1, r.frameCount);
            r.maxEnergy = max;

            ct.ThrowIfCancellationRequested();
            var F = OnsetDetector.ComputeFunctions(mono, low, ol, opt, prog == null ? (Action<float>)null : v => prog(FeatureShare + v * OnsetShare));
            timings.onsetOdfMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

            r.onsets = OnsetDetector.Detect(F, ol, p, ct, rawPeaks);
            timings.onsetPickMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

            ct.ThrowIfCancellationRequested();
            TempoEstimator.Estimate(F, r.onsets, p, out r.estimatedBpm, out r.bpmConfidence, out r.beatPhase);
            r.beatTimes = BeatTracker.Track(r.onsets, r.estimatedBpm, r.duration, 0.005, p.BeatTightness);
            timings.tempoMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

            r.segments = StructureAnalyzer.Segment(r.energy, r.hopTime, r.maxEnergy, p.SegmentSeconds);
            timings.segmentsMs = sw.Elapsed.TotalMilliseconds;

            functionsOut?.Invoke(F);
            prog?.Invoke(1f);
            timings.totalMs = total.Elapsed.TotalMilliseconds;
            return r;
        }
    }
}
