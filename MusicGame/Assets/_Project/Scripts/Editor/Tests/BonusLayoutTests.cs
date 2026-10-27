#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicGame.Audio.Analysis;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode tests of the P4 bonus layouts on a REAL published profile (song_008) with the project's
/// gameplay config: repeatability (same key ⇒ byte-identical layout), variety between combinations,
/// persistence (generated once, loaded after, rebuilt identically when the file is missing or corrupt),
/// compatibility (profile revision / generator / config in the key, stale revisions pruned), continuous
/// difficulty (per level AND per match, monotonic selection), accessibility, one object per moment (no
/// duplicates from simultaneous labels or Impacts), identifiable / optional synthetic fill, and sync
/// (a detected bonus sits exactly at warmup + its attack time). Layout files go to a temp folder.
/// Run: -runTests -testPlatform EditMode -testFilter BonusLayoutTests
/// </summary>
public class BonusLayoutTests
{
    private const string ConfigPath = "Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset";
    private MusicRunnerGameplayConfig _config;
    private SongProfile _profile;
    private MusicPath _path;
    private string _tmp;

    [SetUp]
    public void SetUp()
    {
        RunnerDifficultyOverride.Clear();
        var asset = AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(ConfigPath);
        Assert.IsNotNull(asset, ConfigPath);
        _config = Clone(asset);
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var bytes = File.ReadAllBytes(SongContentKeys.ProfileAssetPath("song_008"));
        _profile = SongAnalysisPipeline.LoadProfile(bytes, new DerivedAnalysisSettings(app.audioAnalysis), out _);
        _path = new SnakeWayWorldGenerator().Generate(_profile, _config, new Vector3(0f, _config.levelGeneration.pathBaseHeight, 0f));
        _tmp = Path.Combine(Path.GetTempPath(), "MusicGameBonusLayoutTests_" + System.Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        RunnerDifficultyOverride.Clear();
        if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true);
    }

    // A config whose sub-configs are private copies (tests change them without touching the assets).
    private static MusicRunnerGameplayConfig Clone(MusicRunnerGameplayConfig src)
    {
        var c = Object.Instantiate(src);
        c.core = Object.Instantiate(src.core);
        c.collectibles = Object.Instantiate(src.collectibles);
        c.levelGeneration = Object.Instantiate(src.levelGeneration);
        return c;
    }

    private GameplayTimeline Gen(float difficulty, string position = "L1#1") =>
        GameplayTimeline.Generate(_profile, _config, _path, BonusLayoutProvider.KeyFor(_profile, _config, difficulty, position, 0).Seed, difficulty);

    private static byte[] Bytes(GameplayTimeline t) =>
        FileBonusLayoutStore.Write(new BonusLayoutRecord { events = t.Events, stats = t.Stats, key = BonusLayoutKey.Create(default, "x", 0, 0, 0) });

    private static IEnumerable<TimelineEvent> Musical(GameplayTimeline t) =>
        t.Events.Where(e => e.origin == BonusOrigin.Detected || e.origin == BonusOrigin.SyntheticGrid);

    [Test]
    public void SameCombinationIsByteIdenticalAndOtherCombinationsVary()
    {
        CollectionAssert.AreEqual(Bytes(Gen(0.4f)), Bytes(Gen(0.4f)), "same key ⇒ same layout");
        var a = Gen(0.4f, "L1#1");
        var b = Gen(0.4f, "L1#2");
        CollectionAssert.AreEqual(a.Events.Where(e => e.origin == BonusOrigin.Detected).Select(e => e.eventTime).ToArray(),
                                  b.Events.Where(e => e.origin == BonusOrigin.Detected).Select(e => e.eventTime).ToArray(),
                                  "the SELECTION depends on the music + difficulty only (deterministic)");
        CollectionAssert.AreNotEqual(Bytes(a), Bytes(b), "another position ⇒ another seed ⇒ other heights / offsets / patterns");
        Assert.AreNotEqual(BonusLayoutProvider.KeyFor(_profile, _config, 0.4f, "L1#1", 0).Seed,
                           BonusLayoutProvider.KeyFor(_profile, _config, 0.4f, "L1#2", 0).Seed);
    }

