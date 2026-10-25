using System;
using System.Collections.Generic;
using System.Linq;
using MusicGame.Audio.Analysis;

namespace Harness
{
    public sealed class CaseResult
    {
        public string name;
        public int truths;            // mandatory truth events
        public int hits;              // mandatory truths matched
        public int misses;            // OMISSIONS: mandatory truths without a detection within the match window
        public int fps;               // EXTRA TEMPORAL EVENTS: detections matching no truth (and no stimulus artefact)
        public int artefactEvents;    // detections on a measured stimulus artefact (case invalid, reported apart)
        public int stimulusArtefacts; // unintended transients found in the stimulus itself
        public int optionalTruths, optionalHit;
        public int famChecked;        // matched truths whose families were checked
        public int famMissing;        // a required family absent
        public int famExtra;          // a family present that is neither required nor justified-optional
        public int famOk;             // famChecked − (events with missing or extra families)
        public double meanErrMs, meanAbsMs, maxAbsMs, maxErrLimitMs;
        public string famDetail = "";
        public bool informational;
        public bool pass;
        public string failReasons = "";
        public string note = "";
    }

    /// <summary>
    /// Synthetic onset suite. Strict verdict for MANDATORY cases: no omissions, no extra events, no
    /// missing family, no extra family (Generic counts as extra whenever a family is required), max
    /// |timing error| within the case limit, and a stimulus free of unintended transients.
    /// Relaxations are per truth and always carry a signal-based justification (see Case.justification).
    /// </summary>
    public static class SynthTests
    {
        public delegate void CaseBuilder(SignalBuilder b);

        public sealed class Case
        {
            public string name; public double seconds; public CaseBuilder build;
            public double tolMs = 50;        // match window (detection ↔ truth)
            public double maxErrMs = 6;      // timing requirement on matched mandatory truths
            public bool informational;       // reported, never a failure — needs a justification
            public string justification = "";
        }

        static void SetAll(SignalBuilder b, OnsetFamily required, OnsetFamily allowed = OnsetFamily.None, bool optionalEvent = false)
        {
            for (int i = 0; i < b.truth.Count; i++) { var t = b.truth[i]; t.expected = required; t.optional = allowed; t.optionalEvent = optionalEvent; b.truth[i] = t; }
        }

