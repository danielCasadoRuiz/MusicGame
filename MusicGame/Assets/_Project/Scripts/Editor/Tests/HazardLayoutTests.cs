#if UNITY_EDITOR
using System.IO;
using System.Linq;
using MusicGame.Audio.Analysis;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode checks of the first hazards version on the real published song_008 + the project config:
/// deterministic placement, gentle → harder progression, nothing on top of a bonus, passages that the
/// player's REAL movement can use (sizes vs. capsule / crouch / jump / air tuck of MusicRunnerCoreConfig),
/// test options off, and the bonus layout key untouched by the hazards config.
/// Run: -runTests -testPlatform EditMode -testFilter HazardLayoutTests
/// </summary>
public class HazardLayoutTests
{
    private const string ConfigPath = "Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset";
    private MusicRunnerGameplayConfig _config;
    private SongProfile _profile;
    private MusicPath _path;

    [SetUp]
    public void SetUp()
    {
        _config = AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(ConfigPath);
        Assert.IsNotNull(_config?.hazards, "root config has a RunnerHazardsConfig");
        var app = Resources.Load<AppConfigSO>("AppConfig");
        _profile = SongAnalysisPipeline.LoadProfile(File.ReadAllBytes(SongContentKeys.ProfileAssetPath("song_008")), new DerivedAnalysisSettings(app.audioAnalysis), out _);
        _path = new SnakeWayWorldGenerator().Generate(_profile, _config, new Vector3(0f, _config.levelGeneration.pathBaseHeight, 0f));
    }

    private (System.Collections.Generic.List<HazardPlacement> list, GameplayTimeline timeline) Gen(float d, string pos = "L1#1")
    {
        int seed = BonusLayoutProvider.KeyFor(_profile, _config, d, pos, 0).Seed;
        var tl = GameplayTimeline.Generate(_profile, _config, _path, seed, d);
        float w = _config.core.warmupTime;
        return (HazardLayout.Generate(_config.hazards, tl.Events, _path, seed, d, w, w + 60f, _config.core.playerSpeed), tl);
    }

    [Test]
    public void TestOptionsAreOffByDefault()
    {
        Assert.IsFalse(_config.hazards.testGallery);
        Assert.IsFalse(_config.hazards.testHotkeys);
    }

    [Test]
    public void SameCombinationSamePlacement()
    {
        var a = Gen(0.4f).list; var b = Gen(0.4f).list;
        Assert.AreEqual(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.AreEqual(a[i].distance, b[i].distance);
            Assert.AreEqual(a[i].kind, b[i].kind);
            Assert.AreEqual(a[i].lateral, b[i].lateral);
            Assert.AreEqual(a[i].isChaser, b[i].isChaser);
        }
    }

    [Test]
    public void FewAndGentleEarlyMoreAndHarderLater()
    {
        var easy = Gen(0.15f).list; var hard = Gen(0.82f, "L4#6").list;
        int eo = easy.Count(p => !p.isChaser), ho = hard.Count(p => !p.isChaser);
        TestContext.WriteLine($"L1#1: {eo} obstacles {easy.Count(p => p.isChaser)} chasers | L4#6: {ho} obstacles {hard.Count(p => p.isChaser)} chasers");
        Assert.Greater(eo, 0, "some obstacles in the first match");
        Assert.Greater(ho, eo, "more obstacles later");
        var early = new[] { RunnerObstacleKind.Portal, RunnerObstacleKind.CrawlWall, RunnerObstacleKind.WindowWall };
        Assert.IsFalse(easy.Any(p => !p.isChaser && early.Contains(p.kind)), "no crouch / window obstacle in the first match");
        Assert.IsTrue(hard.Any(p => !p.isChaser && early.Contains(p.kind)), "demanding kinds appear later");
    }

