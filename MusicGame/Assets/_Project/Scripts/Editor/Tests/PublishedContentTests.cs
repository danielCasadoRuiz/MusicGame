#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicGame.Audio.Analysis;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode tests of the PUBLISHED catalog contract (phase P3): the compatibility rules a runtime load
/// applies to a published profile (PublishedProfileCheck — every mismatch is an explicit reason, never a
/// silent re-analysis), the format-2 playback binding of the baked profiles in the project (byte-identical
/// round trip, identity time conversion, bound to the CURRENT master + import settings), the index revision
/// hash, and the Phase 4 bonus-layout reuse key (no attempt number, lap only when it matters, continuous
/// difficulty quantised). Run: -runTests -testPlatform EditMode -testFilter PublishedContentTests
/// </summary>
public class PublishedContentTests
{
    private static AppAnalysisVersions App => new(ProfileBinary.FormatVersion, AnalysisVersions.Pipeline, 0x1234UL, 1);

    private static (ProfileHeader h, SongCatalogIndexEntry e) Matching()
    {
        var h = new ProfileHeader
        {
            pipelineVersion = AnalysisVersions.Pipeline, paramsHash = 0x1234UL, tagModelVersion = 1, flags = ProfileBinary.FlagTags,
            songId = "song_x", audioIdentity = 0xAAUL, playbackRevision = 0xBBUL, bodyCrc32 = 0xCCu,
            playbackSamples = 1000, playbackChannels = 2, playbackFrequency = 48000,
        };
        var e = new SongCatalogIndexEntry { songId = "song_x", masterIdentity = 0xAAUL, playbackRevision = 0xBBUL, profileCrc = 0xCCu };
        return (h, e);
    }

    [Test]
    public void MatchingHeaderEntryAndAppAreCompatible()
    {
        var (h, e) = Matching();
        Assert.IsNull(PublishedProfileCheck.Validate(h, e, App));
        Assert.IsNull(PublishedProfileCheck.ValidateClip(h, 1000, 2, 48000));
    }

    [Test]
    public void EveryMismatchIsAnExplicitReason()
    {
        var cases = new Dictionary<string, System.Action<ProfileHeader, SongCatalogIndexEntry>>
        {
            ["format"]          = (h, e) => h.formatVersion = 1,
            ["pipeline"]        = (h, e) => h.pipelineVersion++,
            ["params"]          = (h, e) => h.paramsHash = 0x9999UL,
            ["tag model"]       = (h, e) => h.tagModelVersion = 7,
            ["other song"]      = (h, e) => h.songId = "song_y",
            ["unbound"]         = (h, e) => h.playbackRevision = 0UL,
            ["master"]          = (h, e) => e.masterIdentity = 0x1UL,
            ["playback"]        = (h, e) => e.playbackRevision = 0x2UL,
            ["profile body"]    = (h, e) => e.profileCrc = 0x3u,
            ["time conversion"] = (h, e) => { h.timeConversion = ProfileTimeConversion.ConstantOffset; h.playbackOffsetSeconds = 0.025f; },
        };
        foreach (var kv in cases)
        {
            var (h, e) = Matching();
            kv.Value(h, e);
            Assert.IsNotNull(PublishedProfileCheck.Validate(h, e, App), $"'{kv.Key}' mismatch was accepted");
        }
        var (h2, e2) = Matching();
        Assert.IsNotNull(PublishedProfileCheck.Validate(null, e2, App));
        Assert.IsNotNull(PublishedProfileCheck.Validate(h2, null, App));
        Assert.IsNotNull(PublishedProfileCheck.ValidateClip(h2, 1001, 2, 48000), "padding/trim of the played clip");
        Assert.IsNotNull(PublishedProfileCheck.ValidateClip(h2, 1000, 1, 48000), "channel change");
        Assert.IsNotNull(PublishedProfileCheck.ValidateClip(h2, 1000, 2, 44100), "resampling");
    }