        public static List<Case> Cases()
        {
            var c = new List<Case>();
            // Sub-hop jitter 1.3 ms × k so attacks land at every position inside a hop.
            c.Add(new Case { name = "kicks", seconds = 7, build = b => { for (int k = 0; k < 12; k++) b.Kick(0.5 + k * 0.5 + k * 0.0013); } });
            c.Add(new Case { name = "snares", seconds = 7, build = b => { for (int k = 0; k < 12; k++) b.Snare(0.5 + k * 0.5 + k * 0.0013); } });
            c.Add(new Case { name = "hihats", seconds = 7, build = b => { for (int k = 0; k < 12; k++) b.Hat(0.5 + k * 0.5 + k * 0.0013); } });
            c.Add(new Case { name = "clicks (must be Generic)", seconds = 7, build = b => { for (int k = 0; k < 12; k++) b.Click(0.5 + k * 0.5 + k * 0.0013); } });
            c.Add(new Case { name = "kick+hat simultaneous", seconds = 7, build = b => { for (int k = 0; k < 12; k++) { double t = 0.5 + k * 0.5 + k * 0.0013; b.Kick(t); b.Hat(t); } } });
            c.Add(new Case { name = "kick+snare simultaneous", seconds = 7, build = b => { for (int k = 0; k < 12; k++) { double t = 0.5 + k * 0.5 + k * 0.0013; b.Kick(t); b.Snare(t); } } });
            c.Add(new Case { name = "plucks (tonal, must be Generic)", seconds = 9, build = b => { double[] f = { 262, 330, 392, 523, 196, 440, 294, 349 }; for (int k = 0; k < 8; k++) b.Pluck(0.5 + k * 1.0 + k * 0.0013, f[k]); } });
            c.Add(new Case { name = "sustained bass (1 Generic start, nothing else)", seconds = 6, maxErrMs = 15,
                justification = "timing limit 15 ms: the note fades in over 20 ms, its attack instant is not sharper than that",
                build = b => { b.Bass(0.5, 4.5); SetAll(b, OnsetFamily.None); } });
            c.Add(new Case { name = "bass notes detached (Generic, not Kick)", seconds = 6, maxErrMs = 15,
                justification = "timing limit 15 ms: 20 ms fade-in per note",
                build = b => { double[] f = { 55, 65.4, 73.4, 49, 55, 82.4, 61.7, 55 }; for (int k = 0; k < 8; k++) b.Bass(0.5 + k * 0.6, 0.45, f[k]); SetAll(b, OnsetFamily.None); } });
            c.Add(new Case { name = "bass notes legato", seconds = 6, informational = true,
                justification = "legato pitch changes with 20 ms fade-ins overlapping the previous note's release: measured energy change at the 7 note boundaries −5.3…+2.4 dB, no sharp attack, so whether each is an onset is ambiguous; detections optional, families still checked (must not be Kick/Snare/HiHat)",
                build = b => { double[] f = { 55, 65.4, 73.4, 49, 55, 82.4, 61.7, 55 }; for (int k = 0; k < 8; k++) b.Bass(0.5 + k * 0.6, 0.6, f[k]); SetAll(b, OnsetFamily.None, OnsetFamily.None, true); } });
            c.Add(new Case { name = "pad crescendo (no event at all)", seconds = 6, build = b => b.PadCrescendo(0.5, 5) });
            c.Add(new Case { name = "quiet(-36dB) then loud(-6dB) + floor -70", seconds = 10, build = b =>
            {
                for (int k = 0; k < 8; k++) { b.Kick(0.5 + k * 0.5, -36); b.Hat(0.75 + k * 0.5, -44); }
                for (int k = 0; k < 8; k++) { b.Kick(5.0 + k * 0.5, -6); b.Hat(5.25 + k * 0.5, -14); }
                b.NoiseFloor(-70);
            } });
            c.Add(new Case { name = "silence gaps", seconds = 12, build = b => { for (int k = 0; k < 4; k++) b.Kick(0.5 + k * 0.5); for (int k = 0; k < 4; k++) b.Snare(6.5 + k * 0.5); } });
            c.Add(new Case { name = "16th hats @180bpm (83ms)", seconds = 5, build = b => { double p = 60.0 / 180 / 4; for (int k = 0; k < 48; k++) b.Hat(0.3 + k * p); } });
            c.Add(new Case { name = "32nd hats @120bpm (62.5ms), accents", seconds = 5, build = b => { double p = 60.0 / 120 / 8; for (int k = 0; k < 64; k++) b.Hat(0.3 + k * p, -14 + ((k % 2) == 1 ? -6 : 0)); } });
            c.Add(new Case { name = "drum loop 120bpm + bass + floor", seconds = 17,
                justification = "hi-hats under the snare hits: HiHat family optional (allowed, not required) on those events — measured masking: the hat is 5.3 dB under the snare in 5–16 kHz and adds only +0.9 dB (44.1 kHz) / +1.3 dB (48 kHz) to that band (see stimulus report)",
                build = b =>
                {
                    double beat = 0.5;
                    for (int bar = 0; bar < 8; bar++)
                    {
                        double t0 = 0.5 + bar * 4 * beat;
                        b.Kick(t0); b.Kick(t0 + 2 * beat);
                        b.Snare(t0 + beat); b.Snare(t0 + 3 * beat);
                        for (int e = 0; e < 8; e++) b.Hat(t0 + e * beat / 2, -18, 0.03, e != 2 && e != 6, e == 2 || e == 6);
                        b.Bass(t0, 4 * beat - 0.05, bar % 2 == 0 ? 55 : 73.4, -10, false);
                    }
                    b.NoiseFloor(-60);
                } });
            c.Add(new Case { name = "edge: kick at t=0", seconds = 1, build = b => b.Kick(0.0) });
            c.Add(new Case { name = "edge: kick 5ms before end", seconds = 1,
                justification = "family relaxed to Kick-or-Generic: only 5 ms of the kick exist, its ring/decay (the kick evidence) is not in the signal",
                build = b => { b.Kick(0.995); SetAll(b, OnsetFamily.None, OnsetFamily.Kick); } });
            c.Add(new Case { name = "edge: 500 samples, click@100", seconds = 500.0 / 48000.0, build = b => b.Click(100.0 / 48000.0) });
            c.Add(new Case { name = "edge: 10 samples", seconds = 10.0 / 48000.0, build = b => { } });
            c.Add(new Case { name = "edge: empty", seconds = 0, build = b => { } });
            return c;
        }

