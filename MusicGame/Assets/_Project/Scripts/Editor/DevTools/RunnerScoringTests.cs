using System.Collections.Generic;
using System.IO;
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
                 $"budget {fight.maxCombatBudget}, compositionInfluence {fight.compositionInfluence}");

        // ── TEST A — normal song, all six types ──────────────────────────────────
        var songA = Song((RingType.Kick, 50, 40), (RingType.Snare, 40, 32), (RingType.HiHat, 100, 80),
                         (RingType.Beat, 70, 56), (RingType.Onset, 30, 24), (RingType.Impact, 10, 8));
        var musA = MusicalPerformanceBuilder.Build(songA, fight.performanceTimingInfluence);
        var buildA = MusicCombatBuildConverter.Build(musA, fight);
        int applicableA = 0;
        foreach (var t in musA.Types) if (t.IsApplicable && t.Type != RingType.Peak) applicableA++;
        log.Check("A: all six musical types applicable", applicableA == 6, $"{applicableA}");
        log.Check($"A: overall performance = 0.80 ({musA.OverallPerformance:0.000})", Mathf.Abs(musA.OverallPerformance - 0.8f) < 1e-4f);
        log.Check($"A: five build stats, points sum to budget ({buildA.TotalPoints:0.00} / {buildA.Budget:0.00})",
                  buildA.Stats.Count == 5 && Mathf.Abs(buildA.TotalPoints - buildA.Budget) < 1e-3f);
        log.Info("A build: " + Describe(buildA));

        // ── TEST B — no HiHat ────────────────────────────────────────────────────
        var songB = Song((RingType.Kick, 40, 32), (RingType.Snare, 30, 24), (RingType.HiHat, 0, 0),
                         (RingType.Beat, 70, 56), (RingType.Onset, 20, 16), (RingType.Impact, 8, 6));
        var musB = MusicalPerformanceBuilder.Build(songB, fight.performanceTimingInfluence);
        var buildB = MusicCombatBuildConverter.Build(musB, fight);
        musB.TryGet(RingType.HiHat, out var hihatB);
        musB.TryGet(RingType.Onset, out var onsetB);
        log.Check("B: HiHat is N/A (not applicable), never a 0% entry", !hihatB.IsApplicable && hihatB.Available == 0);
        float expectedOverallB = (0.8f + 0.8f + 0.8f + 0.8f + 0.75f) / 5f;
        log.Check($"B: overall averages only the 5 existing types ({musB.OverallPerformance:0.0000} == {expectedOverallB:0.0000})",
                  Mathf.Abs(musB.OverallPerformance - expectedOverallB) < 1e-4f);
        buildB.TryGet(FightStatId.Agility, out var agilityB);
        log.Check($"B: Agility quality reweights to 100% Onset ({agilityB.Quality:0.000} == {onsetB.Performance:0.000}), sources '{agilityB.Sources}'",
                  Mathf.Abs(agilityB.Quality - onsetB.Performance) < 1e-4f && !agilityB.UsedFallback);
        // Same player skill (every existing type 80%) with vs. without HiHat -> same total power.
        var songB2 = Song((RingType.Kick, 40, 32), (RingType.Snare, 30, 24), (RingType.Beat, 70, 56), (RingType.Onset, 20, 16), (RingType.Impact, 10, 8));
        var songA2 = Song((RingType.Kick, 40, 32), (RingType.Snare, 30, 24), (RingType.HiHat, 90, 72), (RingType.Beat, 70, 56), (RingType.Onset, 20, 16), (RingType.Impact, 10, 8));
        var bB2 = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(songB2), fight);
        var bA2 = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(songA2), fight);
        log.Check($"B: missing HiHat does not make the fighter globally weaker (budget {bB2.Budget:0.00} == {bA2.Budget:0.00})",
                  Mathf.Abs(bB2.Budget - bA2.Budget) < 1e-3f);
        log.Info("B build: " + Describe(buildB));

        // ── TEST C — event density ───────────────────────────────────────────────
        var c1 = MusicalPerformanceBuilder.Build(Song((RingType.Kick, 20, 16)));
        var c2 = MusicalPerformanceBuilder.Build(Song((RingType.Kick, 65, 52)));
        c1.TryGet(RingType.Kick, out var k1);
        c2.TryGet(RingType.Kick, out var k2);
        log.Check($"C: 16/20 Kick == 52/65 Kick ({k1.Performance:0.000} == {k2.Performance:0.000})", Mathf.Abs(k1.Performance - k2.Performance) < 1e-5f);
        var dense  = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song((RingType.Kick, 20, 16), (RingType.HiHat, 300, 240), (RingType.Snare, 20, 16))), fight);
        var sparse = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song((RingType.Kick, 20, 16), (RingType.HiHat, 20, 16), (RingType.Snare, 20, 16))), fight);
        log.Check($"C: 300 HiHats give no free combat power (budget {dense.Budget:0.00} == {sparse.Budget:0.00})", Mathf.Abs(dense.Budget - sparse.Budget) < 1e-3f);

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

        // ── TEST E — fair combat power ───────────────────────────────────────────
        var e1 = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song(
            (RingType.Kick, 80, 64), (RingType.Snare, 10, 8), (RingType.HiHat, 200, 160), (RingType.Beat, 60, 48), (RingType.Onset, 15, 12), (RingType.Impact, 5, 4))), fight);
        var e2 = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song(
            (RingType.Kick, 15, 12), (RingType.Snare, 90, 72), (RingType.Beat, 40, 32), (RingType.Onset, 60, 48), (RingType.Impact, 20, 16))), fight);
        log.Check($"E: both songs at 80% -> same total budget ({e1.Budget:0.00} == {e2.Budget:0.00}), totals {e1.TotalPoints:0.00} / {e2.TotalPoints:0.00}",
                  Mathf.Abs(e1.Budget - e2.Budget) < 1e-3f && Mathf.Abs(e1.TotalPoints - e2.TotalPoints) < 1e-3f);
        float diff = 0f;
        foreach (var s in e1.Stats) diff = Mathf.Max(diff, Mathf.Abs(s.Points - e2.Get(s.StatId)));
        log.Check($"E: different songs give different builds (largest stat difference {diff:0.00} points)", diff > 1f);
        log.Info("E song 1 (kick/hihat heavy): " + Describe(e1));
        log.Info("E song 2 (snare/onset heavy, no hihat): " + Describe(e2));
        var low = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song((RingType.Kick, 50, 20), (RingType.Snare, 40, 16))), fight);
        log.Check($"E: playing worse earns less total power (40% -> {low.Budget:0.00} < 80% -> {e1.Budget:0.00})", low.Budget < e1.Budget);

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
        log.Check($"F: CombatBuild exposes the five stats, summing to the budget ({cb.PunchPower:0.0}+{cb.KickPower:0.0}+{cb.Agility:0.0}+{cb.Resistance:0.0}+{cb.ImpactPower:0.0} = {cb.Budget:0.00})",
                  Mathf.Abs(cb.PunchPower + cb.KickPower + cb.Agility + cb.Resistance + cb.ImpactPower - cb.Budget) < 1e-3f && cb.BudgetInvariantHolds);
        var sameMusicNoResources = RunnerResultsBuilder.Build(new GameEndedEvent { Stats = stats, Performance = songB }, fight);
        log.Check("F: resources/combos never change musical performance or the build",
                  Mathf.Approximately(sameMusicNoResources.OverallPerformance, results.OverallPerformance) &&
                  Mathf.Approximately(sameMusicNoResources.CombatBuild.PunchPower, cb.PunchPower) &&
                  Mathf.Approximately(sameMusicNoResources.CombatBuild.Budget, cb.Budget));

        // ── Sparse song / invariant everywhere ───────────────────────────────────
        var sparse1 = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song((RingType.Kick, 3, 1))), fight);
        var empty   = MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(Song()), fight);
        bool noNaN = true;
        foreach (var b2 in new[] { sparse1, empty })
            foreach (var st in b2.Stats) noNaN &= !float.IsNaN(st.Points) && !float.IsNaN(st.Share);
        log.Check($"Sparse: only Kick (1/3) -> no NaN, budget from overall ({sparse1.Budget:0.00} == {fight.maxCombatBudget / 3f:0.00}), sums to budget",
                  noNaN && Mathf.Abs(sparse1.Budget - fight.maxCombatBudget * fight.performanceToBudget.Evaluate(1f / 3f)) < 1e-3f && sparse1.BudgetInvariantHolds);
        log.Check($"Sparse: no musical type at all -> budget {empty.Budget:0.00}, no NaN, invariant holds", noNaN && empty.BudgetInvariantHolds);
        log.Check("Invariant: every build above sums to its budget",
                  buildA.BudgetInvariantHolds && buildB.BudgetInvariantHolds && e1.BudgetInvariantHolds && e2.BudgetInvariantHolds && low.BudgetInvariantHolds && dense.BudgetInvariantHolds);

        // ── TEST G — real generated timelines (cached SongProfiles of real songs) ───────
        RealTimelines(log, gameplay);

        // ── Config sanity ─────────────────────────────────────────────────────────
        log.Check("Config: Life/Special are resources, not musical", RingTypes.IsResource(RingType.Life) && RingTypes.IsResource(RingType.Special) &&
                  !System.Array.Exists(RingTypes.Musical, t => RingTypes.IsResource(t)));
        log.Check("Config: build weights reference only musical sources", AllBuildSourcesMusical(fight));
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
        foreach (var m in fight.buildStats)
            foreach (var s in m.sources)
                if (RingTypes.IsResource(s.source)) return false;
        return true;
    }

    private static string Describe(MusicCombatBuild b)
    {
        var sb = new StringBuilder($"budget {b.Budget:0.0} (overall {b.OverallPerformance:0.00}) —");
        foreach (var s in b.Stats) sb.Append($" {s.StatId} {s.Points:0.0} [{s.Sources}];");
        return sb.ToString();
    }
}
