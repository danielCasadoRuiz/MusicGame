#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using MusicGame.Audio.Analysis;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Diagnostic: where the musical bonuses of the PLAYED fragment (PlayRangeResolver window) are lost, per
/// published song and campaign position — detections in the window, then the same gates as
/// GameplayTimeline.SelectDetectedCandidates in its order (strength floor, near-silent energy, merged into an
/// Impact, minimum spacing, difficulty quota), then the musical objects really emitted by Generate in the window.
/// The gates are re-applied over the WHOLE song exactly as the selection does (spacing and quota are song-wide);
/// only the counting is limited to the window. Read-only: no asset or stored layout is touched.
/// Batch: -executeMethod BonusDensityReport.RunFromCommandLine [-densityOut FILE] [-densityPositions L1#1,L4#6]
/// </summary>
public static class BonusDensityReport
{
    private const string ConfigPath = "Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset";

    public static void RunFromCommandLine()
    {
        string outPath = Path.GetFullPath("Logs/BonusDensityReport.txt");
        string positions = "L1#1,L1#5,L2#3,L3#3,L4#6";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-densityOut") outPath = args[i + 1];
            if (args[i] == "-densityPositions") positions = args[i + 1];
        }
        int code = 0;
        try { File.WriteAllText(outPath, Build(positions.Split(','))); }
        catch (System.Exception ex) { Debug.LogException(ex); code = 2; }
        EditorApplication.Exit(code);
    }

    [MenuItem("MusicGame/Dev/Bonus Density Report")]
    public static void RunFromMenu() => Debug.Log(Build(new[] { "L1#1", "L1#5", "L2#3", "L3#3", "L4#6" }));

    public static string Build(string[] positions)
    {
        var config   = AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(ConfigPath);
        var campaign = AssetDatabase.FindAssets("t:CampaignConfigSO").Select(g => AssetDatabase.LoadAssetAtPath<CampaignConfigSO>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
        var app      = Resources.Load<AppConfigSO>("AppConfig");
        var sb = new StringBuilder();
        var sel = config.collectibles.selection;
        sb.AppendLine($"selection: minInterval={sel.minInterval} strengthFloor={sel.strengthFloor} share={sel.easyShare}->{sel.hardShare} " +
                      $"impactMerge={sel.impactMergeWindow} energyThreshold={config.levelGeneration.energyThreshold} playerSpeed={config.core.playerSpeed}");
        sb.AppendLine("song      pos   diff  window   | inWin  -str  -energy -impact -spacing -quota | selected emitted  /s");

        var totals = new System.Collections.Generic.Dictionary<string, (int win, int str, int en, int imp, int sp, int q, int sel, int emit, float secs)>();
        // Per musical type actually emitted in the window (what the runner offers to collect — the combat build's inputs).
        var types = new System.Text.StringBuilder();
        RingType[] musicalTypes = { RingType.Kick, RingType.Snare, RingType.HiHat, RingType.Beat, RingType.Onset, RingType.Impact, RingType.Peak };
        types.AppendLine("PER TYPE (emitted in the played window)");
        types.AppendLine($"song      pos   | {string.Join(" ", musicalTypes.Select(t => $"{t,6}"))}");
        foreach (string file in Directory.GetFiles(SongContentKeys.ProfileFolder, "*.mgsp.bytes").OrderBy(f => f))
        {
            string songId = Path.GetFileName(file).Replace(".mgsp.bytes", "");
            var profile = SongAnalysisPipeline.LoadProfile(File.ReadAllBytes(file), new DerivedAnalysisSettings(app.audioAnalysis), out _);
            if (profile?.onsetEvents == null || profile.onsetEvents.Length == 0) continue;
            var path = new SnakeWayWorldGenerator().Generate(profile, config, new Vector3(0f, config.levelGeneration.pathBaseHeight, 0f));
            var range = PlayRangeResolver.Resolve(config.core, profile.duration);
            foreach (string pos in positions)
            {
                var parts = pos.Split('#');
                float d = campaign != null ? campaign.RunnerDifficultyAt(parts[0], int.Parse(parts[1]), 1) : 0.15f;
                var r = Count(profile, config, path, d, range.Start, range.End, pos);
                float secs = range.End - range.Start;
                sb.AppendLine($"{songId,-9} {pos,-5} {d:0.00}  {range.Start,3:0}-{range.End,3:0}s | {r.win,5} {r.str,5} {r.en,7} {r.imp,7} {r.sp,8} {r.q,6} | {r.sel,8} {r.emit,7} {r.emit / secs,4:0.00}");
                var timeline = GameplayTimeline.Generate(profile, config, path, BonusLayoutProvider.KeyFor(profile, config, d, pos, 0).Seed, d);
                var counts = musicalTypes.Select(rt => timeline.Events.Count(e => e.ringType == rt && e.eventTime - config.core.warmupTime >= range.Start
                                                                                && e.eventTime - config.core.warmupTime < range.End)).ToArray();
                int impactsAll = profile.impactTimes?.Length ?? 0;
                int impactsWin = profile.impactTimes?.Count(t => t >= range.Start && t < range.End) ?? 0;
                int macroWin = timeline.MacroEvents.Count(m => m.type == MacroEventType.Impact && m.eventTime - config.core.warmupTime >= range.Start && m.eventTime - config.core.warmupTime < range.End);
                types.AppendLine($"{songId,-9} {pos,-5} | {string.Join(" ", counts.Select(c => $"{c,6}"))} | impactTimes {impactsAll} (window {impactsWin}), macro Impact in window {macroWin}, " +
                                 $"beats {profile.beatTimes?.Length ?? 0}, syntheticFill {sel.syntheticFill}, impactThreshold {app.audioAnalysis?.impactThreshold}");
                totals.TryGetValue(pos, out var t);
                totals[pos] = (t.win + r.win, t.str + r.str, t.en + r.en, t.imp + r.imp, t.sp + r.sp, t.q + r.q, t.sel + r.sel, t.emit + r.emit, t.secs + secs);
            }
        }
        sb.AppendLine("TOTAL (all songs)");
        foreach (var kv in totals)
        {
            var t = kv.Value;
            sb.AppendLine($"  {kv.Key,-5} inWin={t.win} -strength={t.str} -energy={t.en} -impact={t.imp} -spacing={t.sp} -quota={t.q} " +
                          $"selected={t.sel} emitted={t.emit} ({t.emit / Mathf.Max(1f, t.secs):0.00}/s)");
        }
        sb.Append(types);
        sb.Append(HazardsAndImpacts(app, config));
        return sb.ToString();
    }

    // Hazards per difficulty (obstacles, chasers, overlapping chasers, hard kinds) + Impact lateral spread.
    private static string HazardsAndImpacts(AppConfigSO app, MusicRunnerGameplayConfig config)
    {
        var sb = new StringBuilder();
        var hz = AssetDatabase.FindAssets("t:RunnerHazardsConfig").Select(g => AssetDatabase.LoadAssetAtPath<RunnerHazardsConfig>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
        if (hz == null) return "HAZARDS: no RunnerHazardsConfig" + System.Environment.NewLine;
        float[] ds = { 0.15f, 0.5f, 0.85f };
        var sums = new (int obstacles, int chasers, int overlaps, int hard)[ds.Length];
        var lateral = new System.Collections.Generic.List<float>();
        int songs = 0;
        foreach (string file in Directory.GetFiles(SongContentKeys.ProfileFolder, "*.mgsp.bytes").OrderBy(f => f))
        {
            var profile = SongAnalysisPipeline.LoadProfile(File.ReadAllBytes(file), new DerivedAnalysisSettings(app.audioAnalysis), out _);
            if (profile?.onsetEvents == null || profile.onsetEvents.Length == 0) continue;
            songs++;
            var path = new SnakeWayWorldGenerator().Generate(profile, config, new Vector3(0f, config.levelGeneration.pathBaseHeight, 0f));
            var range = PlayRangeResolver.Resolve(config.core, profile.duration);
            float warm = config.core.warmupTime, speed = config.core.playerSpeed;
            for (int i = 0; i < ds.Length; i++)
            {
                var tl = GameplayTimeline.Generate(profile, config, path, 1234, ds[i]);
                if (i == 1) foreach (var e in tl.Events) if (e.ringType == RingType.Impact) lateral.Add(e.lateralOffset);
                var placements = HazardLayout.Generate(hz, tl.Events.ToArray(), path, 1234, ds[i], range.Start + warm, range.End + warm, speed);
                var chasers = placements.Where(p => p.isChaser).Select(p => p.songTime).OrderBy(t => t).ToList();
                float dur = RunnerHazardsConfig.Lerp(hz.easyChaserDuration, hz.hardChaserDuration, ds[i]);
                int overlaps = 0;
                for (int k = 1; k < chasers.Count; k++) if (chasers[k] - chasers[k - 1] < dur) overlaps++;
                int hard = placements.Count(p => !p.isChaser && hz.UnlockFor(p.kind) >= 0.3f);
                sums[i].obstacles += placements.Count(p => !p.isChaser); sums[i].chasers += chasers.Count; sums[i].overlaps += overlaps; sums[i].hard += hard;
            }
        }
        sb.AppendLine($"HAZARDS per played window, mean over {songs} songs:");
        for (int i = 0; i < ds.Length; i++)
            sb.AppendLine($"  difficulty {ds[i]:0.00}: obstacles {sums[i].obstacles / (float)Mathf.Max(1, songs):0.0} (hard kinds {sums[i].hard / (float)Mathf.Max(1, songs):0.0}), " +
                          $"chasers {sums[i].chasers / (float)Mathf.Max(1, songs):0.0}, overlapping chasers {sums[i].overlaps}");
        if (lateral.Count > 0)
            sb.AppendLine($"IMPACT lateral offsets (difficulty 0.5): n {lateral.Count}, min {lateral.Min():0.00}, max {lateral.Max():0.00}, centred (|x|<0.05) {lateral.Count(x => Mathf.Abs(x) < 0.05f)}");
        return sb.ToString();
    }

    private static (int win, int str, int en, int imp, int sp, int q, int sel, int emit) Count(
        SongProfile profile, MusicRunnerGameplayConfig config, MusicPath path, float difficulty, float start, float end, string position)
    {
        var sel = config.collectibles.selection;
        float warmup = config.core.warmupTime;
        float minInterval = sel.MinInterval(difficulty), floor = sel.StrengthFloor(difficulty);
        bool In(float clipTime) => clipTime >= start && clipTime < end;

        var macro = GameplayTimeline.Generate(profile, config, path, 1, difficulty).MacroEvents;
        var impacts = macro.Where(m => m.type == MacroEventType.Impact).Select(m => m.eventTime - warmup).ToList();
        var ev = profile.onsetEvents;
        int win = 0, str = 0, en = 0, imp = 0, sp = 0, q = 0;
        var ranked = new System.Collections.Generic.List<(int index, float priority)>();
        for (int k = 0; k < ev.Length; k++)
        {
            var e = ev[k];
            if (e.origin != OnsetOrigin.Detected) continue;
            bool w = In(e.time);
            if (w) win++;
            if (e.strength < floor) { if (w) str++; continue; }
            if (profile.GetEnergyAt(e.time) < profile.averageEnergy * config.levelGeneration.energyThreshold) { if (w) en++; continue; }
            if (impacts.Any(x => Mathf.Abs(x - e.time) <= sel.impactMergeWindow)) { if (w) imp++; continue; }
            float conf = Mathf.Max(Mathf.Max(e.kickConfidence, e.snareConfidence), Mathf.Max(e.hihatConfidence, e.genericConfidence));
            float priority = e.strength * (0.6f + 0.4f * conf);
            if (profile.beatTimes != null && profile.beatTimes.Any(b => Mathf.Abs(b - e.time) <= sel.onBeatWindow)) priority += sel.onBeatPriority;
            ranked.Add((k, priority));
        }
        ranked.Sort((a, b) => a.priority != b.priority ? b.priority.CompareTo(a.priority) : a.index.CompareTo(b.index));
        var times = new System.Collections.Generic.List<float>();
        var accepted = new System.Collections.Generic.List<int>();
        foreach (var (index, _) in ranked)
        {
            float t = ev[index].time;
            int p = times.BinarySearch(t);
            if (p >= 0) { if (In(t)) sp++; continue; }
            p = ~p;
            if ((p > 0 && t - times[p - 1] < minInterval) || (p < times.Count && times[p] - t < minInterval)) { if (In(t)) sp++; continue; }
            times.Insert(p, t);
            accepted.Add(index);
        }
        int quota = Mathf.Max(1, Mathf.RoundToInt(sel.Share(difficulty) * accepted.Count));
        for (int k = quota; k < accepted.Count; k++) if (In(ev[accepted[k]].time)) q++;
        int selected = accepted.Take(quota).Count(k => In(ev[k].time));

        var timeline = GameplayTimeline.Generate(profile, config, path, BonusLayoutProvider.KeyFor(profile, config, difficulty, position, 0).Seed, difficulty);
        int emitted = timeline.Events.Count(e => e.origin == BonusOrigin.Detected && In(e.eventTime - warmup));
        return (win, str, en, imp, sp, q, selected, emitted);
    }
}
#endif