    [Test]
    public void GeneratedOnceThenLoadedAndRebuiltIdenticallyWhenMissingOrCorrupt()
    {
        var store = new FileBonusLayoutStore(_tmp);
        var provider = new BonusLayoutProvider(store);
        var first = provider.GetOrCreate(_profile, _config, _path, 0.5f, "L2#3", 0);
        Assert.AreEqual(BonusLayoutOutcome.Generated, first.Outcome);
        byte[] reference = FileBonusLayoutStore.Write(first.Record);

        var second = provider.GetOrCreate(_profile, _config, _path, 0.5f, "L2#3", 0);
        Assert.AreEqual(BonusLayoutOutcome.Loaded, second.Outcome, "a repeat loads, never regenerates");
        CollectionAssert.AreEqual(reference, FileBonusLayoutStore.Write(second.Record));
        CollectionAssert.AreEqual(first.Timeline.Events.Select(e => e.verticalOffset).ToArray(), second.Timeline.Events.Select(e => e.verticalOffset).ToArray());

        string file = store.PathFor(first.Record.key);
        File.Delete(file);
        var rebuilt = provider.GetOrCreate(_profile, _config, _path, 0.5f, "L2#3", 0);
        Assert.AreEqual(BonusLayoutOutcome.Generated, rebuilt.Outcome);
        CollectionAssert.AreEqual(Bytes(first.Timeline), Bytes(rebuilt.Timeline), "missing file ⇒ the same seed + data + config + generator rebuild the same layout");

        var corrupt = File.ReadAllBytes(file);
        corrupt[corrupt.Length / 2] ^= 0x5A;
        File.WriteAllBytes(file, corrupt);
        var repaired = provider.GetOrCreate(_profile, _config, _path, 0.5f, "L2#3", 0);
        Assert.AreEqual(BonusLayoutOutcome.Rebuilt, repaired.Outcome, "a corrupt file is never used");
        CollectionAssert.AreEqual(Bytes(first.Timeline), Bytes(repaired.Timeline));
    }

    [Test]
    public void KeyFollowsRevisionsAndConfigButNotAttemptsOrDiagnostics()
    {
        var k = BonusLayoutProvider.KeyFor(_profile, _config, 0.5f, "L2#3", 0);
        Assert.AreEqual(RunnerPickupLayout.GeneratorVersion, k.generatorVersion);
        Assert.AreEqual(_profile.profileCrc, k.profile.profileCrc);
        Assert.AreNotEqual(0u, k.profile.profileCrc, "published profile carries its body CRC");

        ulong h0 = BonusLayoutProvider.LayoutConfigHash(_config);
        _config.collectibles.diagnostics.clickMode = BonusClickMode.SelectedBonuses;
        Assert.AreEqual(h0, BonusLayoutProvider.LayoutConfigHash(_config), "listening diagnostics never change the layout");
        _config.collectibles.selection.minInterval += 0.05f;
        Assert.AreNotEqual(h0, BonusLayoutProvider.LayoutConfigHash(_config), "a selection tuning change ⇒ another key");
        Assert.AreEqual(h0, BonusLayoutProvider.LayoutConfigHash(Clone(AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(ConfigPath))),
                        "hash stable across instances (no instance ids)");

        Assert.IsNull(typeof(BonusLayoutKey).GetField("attempt"));
        Assert.AreEqual(k.ToString(), BonusLayoutProvider.KeyFor(_profile, Clone(AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(ConfigPath)), 0.5f, "L2#3", 0).ToString());
    }

    [Test]
    public void StaleProfileRevisionLayoutsArePruned()
    {
        var store = new FileBonusLayoutStore(_tmp);
        var provider = new BonusLayoutProvider(store);
        provider.GetOrCreate(_profile, _config, _path, 0.3f, "L1#1", 0);
        provider.GetOrCreate(_profile, _config, _path, 0.4f, "L1#2", 0);
        Assert.AreEqual(2, Directory.GetFiles(_tmp).Length);
        _profile.profileCrc ^= 1u; // a content update of this song's profile
        var r = provider.GetOrCreate(_profile, _config, _path, 0.3f, "L1#1", 0);
        Assert.AreEqual(BonusLayoutOutcome.Generated, r.Outcome, "a new profile revision is a new combination");
        Assert.AreEqual(1, Directory.GetFiles(_tmp).Length, "the old revision's layouts are removed, the new one kept");
    }

