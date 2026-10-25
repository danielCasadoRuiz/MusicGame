using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Acceptance tests for the Runner scoring / pickup-result / combat-conversion pipeline — pure
/// logic, driven through the REAL classes (PickupComboTracker, MusicalPerformanceBuilder,
/// MusicCombatBuildConverter, RunnerResultsBuilder, FighterStatsBuilder, GameSession.AddRunResources)
/// with the project's REAL config assets. Tools > MusicGame > Run Runner Scoring Tests, or batch:
/// -executeMethod RunnerScoringTests.RunFromCommandLine -quit  (writes Logs/RunnerScoringTests.txt).
/// </summary>
public static class RunnerScoringTests
{
    private const string GameplayConfigPath = "Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset";
    private const string FightStatsPath     = "Assets/_Project/Configs/Fight/FightStatsConfig.asset";

    [MenuItem("Tools/MusicGame/Run Runner Scoring Tests")]
    public static void RunMenu() => Debug.Log(Run(out _));

    public static void RunFromCommandLine()
    {
        string report = Run(out bool ok);
        File.WriteAllText("Logs/RunnerScoringTests.txt", report);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private class Log
    {
        private readonly StringBuilder _sb = new();
        private int _pass, _fail;
        public void Info(string s) => _sb.AppendLine("  INFO  " + s);
        public void Check(string name, bool ok, string detail = "")
        {
            if (ok) { _pass++; _sb.AppendLine("  PASS  " + name); }
            else { _fail++; _sb.AppendLine($"  FAIL  {name} — {detail}"); }
        }
        public string Finish(out bool ok)
        {
            ok = _fail == 0;
            string r = $"[RunnerScoringTests] {(ok ? "ALL PASSED" : "FAILURES")} — {_pass} passed, {_fail} failed\n{_sb}";
            if (ok) Debug.Log(r); else Debug.LogError(r);
            return r;
        }
    }

    private static GamePerformance Song(params (RingType type, int available, int collected)[] types)
    {
        var perf = new GamePerformance();
        foreach (var (type, available, collected) in types)
        {
            if (available <= 0) continue; // GameplayManager omits types the timeline never generated
            perf.ByType[type] = new TypePerformance
            {
                Type = type, Available = available, Collected = collected,
                CollectionRate = (float)collected / available, TimingAccuracy = 0.9f,
            };
        }
        return perf;
    }

    public static string Run(out bool ok)
    {
        var log = new Log();
        var gameplay = AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(GameplayConfigPath);
        var fight    = AssetDatabase.LoadAssetAtPath<FightStatsConfig>(FightStatsPath);
        log.Check("Config assets load (MusicRunnerGameplayConfig, FightStatsConfig)", gameplay != null && fight != null);
        if (gameplay == null || fight == null) return log.Finish(out ok);
        var scoring = gameplay.scoring;
        log.Info($"combo window {scoring.comboWindowSeconds:0.00} s, triple {scoring.tripleComboPickups}, quad {scoring.quadComboPickups}, " +
                 $"build base {fight.baseStatPoints}, cap {fight.maxStatPoints}");

        // ── TEST A — direct correspondences (one bonus type at a time vs none) ───────────
        var none = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song()), fight);
        foreach (var st in MusicCombatBuildConverter.Stats)
            log.Check($"A: no bonus -> {st} at its base {fight.baseStatPoints}", Mathf.Approximately(none.Get(st), fight.baseStatPoints));
        var expected = new (RingType type, FightStatId[] feeds)[]
        {
            (RingType.Kick,   new[] { FightStatId.KickPower }),
            (RingType.Snare,  new[] { FightStatId.PunchPower }),
            (RingType.HiHat,  new[] { FightStatId.Agility }),
            (RingType.Beat,   new[] { FightStatId.Resistance }),
            (RingType.Onset,  new[] { FightStatId.PunchPower, FightStatId.KickPower, FightStatId.Resistance, FightStatId.Agility }),
            (RingType.Impact, new[] { FightStatId.PunchPower, FightStatId.KickPower }),
            (RingType.Peak,   new FightStatId[0]),
        };
        foreach (var (type, feeds) in expected)
        {
            var one = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song((type, 10, 10))), fight);
            var wrong = new List<string>();
            foreach (var st in MusicCombatBuildConverter.Stats)
            {
                bool shouldGrow = System.Array.IndexOf(feeds, st) >= 0;
                float gain = one.Get(st) - none.Get(st);
                float per = fight.Contribution(type)?.Get(st) ?? 0f;
                if (shouldGrow ? !(gain > 0f && Mathf.Abs(gain - 10f * per) < 1e-3f) : Mathf.Abs(gain) > 1e-5f) wrong.Add($"{st} {gain:+0.00;-0.00;0}");
            }
            log.Check($"A: 10 x {type} feeds only [{string.Join(", ", feeds)}] by exactly 10 x its points", wrong.Count == 0, string.Join(", ", wrong));
        }
        log.Check("A: Onset and Impact are musical bonuses, never charges / lives",
                  RingTypes.IsMusical(RingType.Onset) && RingTypes.IsMusical(RingType.Impact) && !RingTypes.IsResource(RingType.Onset) && !RingTypes.IsResource(RingType.Impact));

        // ── TEST B — cumulative, independent, monotonic ──────────────────────────────
        var withoutKick = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song((RingType.Snare, 20, 12), (RingType.Beat, 20, 10))), fight);
        var plusKick = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song((RingType.Snare, 20, 12), (RingType.Beat, 20, 10), (RingType.Kick, 30, 25))), fight);
        log.Check("B: adding Kick never lowers or redistributes Punch / Resistance / Agility",
                  Mathf.Approximately(withoutKick.PunchPower, plusKick.PunchPower) && Mathf.Approximately(withoutKick.Resistance, plusKick.Resistance)
                  && Mathf.Approximately(withoutKick.Agility, plusKick.Agility) && plusKick.KickPower > withoutKick.KickPower);
        var rng = new System.Random(7);
        int nonMonotonic = 0;
        RingType[] musicalTypes = { RingType.Kick, RingType.Snare, RingType.HiHat, RingType.Beat, RingType.Onset, RingType.Impact };
        for (int trial = 0; trial < 300; trial++)
        {
            var picks = musicalTypes.Select(_ => rng.Next(0, 120)).ToArray();
            int bump = rng.Next(musicalTypes.Length);
            var before = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song(musicalTypes.Select((t, i) => (t, 150, picks[i])).ToArray())), fight);
            picks[bump] = Mathf.Min(150, picks[bump] + 1 + rng.Next(20));
            var after = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song(musicalTypes.Select((t, i) => (t, 150, picks[i])).ToArray())), fight);
            foreach (var st in MusicCombatBuildConverter.Stats) if (after.Get(st) < before.Get(st) - 1e-5f) nonMonotonic++;
        }
        log.Check("B: 300 random builds - collecting more never lowers any stat (cap included)", nonMonotonic == 0, $"{nonMonotonic} decreases");
        var huge = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song((RingType.Snare, 900, 900), (RingType.Onset, 300, 300))), fight);
        float flownPunch = huge.Contributions.Where(c => c.Stat == FightStatId.PunchPower).Sum(c => c.Points);
        log.Check($"B: a huge haul stops at the cap ({huge.PunchPower:0.0} = {fight.maxStatPoints}) and its contributions still add up exactly",
                  Mathf.Approximately(huge.PunchPower, fight.maxStatPoints) && Mathf.Abs(flownPunch - (huge.PunchPower - fight.baseStatPoints)) < 1e-3f);

        // ── TEST C — no fallback: a missing bonus type adds nothing ──────────────────
        var noHat = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song((RingType.Kick, 40, 30), (RingType.Snare, 40, 30), (RingType.Beat, 40, 30))), fight);
        log.Check("C: a song without HiHat / Onset leaves Agility at its base (never borrowed from other types or the overall)",
                  Mathf.Approximately(noHat.Agility, fight.baseStatPoints));
        var sameCountsLowerRate = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song((RingType.Kick, 400, 30), (RingType.Snare, 400, 30), (RingType.Beat, 400, 30))), fight);
        log.Check("C: the build counts what was collected (same pickups -> same build, whatever the song offered)",
                  MusicCombatBuildConverter.Stats.All(st => Mathf.Approximately(noHat.Get(st), sameCountsLowerRate.Get(st))));
        // Falls take bonuses away (the live counter): the build converts what is still HELD, never what was lost.
        var held = new GamePerformance();
        held.ByType[RingType.Kick] = new TypePerformance { Type = RingType.Kick, Available = 40, Collected = 23, Kept = 12, CollectionRate = 23f / 40f, TimingAccuracy = 0.9f };
        var heldBuild = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(held), fight);
        log.Check("C: after falls the build uses the HELD count (12 of 23 collected), the same number the HUD showed",
                  Mathf.Abs(heldBuild.KickPower - (fight.baseStatPoints + 12 * fight.Contribution(RingType.Kick).kickPower)) < 1e-3f, $"{heldBuild.KickPower:0.00}");
        var songB = Song((RingType.Kick, 40, 32), (RingType.Snare, 30, 24), (RingType.HiHat, 0, 0),
                         (RingType.Beat, 70, 56), (RingType.Onset, 20, 16), (RingType.Impact, 8, 6));

        // ── TEST D — combos ──────────────────────────────────────────────────────
        float w = scoring.comboWindowSeconds;
        var combo = new PickupComboTracker(scoring);
        combo.Register(10f); combo.Register(10f + w * 0.3f); combo.Register(10f + w * 0.6f);
        log.Check("D: a Triple is pending (not granted) while its window is still open", combo.TripleCount == 0 && combo.PendingCount == 3);
        combo.Tick(10f + w + 0.01f);
        log.Check($"D: 3 pickups inside the window -> Triple ({combo.TripleCount} triple, {combo.QuadCount} quad)", combo.TripleCount == 1 && combo.QuadCount == 0);

        combo.Reset();
        for (int i = 0; i < 4; i++) combo.Register(20f + i * w * 0.2f);
        combo.Tick(30f);
        log.Check($"D: 4 pickups in one sequence -> one Quad, no Triple ({combo.TripleCount} triple, {combo.QuadCount} quad)", combo.QuadCount == 1 && combo.TripleCount == 0);

        combo.Reset();
        combo.Register(40f); combo.Register(40f + w * 0.5f); combo.Register(40f + w + 0.3f); combo.Tick(50f);
        log.Check("D: pickups spread beyond the window form no combo", combo.TripleCount == 0 && combo.QuadCount == 0);

        combo.Reset();
        for (int i = 0; i < 8; i++) combo.Register(60f + i * 0.05f);
        combo.Tick(70f);
        log.Check($"D: 8 rapid pickups -> 2 Quads (sequence closes at 4) ({combo.QuadCount})", combo.QuadCount == 2 && combo.TripleCount == 0);

        combo.Reset();
        combo.Register(80f); combo.Register(80.1f); combo.Cancel(); combo.Register(80.2f); combo.Tick(90f);
        log.Check("D: a fall (Cancel) drops the sequence in progress", combo.TripleCount == 0 && combo.QuadCount == 0);

        var stats = new CollectionStats();
        stats.AddScore(100);
        var tracker = new RunnerResourceTracker(scoring);
        for (int i = 0; i < 4; i++) tracker.OnMusicalPickup(100f + i * 0.1f);
        tracker.OnResourcePickup(RingType.Life);
        tracker.OnResourcePickup(RingType.Special);
        tracker.Flush();
        var counts = tracker.Counts;
        log.Check($"D: TotalScore is NOT multiplied by combos ({stats.Score} == 100); resources tracked (quad {counts.QuadCombos}, life {counts.Lives}, special {counts.Specials})",
                  stats.Score == 100 && counts.QuadCombos == 1 && counts.TripleCombos == 0 && counts.Lives == 1 && counts.Specials == 1);

        // ── TEST E — different songs, different specialisations (no normalisation) ──────
        var e1 = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song(
            (RingType.Kick, 80, 64), (RingType.Snare, 10, 8), (RingType.HiHat, 200, 160), (RingType.Beat, 60, 48), (RingType.Onset, 15, 12), (RingType.Impact, 5, 4))), fight);
        var e2 = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song(
            (RingType.Kick, 15, 12), (RingType.Snare, 90, 72), (RingType.Beat, 40, 32), (RingType.Onset, 60, 48), (RingType.Impact, 3, 2))), fight);
        log.Check("E: a kick-heavy song favours KickPower, a snare-heavy one PunchPower", e1.KickPower > e2.KickPower && e2.PunchPower > e1.PunchPower);
        log.Info("E song 1 (kick/hihat heavy): " + Describe(e1));
        log.Info("E song 2 (snare/onset heavy, no hihat): " + Describe(e2));

        // ── TEST F — data persistence through the session model ──────────────────
        stats = new CollectionStats();
        stats.Register(RingType.Kick);
        stats.AddScore(1234);
        var ended = new GameEndedEvent
        {
            Stats = stats, FallCount = 1, NormalizedScore = 0.7f, MaxPossibleScore = 2000, Performance = songB,
            Resources = new RunnerResourceCounts { TripleCombos = 4, QuadCombos = 2, Lives = 3, Specials = 1 },
        };
        var results = RunnerResultsBuilder.Build(ended, fight);
        log.Check($"F: RunnerResults carries TotalScore ({results.TotalScore}), musical performance, overall ({results.OverallPerformance:0.00})",
                  results.TotalScore == 1234 && results.Musical != null && results.Musical.Types.Count == RingTypes.Musical.Length);
        log.Check("F: RunnerResults carries resources (x3 4, x4 2, lives 3, specials 1)",
                  results.Resources.TripleCombos == 4 && results.Resources.QuadCombos == 2 && results.Resources.Lives == 3 && results.Resources.Specials == 1);
        var cb = results.CombatBuild;
        log.Check($"F: CombatBuild exposes the four stats ({cb.PunchPower:0.0} / {cb.KickPower:0.0} / {cb.Resistance:0.0} / {cb.Agility:0.0})",
                  cb.Stats.Count == 4 && MusicCombatBuildConverter.Stats.All(st => cb.TryGet(st, out _)));
        var sameMusicNoResources = RunnerResultsBuilder.Build(new GameEndedEvent { Stats = stats, Performance = songB }, fight);
        log.Check("F: resources/combos never change musical performance or the build",
                  Mathf.Approximately(sameMusicNoResources.OverallPerformance, results.OverallPerformance) &&
                  MusicCombatBuildConverter.Stats.All(st => Mathf.Approximately(sameMusicNoResources.CombatBuild.Get(st), cb.Get(st))));
        var otherScore = new CollectionStats(); otherScore.AddScore(99999);
        var scored = RunnerResultsBuilder.Build(new GameEndedEvent { Stats = otherScore, Performance = songB }, fight);
        log.Check("F: the runner score never enters the build", MusicCombatBuildConverter.Stats.All(st => Mathf.Approximately(scored.CombatBuild.Get(st), cb.Get(st))));

        // ── Sparse / empty songs ─────────────────────────────────────────────────
        var sparse1 = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song((RingType.Kick, 3, 1))), fight);
        var empty   = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song()), fight);
        bool noNaN = true;
        foreach (var b2 in new[] { sparse1, empty })
            foreach (var st in b2.Stats) noNaN &= !float.IsNaN(st.Points);
        log.Check("Sparse: one Kick / nothing at all -> no NaN, every stat >= its base",
                  noNaN && sparse1.Stats.All(x => x.Points >= fight.baseStatPoints) && empty.Stats.All(x => Mathf.Approximately(x.Points, fight.baseStatPoints)));

        // ── TEST G — real generated timelines (cached SongProfiles of real songs) ───────
        RealTimelines(log, gameplay);

        // ── Config sanity ─────────────────────────────────────────────────────────
        log.Check("Config: Life/Special are resources, not musical", RingTypes.IsResource(RingType.Life) && RingTypes.IsResource(RingType.Special) &&
                  !System.Array.Exists(RingTypes.Musical, t => RingTypes.IsResource(t)));
        log.Check("Config: build contributions reference only musical sources", AllBuildSourcesMusical(fight));
        return log.Finish(out ok);
    }

    /// <summary>Generates the REAL GameplayTimeline for every cached song profile (on a straight test
    /// path) and checks what the Runner will actually offer: resource pickups exist only inside the
    /// played window, keep clear of musical pickups, never count as musical; the availability count
    /// (played window only) is what the HUD / performance use.</summary>
    private static void RealTimelines(Log log, MusicRunnerGameplayConfig gameplay)
    {
        string dir = Path.Combine(Application.persistentDataPath, "SongCache");
        if (!Directory.Exists(dir)) { log.Info($"G: no SongCache at {dir} — skipped"); return; }

        var samples = new MusicPath.Sample[4000];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = new MusicPath.Sample(new Vector3(0f, 0f, i * 1f), Vector3.forward, i * 1f, 6f);
        var path = new MusicPath(samples);

        var c = gameplay.collectibles;
        float warmup = gameplay.core.warmupTime;
        int songs = 0, badWindow = 0, badGap = 0, noLife = 0, noSpecial = 0, hiddenTypes = 0;
        var lines = new StringBuilder();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            SongProfile profile;
            try { profile = JsonUtility.FromJson<SongProfileData>(File.ReadAllText(file))?.ToProfile(); }
            catch { continue; }
            if (profile == null || profile.duration <= 0f) continue;
            songs++;

            var timeline = GameplayTimeline.Generate(profile, gameplay, path);
            var range = PlayRangeResolver.Resolve(gameplay.core, profile.duration);
            float start = warmup + range.Start, end = warmup + range.End;

            var available = new Dictionary<RingType, int>();
            int total = 0;
            foreach (var e in timeline.Events)
            {
                total++;
                bool inWindow = e.eventTime >= start - 0.05f && e.eventTime < end;
                if (inWindow) { available.TryGetValue(e.ringType, out int n); available[e.ringType] = n + 1; }
                if (!RingTypes.IsResource(e.ringType)) continue;
                if (!inWindow) badWindow++;
                foreach (var o in timeline.Events)
                    if (!o.Equals(e) && Mathf.Abs(o.eventTime - e.eventTime) < c.resourcePickupMinGap - 1e-3f) { badGap++; break; }
            }
            available.TryGetValue(RingType.Life, out int lives);
            available.TryGetValue(RingType.Special, out int specials);
            if (c.spawnLife && c.lifePickupsPerMinute > 0f && Mathf.RoundToInt((range.End - range.Start) / 60f * c.lifePickupsPerMinute) > 0 && lives == 0) noLife++;
            if (c.spawnSpecial && c.maxSpecialPickups > 0 && specials == 0) noSpecial++;

            var shown = new List<string>();
            foreach (var t in RingTypes.Musical)
            {
                available.TryGetValue(t, out int n);
                if (n > 0) shown.Add($"{t} {n}"); else if (t != RingType.Peak) hiddenTypes++;
            }
            lines.Append($"\n        {Path.GetFileNameWithoutExtension(file)}: window {range.Start:0}-{range.End:0}s of {profile.duration:0}s, " +
                         $"{total} events in timeline, available [{string.Join(", ", shown)}], Life {lives}, Special {specials}");
        }
        log.Info($"G: {songs} real songs:{lines}");

        // Life/Special configured to ZERO: valid, no resource pickups, musical availability unchanged.
        var noRes = Object.Instantiate(gameplay);
        noRes.collectibles = Object.Instantiate(gameplay.collectibles);
        noRes.collectibles.maxLifePickups = 0;
        noRes.collectibles.maxSpecialPickups = 0;
        int zeroBad = 0, zeroSongs = 0;
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            SongProfile profile;
            try { profile = JsonUtility.FromJson<SongProfileData>(File.ReadAllText(file))?.ToProfile(); }
            catch { continue; }
            if (profile == null || profile.duration <= 0f) continue;
            if (++zeroSongs > 5) break;
            var with    = GameplayTimeline.Generate(profile, gameplay, path);
            var without = GameplayTimeline.Generate(profile, noRes, path);
            int musicalWith = 0, musicalWithout = 0, resourcesWithout = 0;
            foreach (var e in with.Events) if (RingTypes.IsMusical(e.ringType)) musicalWith++;
            foreach (var e in without.Events) { if (RingTypes.IsMusical(e.ringType)) musicalWithout++; else resourcesWithout++; }
            if (musicalWith != musicalWithout || resourcesWithout != 0) zeroBad++;
        }
        log.Check("G: Life = 0 / Special = 0 config -> no resource pickups, musical pickups identical", zeroBad == 0, $"{zeroBad} songs differ");
        Object.DestroyImmediate(noRes.collectibles);
        Object.DestroyImmediate(noRes);
        if (songs == 0) return;
        log.Check("G: every Life/Special pickup lies inside the played window", badWindow == 0, $"{badWindow} outside");
        log.Check($"G: every Life/Special pickup keeps {c.resourcePickupMinGap:0.00} s clear of other pickups", badGap == 0, $"{badGap} too close");
        log.Check("G: every song offers its Life pickups", noLife == 0, $"{noLife} songs without");
        log.Check("G: every song offers a Special pickup", noSpecial == 0, $"{noSpecial} songs without");
        log.Info($"G: musical types absent from a song's played window (would be hidden / N/A): {hiddenTypes} across all songs");
    }

    private static bool AllBuildSourcesMusical(FightStatsConfig fight)
    {
        foreach (var c in fight.contributions)
            if (c != null && RingTypes.IsResource(c.source)) return false;
        return true;
    }

    private static string Describe(MusicCombatBuild b)
    {
        var sb = new StringBuilder($"total {b.TotalPoints:0.0} —");
        foreach (var s in b.Stats) sb.Append($" {s.StatId} {s.Points:0.0}{(s.Capped ? " (cap)" : "")};");
        return sb.ToString();
    }
}