    [Test]
    public void PublishedProfilesAreBoundToTheCurrentMasterAndRoundTrip()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        Assert.IsNotNull(app?.songCatalog, "AppConfig.songCatalog missing");
        var versions = AppAnalysisVersions.Current(app.audioAnalysis);
        int checkedSongs = 0;
        foreach (var e in app.songCatalog.songs.Where(s => s != null && !string.IsNullOrEmpty(s.songId)))
        {
            string path = SongContentKeys.ProfileAssetPath(e.songId);
            Assert.IsTrue(File.Exists(path), $"{e.songId}: no published profile (run the bake)");
            string problem = SongContentValidator.CheckProfileAgainstSource(e.songId, AssetDatabase.GUIDToAssetPath(e.editorGuid), path, versions,
                                                                            EditorUserBuildSettings.activeBuildTarget, out var header, out var bytes);
            Assert.IsNull(problem, $"{e.songId}: {problem}");
            Assert.AreEqual(ProfileTimeConversion.Identity, header.timeConversion, $"{e.songId}: conversion");
            Assert.IsTrue(header.HasTags && header.HasPlaybackBinding, $"{e.songId}: tags + binding");
            var raw = SongProfileSerializer.Read(bytes, out var h, out var tags);
            CollectionAssert.AreEqual(bytes, SongProfileSerializer.Write(h, raw, tags), $"{e.songId}: round trip");
            checkedSongs++;
        }
        Assert.Greater(checkedSongs, 0);
    }

    [Test]
    public void CatalogAddressesSurviveTheBinaryCatalog()
    {
        // Measured: Addressables 2.3.1 wrote "VÅÅV - Dancig Queer" as "V??V - Dancig Queer".
        Assert.IsFalse(SongContentKeys.IsSafeAddress("VÅÅV - Dancig Queer"));
        Assert.IsFalse(SongContentKeys.IsSafeAddress("All But Water — Zero Nine"));
        Assert.IsTrue(SongContentKeys.IsSafeAddress(SongContentKeys.AudioAddress("song_018")));
        var index = AssetDatabase.LoadAssetAtPath<SongCatalogIndexSO>(SongContentKeys.IndexAssetPath);
        Assert.IsNotNull(index, "no published index (run the bake)");
        foreach (var e in index.songs)
        {
            Assert.IsTrue(SongContentKeys.IsSafeAddress(e.audioAddress) && SongContentKeys.IsSafeAddress(e.profileAddress), $"{e.songId}: '{e.audioAddress}' / '{e.profileAddress}'");
            Assert.IsFalse(string.IsNullOrEmpty(e.displayName), $"{e.songId}: no display name");
        }
    }

    [Test]
    public void IndexRevisionFollowsEveryEntryRevision()
    {
        var a = new List<SongCatalogIndexEntry> { new() { songId = "s1", audioAddress = "A", masterIdentity = 1, playbackRevision = 2, profileCrc = 3, order = 1 } };
        string r0 = SongCatalogBake.ContentRevision(a);
        a[0].profileCrc = 4;
        Assert.AreNotEqual(r0, SongCatalogBake.ContentRevision(a), "a profile-only change must change the index revision");
        a[0].profileCrc = 3;
        Assert.AreEqual(r0, SongCatalogBake.ContentRevision(a));
        a.Add(new SongCatalogIndexEntry { songId = "s2", audioAddress = "B", masterIdentity = 5, playbackRevision = 6, profileCrc = 7, order = 2 });
        Assert.AreNotEqual(r0, SongCatalogBake.ContentRevision(a), "an added song must change the index revision");
    }

    [Test]
    public void BonusLayoutKeyContainsWhatChangesTheResultOnly()
    {
        var rev = new ProfileRevision { songId = "s1", masterIdentity = 1, playbackRevision = 2, profileCrc = 3, pipelineVersion = 3, paramsHash = 4 };
        var k = BonusLayoutKey.Create(rev, "L2#3", 0.4f, 1, 0xABCUL);
        Assert.AreEqual(k, BonusLayoutKey.Create(rev, "L2#3", 0.4000001f, 1, 0xABCUL), "float noise must not fork keys");
        Assert.AreEqual(k.ToString(), BonusLayoutKey.Create(rev, "L2#3", 0.4f, 1, 0xABCUL).ToString());
        Assert.AreNotEqual(k, BonusLayoutKey.Create(rev, "L2#3", 0.45f, 1, 0xABCUL), "difficulty is continuous, not 4 variants");
        Assert.AreNotEqual(k, BonusLayoutKey.Create(rev, "L2#4", 0.4f, 1, 0xABCUL), "another match of the level");
        Assert.AreNotEqual(k, BonusLayoutKey.Create(rev, "L2#3", 0.4f, 2, 0xABCUL), "generator version");
        Assert.AreNotEqual(k, BonusLayoutKey.Create(rev, "L2#3", 0.4f, 1, 0xABDUL), "geometry / movement");
        var rev2 = rev; rev2.profileCrc = 9;
        Assert.AreNotEqual(k, BonusLayoutKey.Create(rev2, "L2#3", 0.4f, 1, 0xABCUL), "profile revision");
        Assert.AreEqual(k, BonusLayoutKey.Create(rev, "L2#3", 0.4f, 1, 0xABCUL, lapIfItChangesDifficulty: 0), "lap omitted unless it changes difficulty");
        Assert.AreNotEqual(k, BonusLayoutKey.Create(rev, "L2#3", 0.4f, 1, 0xABCUL, lapIfItChangesDifficulty: 2));
        Assert.IsNull(typeof(BonusLayoutKey).GetField("attempt"), "no attempt number in the key");
    }
}
#endif