    [Test]
    public void DifficultyRisesPerLevelAndPerMatch()
    {
        var campaign = ScriptableObject.CreateInstance<CampaignConfigSO>();
        float prev = -1f;
        foreach (var level in campaign.levels)
            for (int m = 1; m <= level.matchCount; m++)
            {
                float d = campaign.RunnerDifficultyAt(level.id, m, 1);
                Assert.Greater(d, prev, $"{level.id}#{m}");
                prev = d;
            }
        Assert.AreEqual(campaign.RunnerDifficultyAt("L2", 3, 1), campaign.RunnerDifficultyAt("L2", 3, 2), "lap step 0 ⇒ laps share the difficulty (lap not in the key)");
        campaign.lapDifficultyStep = 0.1f;
        Assert.Greater(campaign.RunnerDifficultyAt("L2", 3, 2), campaign.RunnerDifficultyAt("L2", 3, 1));

        // Nested: every moment of an easier layout is also in a harder one (same song, same ranking).
        var easier = new HashSet<int>(Gen(0.3f).Events.Where(e => e.origin == BonusOrigin.Detected).Select(e => e.onsetIndex));
        var harder = new HashSet<int>(Gen(0.7f).Events.Where(e => e.origin == BonusOrigin.Detected).Select(e => e.onsetIndex));
        Assert.IsTrue(easier.IsSubsetOf(harder), "a harder layout contains every moment of the easier one");

        int prevCount = -1; float prevElevated = -1f;
        foreach (float d in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            var t = Gen(d);
            int count = t.Events.Count(e => e.origin == BonusOrigin.Detected);
            Assert.GreaterOrEqual(count, prevCount, $"difficulty {d}: harder keeps at least as many moments");
            prevCount = count;
            float elevated = _config.collectibles.HeightsAt(d).elevatedChance;
            Assert.GreaterOrEqual(elevated, prevElevated);
            prevElevated = elevated;
            var times = t.Events.Where(e => e.origin == BonusOrigin.Detected).Select(e => e.eventTime).OrderBy(x => x).ToArray();
            float minGap = times.Zip(times.Skip(1), (a, b) => b - a).DefaultIfEmpty(9f).Min();
            Assert.GreaterOrEqual(minGap, _config.collectibles.selection.MinInterval(d) - 1e-4f, $"difficulty {d}: spacing");
            TestContext.WriteLine($"d={d:F2}: {t.Stats}");
        }
        // Every match of a level gets MORE of the song's moments than the previous one (smooth density quota).
        foreach (var level in campaign.levels)
        {
            int last = -1;
            for (int m = 1; m <= level.matchCount; m++)
            {
                int n = Gen(campaign.RunnerDifficultyAt(level.id, m, 1)).Events.Count(e => e.origin == BonusOrigin.Detected);
                Assert.Greater(n, last, $"{level.id}#{m}: more bonuses than the previous match");
                TestContext.WriteLine($"{level.id}#{m} d={campaign.RunnerDifficultyAt(level.id, m, 1):F3}: {n} bonuses");
                last = n;
            }
        }
        Assert.AreEqual(_config.collectibles.normalHeights.elevatedChance, _config.collectibles.HeightsAt(0.3f).elevatedChance, 1e-6f, "Normal anchor kept");
        Assert.AreEqual(_config.collectibles.hardHeights.offTrackChance, _config.collectibles.HeightsAt(0.6f).offTrackChance, 1e-6f, "Hard anchor kept");
    }

    [Test]
    public void DevOverridesAreOffByDefaultAndOnlyActWhenEnabled()
    {
        var a = _config.collectibles.devLayoutOverrides;
        Assert.IsFalse(a.enabled, "dev layout overrides are off in the project config");
        Assert.IsNull(_config.collectibles.DevOverrides);
        ulong normalHash = BonusLayoutProvider.LayoutConfigHash(_config);
        int normalCount = Gen(1f).Events.Length;
        a.minBonusSpacing = 0.6f; a.noJumps = true; a.noOffTrack = true; a.maxDifficulty = 0.2f;
        Assert.AreEqual(normalHash, BonusLayoutProvider.LayoutConfigHash(_config), "inactive overrides never change the normal key");
        Assert.AreEqual(normalCount, Gen(1f).Events.Length, "inactive overrides never change the layout");
        a.maxDifficulty = 1f;
        a.enabled = true; // editor tool
        Assert.AreNotEqual(normalHash, BonusLayoutProvider.LayoutConfigHash(_config), "an active dev override is its own key");
        var t = Gen(1f);
        var times = Musical(t).Select(e => e.eventTime).OrderBy(x => x).ToArray();
        Assert.GreaterOrEqual(times.Zip(times.Skip(1), (x, y) => y - x).Min(), 0.6f - 1e-4f, "dev spacing override");
        Assert.IsFalse(t.Events.Any(e => e.isOffTrack), "no off-track bonus");
        foreach (var e in t.Events)
        {
            var (bottom, _) = GameplayTimeline.ElevatedBand(e.ringType, _config);
            Assert.Less(e.verticalOffset, bottom, $"{e.ringType} at {e.eventTime:F2} needs a jump");
            Assert.LessOrEqual(Mathf.Abs(e.lateralOffset), _path.GetWidth(e.eventDistance) * 0.5f + 1e-3f, $"{e.ringType} at {e.eventTime:F2} is off the track");
        }
        a.maxDifficulty = 0.2f;
        Assert.AreEqual(Gen(0.2f).Events.Length, Gen(1f).Events.Length, "difficulty capped (dev tool)");
    }