    [Test]
    public void NoBonusInsideAndEveryObstacleHasAPassage()
    {
        foreach (float d in new[] { 0.15f, 0.5f, 0.82f })
        {
            var (list, tl) = Gen(d, "Lx");
            float chaseEnd = float.MinValue;
            foreach (var p in list)
            {
                if (p.isChaser) { chaseEnd = p.songTime + RunnerHazardsConfig.Lerp(_config.hazards.easyChaserDuration, _config.hazards.hardChaserDuration, d); continue; }
                var prefab = _config.hazards.PrefabFor(p.kind);
                Assert.IsTrue(HazardLayout.FreeOfBonuses(tl.Events, p.distance, prefab.Length * 0.5f + _config.hazards.bonusClearance,
                              HazardLayout.BlockFootprint(prefab, p.trackWidth, p.lateral)), $"bonus near {p.kind} @{p.distance:F1}");
                Assert.GreaterOrEqual(p.trackWidth, prefab.RequiredTrackWidth, $"{p.kind} too narrow");
                float half = p.trackWidth * 0.5f, feat = prefab.HalfFeatureWidth(p.trackWidth);
                if (p.kind == RunnerObstacleKind.LowBlock || p.kind == RunnerObstacleKind.HighBlock)
                    Assert.GreaterOrEqual(Mathf.Max(half - (p.lateral + feat), (p.lateral - feat) + half), prefab.minPassage - 1e-3f, "a free side next to the block");
                else if (p.kind != RunnerObstacleKind.Portal)
                    Assert.LessOrEqual(Mathf.Abs(p.lateral) + feat, half + 1e-3f, $"{p.kind} opening on the track");
                Assert.Greater(p.songTime, chaseEnd, "no obstacle during a chase");
            }
        }
    }

    // The sizes against the player's real capabilities (capsule, crouch, jump apex, air tuck).
    [Test]
    public void OpeningsMatchThePlayer()
    {
        var c = _config.core;
        float stand = PlayerController.CapsuleHeight, crouch = Mathf.Clamp(c.crouchHeight, 0.6f, stand);
        float apex = c.jumpForce * c.jumpForce / (2f * -c.gravity);
        float lift = (stand - crouch) * (1f - Mathf.Clamp01(c.airTuckHeadDrop));
        var h = _config.hazards;
        Assert.Less(h.lowBlockPrefab.lowHeight, apex - 0.5f, "low block: jumpable with margin");
        Assert.Greater(h.lowBlockPrefab.lowHeight, 0.35f, "low block: not walkable (step offset 0.3)");
        Assert.Greater(h.highBlockPrefab.highHeight, apex + 0.3f, "high block: not jumpable");
        Assert.Greater(h.portalPrefab.portalClearance, crouch + 0.2f, "portal: crouched fits with tolerance");
        Assert.Less(h.portalPrefab.portalClearance, stand, "portal: standing does not fit");
        Assert.Greater(h.portalPrefab.portalClearance + h.portalPrefab.beamThickness, apex, "portal: no jumping over the beam");
        Assert.Greater(h.crawlWallPrefab.crawlHeight, crouch + 0.2f, "crawl wall: crouched fits");
        Assert.Less(h.crawlWallPrefab.crawlHeight, stand, "crawl wall: standing does not fit");
        Assert.Greater(h.crawlWallPrefab.crawlWidth, 0.9f + 0.6f, "crawl wall: wide enough");
        var w = h.windowWallPrefab;
        Assert.Less(w.windowHeight, stand, "window: a standing body never fits");
        Assert.Greater(w.windowBottom, 0.6f, "window: needs a jump");
        // Tucked body: feet = arc + lift, top = arc + lift + crouch. Time with the feet above the bottom and the top below the top.
        float t0 = float.NaN, t1 = float.NaN;
        for (float t = 0f; t < 2f * c.jumpForce / -c.gravity; t += 0.002f)
        {
            float arc = c.jumpForce * t + 0.5f * c.gravity * t * t;
            bool fits = arc + lift >= w.windowBottom + 0.02f && arc + lift + crouch <= w.windowBottom + w.windowHeight - 0.02f;
            if (fits && float.IsNaN(t0)) t0 = t;
            if (fits) t1 = t;
        }
        float cross = (w.depth + 0.9f) / c.playerSpeed;
        TestContext.WriteLine($"window passable while tucked for {(t1 - t0):F2} s (crossing takes {cross:F2} s)");
        Assert.IsFalse(float.IsNaN(t0), "window: reachable jumping + crouching");
        Assert.Greater(t1 - t0, cross * 2f, "window: a comfortable timing window");
        Assert.Greater(h.uShapePrefab.corridorWidth, 0.9f + 1f, "U: corridor wide enough");
    }

    [Test]
    public void BonusLayoutKeyIgnoresHazards()
    {
        ulong before = BonusLayoutProvider.LayoutConfigHash(_config);
        float g = _config.hazards.easyObstacleGapSeconds;
        try { _config.hazards.easyObstacleGapSeconds = g + 3f; Assert.AreEqual(before, BonusLayoutProvider.LayoutConfigHash(_config)); }
        finally { _config.hazards.easyObstacleGapSeconds = g; }
    }
}
#endif