        public static CaseResult Evaluate(Case c, List<Truth> truth, OnsetEvent[] ev, List<double> artefacts)
        {
            var r = new CaseResult { name = c.name, informational = c.informational, maxErrLimitMs = c.maxErrMs, stimulusArtefacts = artefacts.Count };
            var used = new bool[ev.Length];
            var errs = new List<double>();
            var famCount = new SortedDictionary<string, int>();
            foreach (var t in truth)
            {
                int best = -1; double bd = double.MaxValue;
                for (int i = 0; i < ev.Length; i++)
                {
                    if (used[i]) continue;
                    double d = Math.Abs(ev[i].time - t.time);
                    if (d < bd) { bd = d; best = i; }
                }
                bool matched = best >= 0 && bd * 1000 <= c.tolMs;
                if (t.optionalEvent) { r.optionalTruths++; if (!matched) continue; r.optionalHit++; }
                else { r.truths++; if (!matched) { r.misses++; continue; } r.hits++; errs.Add((ev[best].time - t.time) * 1000); }
                used[best] = true;

                // Strict families: required ⊆ got, got ⊆ required ∪ allowed (Generic only when nothing is required).
                var got = ev[best].families;
                var permitted = t.expected | t.optional | (t.expected == OnsetFamily.None ? OnsetFamily.Generic : OnsetFamily.None);
                bool missing = (got & t.expected) != t.expected;
                bool extra = (got & ~permitted) != 0;
                r.famChecked++;
                if (missing) r.famMissing++;
                if (extra) r.famExtra++;
                if (!missing && !extra) r.famOk++;
                string key = (t.label ?? "?") + "→" + got;
                famCount[key] = famCount.TryGetValue(key, out var v) ? v + 1 : 1;
            }
            for (int i = 0; i < ev.Length; i++)
            {
                if (used[i]) continue;
                if (artefacts.Any(a => Math.Abs(ev[i].time - a) <= 0.015)) r.artefactEvents++;
                else r.fps++;
            }
            if (errs.Count > 0) { r.meanErrMs = errs.Average(); r.meanAbsMs = errs.Average(Math.Abs); r.maxAbsMs = errs.Max(Math.Abs); }
            r.famDetail = string.Join(", ", famCount.Select(kv => kv.Key + ":" + kv.Value));

            var why = new List<string>();
            if (r.stimulusArtefacts > 0) why.Add("stimulus has unintended transients");
            if (r.misses > 0) why.Add("omissions");
            if (r.fps > 0) why.Add("extra events");
            if (r.famMissing > 0) why.Add("missing family");
            if (r.famExtra > 0) why.Add("extra family");
            if (r.maxAbsMs > c.maxErrMs) why.Add($"timing > {c.maxErrMs} ms");
            r.failReasons = string.Join("; ", why);
            r.pass = why.Count == 0;
            if (c.informational) { r.note = "INFORMATIONAL: " + c.justification; }
            else if (c.justification.Length > 0) r.note = "justified relaxation: " + c.justification;
            return r;
        }

        public static List<CaseResult> RunAll(AnalysisParams p, int sr, AnalysisRunOptions opt, bool print)
        {
            var results = new List<CaseResult>();
            if (print) Console.WriteLine($"\n=== Synthetic onset tests @ {sr} Hz ===");
            foreach (var c in Cases())
            {
                var b = new SignalBuilder(sr, c.seconds);
                c.build(b);
                var truth = b.MergedTruth();
                var artefacts = StimulusCheck.AllArtefacts(b);
                var res = MusicAnalyzer.Analyze(b.x, sr, p, opt);
                var r = Evaluate(c, truth, res.onsets, artefacts);
                results.Add(r);
                if (print) Print(r);
            }
            return results;
        }

        public static void Print(CaseResult r)
        {
            string verdict = r.informational ? "INFO" : (r.pass ? "PASS" : "FAIL");
            Console.WriteLine($"{verdict} {r.name,-48} | omissions {r.misses}/{r.truths} | extra events {r.fps} | stimulus artefacts {r.stimulusArtefacts} (events on them {r.artefactEvents}) | families: missing {r.famMissing} extra {r.famExtra} ok {r.famOk}/{r.famChecked} | timing mean {r.meanErrMs:F2} |mean| {r.meanAbsMs:F2} max {r.maxAbsMs:F2} ms (limit {r.maxErrLimitMs}) | optional events {r.optionalHit}/{r.optionalTruths}");
            if (r.famDetail.Length > 0) Console.WriteLine($"     labels: {r.famDetail}");
            if (!r.pass && !r.informational) Console.WriteLine($"     FAILED: {r.failReasons}");
            if (r.note.Length > 0) Console.WriteLine($"     {r.note}");
        }
    }
}
