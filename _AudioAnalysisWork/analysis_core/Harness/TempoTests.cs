using System;
using System.Linq;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public static partial class Program
    {
        static partial void Extra3(string cmd, string[] rest, ref int handled)
        {
            if (cmd == "tempo") { TempoTests(); handled = 0; return; }
            Extra4(cmd, rest, ref handled);
        }
        static partial void Extra4(string cmd, string[] rest, ref int handled);

        static void TempoTests()
        {
            Console.WriteLine("pattern | true BPM | offset s | est BPM | conf | ratio | phase s | phase err ms (mod beat) | legacy BPM");
            int ok = 0, okOct = 0, phaseOk = 0, total = 0, beatOk = 0, beatTot = 0;
            foreach (string pat in new[] { "4floor", "rock", "halftime", "hats16" })
            foreach (double bpm in new[] { 84.0, 97.0, 120.0, 128.0, 140.0, 174.0 })
            {
                double beat = 60.0 / bpm, off = 0.237;
                var b = new SignalBuilder(48000, 30, (uint)(bpm * 10));
                for (int k = 0; off + k * beat < 29.5; k++)
                {
                    double t = off + k * beat;
                    switch (pat)
                    {
                        case "4floor": b.Kick(t); b.Hat(t + beat / 2, -16); break;
                        case "rock": if (k % 2 == 0) b.Kick(t); else b.Snare(t); b.Hat(t, -18); b.Hat(t + beat / 2, -20); break;
                        case "halftime": if (k % 4 == 0) b.Kick(t); if (k % 4 == 2) b.Snare(t); b.Hat(t, -18); break;
                        case "hats16": for (int q = 0; q < 4; q++) b.Hat(t + q * beat / 4, q == 0 ? -12 : -20); if (k % 4 == 0) b.Kick(t); break;
                    }
                }
                b.NoiseFloor(-65);
                var r = MusicAnalyzer.Analyze(b.x, 48000, AnalysisParams.Default, new AnalysisRunOptions());
                var leg = LegacyAnalyzer.Analyze(b.x, 48000);
                double ratio = r.estimatedBpm / bpm;
                double estBeat = 60.0 / r.estimatedBpm;
                // phase error measured modulo the ESTIMATED beat (grid must hit the true attacks)
                double d = (r.beatPhase - off) / estBeat; d = (d - Math.Round(d)) * estBeat * 1000;
                total++;
                if (Math.Abs(ratio - 1) < 0.02) ok++;
                if (new[] { 0.5, 1, 2 }.Any(m => Math.Abs(ratio - m) < 0.02 * m)) okOct++;
                if (Math.Abs(d) < 15) phaseOk++;
                // tracked beats vs true beats (beat level of the ESTIMATED tempo octave)
                var trueBeats = new System.Collections.Generic.List<float>();
                for (double tb = off; tb < 29.5; tb += estBeat) trueBeats.Add((float)tb);
                int matched = trueBeats.Count(tb => r.beatTimes.Any(x => Math.Abs(x - tb) <= 0.015));
                double maxErr = trueBeats.Select(tb => r.beatTimes.Length == 0 ? 1 : r.beatTimes.Min(x => Math.Abs(x - tb))).Where(e => e <= 0.05).DefaultIfEmpty(0).Max() * 1000;
                beatOk += matched; beatTot += trueBeats.Count;
                Console.WriteLine($"{pat,-9} | {bpm,5:F0} | {off:F3} | {r.estimatedBpm,6:F2} | {r.bpmConfidence:F2} | {ratio,5:F2} | {r.beatPhase:F3} | {d,7:F1} | {leg.bpm,4:F0} | tracked beats {matched}/{trueBeats.Count} within 15 ms (max {maxErr:F1} ms), extra {r.beatTimes.Length - matched}");
            }
            Console.WriteLine($"exact tempo (±2%) {ok}/{total}; up to octave (×½, ×2) {okOct}/{total}; phase within ±15 ms of a true beat {phaseOk}/{total}; tracked beats on true beats {beatOk}/{beatTot}");
        }
    }
}
