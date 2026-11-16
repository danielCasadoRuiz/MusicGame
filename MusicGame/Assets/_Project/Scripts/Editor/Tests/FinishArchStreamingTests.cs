#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class FinishArchStreamingTests
{
    private GameObject _host, _prefab;
    private MusicRunnerGameplayConfig _config;
    private MusicRunnerCoreConfig _core;
    private MusicWorldManager _world;
    private float _archDistance;

    [SetUp]
    public void SetUp()
    {
        _host = new GameObject("FinishArchStreamingTest");
        _host.SetActive(false);
        _world = _host.AddComponent<MusicWorldManager>();
        _prefab = new GameObject("FinishArchTemplate");
        _core = ScriptableObject.CreateInstance<MusicRunnerCoreConfig>();
        _core.playerSpeed = 10f;
        _core.warmupTime = 3f;
        _core.endingFlatSeconds = 3f;
        _core.finishArchPrefab = _prefab;
        _config = ScriptableObject.CreateInstance<MusicRunnerGameplayConfig>();
        _config.core = _core;
        _world.Initialize(_config);
        const float playEnd = 120f;
        _archDistance = MusicTime.SongToDistance(
            MusicTime.ClipToSong(playEnd, _core.warmupTime), _core.playerSpeed);
        typeof(MusicWorldManager).GetField("_playRangeEnd", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(_world, playEnd);
        typeof(MusicWorldManager).GetProperty("Path").SetValue(_world, new MusicPath(new[]
        {
            new MusicPath.Sample(Vector3.zero, Vector3.forward, 0f, 8f),
            new MusicPath.Sample(Vector3.forward * 2000f, Vector3.forward, 2000f, 8f),
        }));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_host);
        Object.DestroyImmediate(_prefab);
        Object.DestroyImmediate(_config);
        Object.DestroyImmediate(_core);
    }

    [Test]
    public void ClosingHillsStartAtTheArchWithoutTheLegacyFlatHold()
    {
        _core.endingWaves = true;
        _core.endingFlatSeconds = 9f; // Old serialized values must not add a flat stretch.
        Assert.That(_world.NormalizedBandValue(0,120f),Is.EqualTo(0f).Within(.0001f));
        Assert.That(_world.NormalizedBandValue(0,120.01f),Is.GreaterThan(0f));
        Assert.That(_world.NormalizedBandValue(0,120.5f),Is.EqualTo(.5f).Within(.0001f));
        Assert.That(_world.NormalizedBandValue(3,121f),Is.EqualTo(0f).Within(.0001f));
        Assert.That(_world.NormalizedBandValue(3,121.5f),Is.EqualTo(.5f).Within(.0001f));
    }

    private void StreamWindow(float start, float end) => typeof(MusicWorldManager)
        .GetMethod("PlaceFinishArch", BindingFlags.NonPublic | BindingFlags.Instance)
        .Invoke(_world, new object[] { start, end });

    [Test]
    public void ArchAppearsWithItsGroundWhileSongIsStillAheadOfTheClosingSection()
    {
        // Player is 60 m before the arch, and 60 m before the playback-end boundary.
        // A loaded window is sufficient: no clock/end-of-song event may gate this decoration.
        StreamWindow(_archDistance - 80f, _archDistance);
        var arch = _host.transform.Find("FinishArch");
        Assert.IsNotNull(arch, "The arch must appear with its ground, before the closing section starts.");
        Assert.AreEqual(_archDistance, arch.position.z, 0.001f);
        StreamWindow(_archDistance - 64f, _archDistance + 16f);
        Assert.AreEqual(1, _host.transform.childCount, "Later windows must not duplicate the arch.");
    }

    [Test]
    public void RestartWindowRemovesOldArchAndRecreatesItOnlyNearFinish()
    {
        StreamWindow(_archDistance - 80f, _archDistance + 10f);
        var previous = _host.transform.Find("FinishArch");
        Assert.IsNotNull(previous);
        int oldId = previous.gameObject.GetInstanceID();
        // Same backward window jump that RestartSong -> RebuildNow performs.
        StreamWindow(0, 100);
        Assert.IsTrue(previous == null || !previous.gameObject.activeSelf,
            "The old finish arch must not remain visible after rewinding.");
        Assert.IsNull(_host.transform.Find("FinishArch"));
        StreamWindow(20, 120);
        Assert.IsNull(_host.transform.Find("FinishArch"));
        StreamWindow(_archDistance - 80f, _archDistance + 10f);
        var recreated = _host.transform.Find("FinishArch");
        Assert.IsNotNull(recreated);
        Assert.AreNotEqual(oldId, recreated.gameObject.GetInstanceID());
        Assert.AreEqual(1, _host.transform.childCount);
    }

    [TestCase(-80f, -0.01f)]
    [TestCase(0.01f, 80f)]
    public void ArchDoesNotAppearOutsideTheLoadedGround(float startOffset, float endOffset)
    {
        StreamWindow(_archDistance + startOffset, _archDistance + endOffset);
        Assert.IsNull(_host.transform.Find("FinishArch"));
    }
}
#endif
