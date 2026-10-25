using System;
using System.Collections.Generic;
using System.Linq;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public static partial class Program
    {
        public static int Main(string[] args)
        {
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            string cmd = args.Length > 0 ? args[0] : "synth";
            var rest = args.Skip(1).ToArray();
            switch (cmd)
            {
                case "fftcheck": FftCheck(); return 0;
                case "synth": return Suite.Synth();
                case "stimuli": return Suite.Stimuli();
                case "suite": return Suite.Run();
                case "artdbg": ArtDebug.Run(rest[0], int.Parse(rest[1])); return 0;
                case "margins": Margins.Run(); return 0;
                case "calib": Calibration.Run(rest); return 0;
                default: return Extra(cmd, rest);
            }
        }

        static partial void ExtraImpl(string cmd, string[] rest, ref int handled);
        static int Extra(string cmd, string[] rest) { int h = -1; ExtraImpl(cmd, rest, ref h); if (h < 0) Console.WriteLine("unknown command " + cmd); return h < 0 ? 1 : h; }

        static void FftCheck()
        {
            var rng = new Rng(7);
            foreach (int n in new[] { 4, 8, 64, 512, 1024, 2048, 4096 })
            {
                var x = new float[n];
                for (int i = 0; i < n; i++) x[i] = rng.Uniform();
                var plan = RealFftPlan.Get(n);
                var re = new float[n / 2 + 1]; var im = new float[n / 2 + 1];
                plan.Forward(x, re, im, new FftScratch(n));
                double maxErr = 0, maxRef = 0;
                for (int k = 0; k <= n / 2; k++)
                {
                    double sr = 0, si = 0;
                    for (int t = 0; t < n; t++) { double a = -2 * Math.PI * k * t / n; sr += x[t] * Math.Cos(a); si += x[t] * Math.Sin(a); }
                    maxErr = Math.Max(maxErr, Math.Max(Math.Abs(sr - re[k]), Math.Abs(si - im[k])));
                    maxRef = Math.Max(maxRef, Math.Sqrt(sr * sr + si * si));
                }
                // vs legacy magnitude (rectangular window) for the n/2 bins
                double maxLeg = 0;
                if (n >= 8)
                {
                    var leg = LegacyFFT.Compute(x, LegacyWindow.Rectangular);
                    for (int k = 0; k < n / 2; k++) maxLeg = Math.Max(maxLeg, Math.Abs(leg[k] - Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / n));
                }
                Console.WriteLine($"FFT n={n,5}: max |new - naiveDFT| = {maxErr:E2} (rel {maxErr / maxRef:E2}), max |new mag/n - legacy mag/n| = {maxLeg:E2}");
            }
        }
    }
}