    [Test]
    public void OneObjectPerMomentAndExactSync()
    {
        var t = Gen(1f);
        float warmup = _config.core.warmupTime;
        var detected = t.Events.Where(e => e.origin == BonusOrigin.Detected).ToList();
        Assert.Greater(detected.Count, 0);
        Assert.AreEqual(detected.Count, detected.Select(e => e.onsetIndex).Distinct().Count(), "a detection never becomes two objects");
        int multi = 0;
        foreach (var e in detected)
        {
            var on = _profile.onsetEvents[e.onsetIndex];
            Assert.AreEqual(MusicTime.OnsetToEventTime(on.time, warmup), e.eventTime, 1e-5f, "bonus at warmup + its attack (no offset)");
            if (e.contributors.Contains("+")) multi++;
        }
        var impacts = t.Events.Where(e => e.ringType == RingType.Impact).Select(e => e.eventTime).ToList();
        float merge = _config.collectibles.selection.impactMergeWindow;
        Assert.IsFalse(detected.Any(e => impacts.Any(i => Mathf.Abs(i - e.eventTime) <= merge)), "a moment represented by an Impact is not spawned twice");
        TestContext.WriteLine($"{detected.Count} detected bonuses, {multi} carry several families as ONE object; {t.Stats}");
    }

    [Test]
    public void SyntheticFillCanBeDisabledAndStaysIdentifiableAndOnTrackedBeats()
    {
        // Exercise both modes explicitly; the authored default can be tuned independently.
        _config.collectibles.selection.syntheticFill = false;
        Assert.AreEqual(0, Gen(0.5f).Events.Count(e => e.origin == BonusOrigin.SyntheticGrid));
        _config.collectibles.selection.syntheticFill = true;
        var t = Gen(0.5f);
        var syn = t.Events.Where(e => e.origin == BonusOrigin.SyntheticGrid).ToList();
        Assert.AreEqual(t.Stats.synthetic, syn.Count);
        float warmup = _config.core.warmupTime, gap = Mathf.Max(_config.collectibles.selection.syntheticMinGap, _config.collectibles.selection.MinInterval(0.5f));
        var detectedTimes = t.Events.Where(e => e.origin == BonusOrigin.Detected).Select(e => e.eventTime).ToList();
        foreach (var e in syn)
        {
            Assert.AreEqual(RingType.Beat, e.ringType);
            Assert.AreEqual("Grid", e.sourceFeature);
            Assert.AreEqual(-1, e.onsetIndex);
            Assert.IsTrue(_profile.beatTimes.Any(b => Mathf.Abs(MusicTime.OnsetToEventTime(b, warmup) - e.eventTime) < 1e-5f), "on a tracked beat");
            Assert.IsFalse(detectedTimes.Any(d => Mathf.Abs(d - e.eventTime) < gap - 1e-4f), "only in real gaps");
        }
        TestContext.WriteLine($"synthetic fill: {syn.Count} beats over {t.Stats.selected} detected bonuses");
    }

    [Test]
    public void LegacyLayoutGetsUsageSummaryOnceWithoutRegeneratingEvents()
    {
        var store = new FileBonusLayoutStore(_tmp);
        var provider = new BonusLayoutProvider(store);
        var original = provider.GetOrCreate(_profile, _config, _path, .5f, "L2#3", 0);
        File.WriteAllBytes(store.PathFor(original.Record.key), BonusUsageSummaryTests.LegacyBytes(original.Record));
        var loaded = provider.GetOrCreate(_profile, _config, _path, .5f, "L2#3", 0);
        Assert.AreEqual(BonusLayoutOutcome.Loaded, loaded.Outcome);
        Assert.AreEqual(original.Record.generatedAtUnixSeconds, loaded.Record.generatedAtUnixSeconds);
        Assert.AreEqual(original.Record.seed, loaded.Record.seed);
        CollectionAssert.AreEqual(original.Record.events.Select(e => e.eventTime), loaded.Record.events.Select(e => e.eventTime));
        var expected = BonusTypeUsage.Summarize(original.Record.events);
        CollectionAssert.AreEqual(expected.Select(u => u.type), loaded.Record.bonusTypes.Select(u => u.type));
        CollectionAssert.AreEqual(expected.Select(u => u.count), loaded.Record.bonusTypes.Select(u => u.count));
        byte[] upgraded = File.ReadAllBytes(store.PathFor(original.Record.key));
        Assert.AreEqual(2, System.BitConverter.ToInt32(upgraded, 4));
        provider.GetOrCreate(_profile, _config, _path, .5f, "L2#3", 0);
        CollectionAssert.AreEqual(upgraded, File.ReadAllBytes(store.PathFor(original.Record.key)), "already upgraded files are not rewritten");
    }
}
#endif
