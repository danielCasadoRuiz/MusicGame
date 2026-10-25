using System;
using System.Diagnostics;
using System.Linq;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public static partial class Program
    {
        static partial void Extra6(string cmd, string[] rest, ref int handled)
        {
            if (cmd == "profile") { ProfileStages(rest[0]); handled = 0; return; }
            Extra7(cmd, rest, ref handled);
        }
        static partial void Extra7(string cmd, string[] rest, ref int handled);

        static void ProfileStages(string name)
        {
            var wav = ResolveWavs(new[] { name })[0];
            var mono = LoadMono(wav, out int sr, out _, out _);
            var seq = new AnalysisRunOptions { maxWorkers = 1 };
            void T(string label, AnalysisSettings s, bool onsetOnly = false)
            {
                var p = AnalysisParams.Create(s);
                // warm-up + 2 timed runs, take min
                double best = double.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var ol = new OnsetLayout(p, sr); var fl = new FeatureLayout(p, sr);
                    int fac = Math.Max(ol.lowFactor, fl.longFactor);
                    var low = fac > 1 ? LowRateSignal.Create(mono, sr, fac, seq) : LowRateSignal.Wrap(mono, sr);
                    if (onsetOnly) OnsetDetector.ComputeFunctions(mono, low, ol, seq, null);
                    else { var r = new AnalysisResult(); FeatureExtractor.Compute(mono, fl.longWindowLength > 0 ? low : null, fl, r, seq, null); }
                    if (i > 0) best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                }
                Console.WriteLine($"  {label,-50} {best,7:F0} ms (sequential)");
            }
            Console.WriteLine(System.IO.Path.GetFileName(wav));
            T("features (default: 1024 + long 4096 for low visual)", new AnalysisSettings());
            T("features, long window at FULL rate (lowRateMinHz=0)", new AnalysisSettings { lowRateMinHz = 0 });
            T("onset ODF, low group at FULL rate (lowRateMinHz=0)", new AnalysisSettings { lowRateMinHz = 0 }, true);
            T("features without long window", new AnalysisSettings { lowBandLongWindowSeconds = 0 });
            T("features without long, flatness/chroma/voice off", new AnalysisSettings { lowBandLongWindowSeconds = 0, computeFlatness = false, computeChroma = false, computeVoice = false });
            T("onset ODF (default 2048/1024/512)", new AnalysisSettings(), true);
            var s1 = new AnalysisSettings(); for (int g = 0; g < 3; g++) s1.onsetGroups[g].windowSeconds = 1024.0 / 48000; 
            T("onset ODF single resolution 1024", s1, true);
            // legacy loop for reference
            double lb = double.MaxValue;
            for (int i = 0; i < 2; i++) { var sw = Stopwatch.StartNew(); LegacyAnalyzer.Analyze(mono, sr); lb = Math.Min(lb, sw.Elapsed.TotalMilliseconds); }
            Console.WriteLine($"  {"legacy FFT loop + DetectOnsets (port)",-50} {lb,7:F0} ms");
        }
    }
}
