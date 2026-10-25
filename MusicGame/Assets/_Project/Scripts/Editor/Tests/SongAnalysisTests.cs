#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using MusicGame.Audio.Analysis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit-mode tests of the integrated song analysis (phase P2B): the main-thread params snapshot,
/// audio identity (no more name + sample count), the binary profile + cache compatibility rules, the
/// raw vs derived contract (no in-place voice smoothing, no double smoothing, the re-tag path keeps
/// raw data raw), the checked PCM capture (GetData failure / silence → explicit error), the first
/// full window, BPM without evidence, sequential vs parallel equivalence inside Unity (Mono) and
/// blocking cancellation. Synthetic audio only; real songs are covered by AnalysisBenchmarkProbe.
/// Run: Unity -batchmode -projectPath &lt;proj&gt; -runTests -testPlatform EditMode
///      -testFilter SongAnalysisTests -testResults &lt;xml&gt;
/// </summary>
public class SongAnalysisTests
{
    private const int Sr = 48000;

    private static AudioAnalysisConfig Config()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        Assert.IsNotNull(app, "Resources/AppConfig missing");
        Assert.IsNotNull(app.audioAnalysis, "AppConfig.audioAnalysis missing");
        return app.audioAnalysis;
    }

    /// <summary>Kick-like bursts every 0.5 s (120 BPM) + hat-like noise on the off-beats.</summary>
    private static float[] Drums(double seconds, int seed = 1, float gain = 0.6f)
    {
        var rng = new System.Random(seed);
        int n = (int)(seconds * Sr);
        var x = new float[n];
        for (double t = 0.25; t < seconds - 0.3; t += 0.5)
        {
            int s0 = (int)(t * Sr);
            for (int i = 0; i < (int)(0.15 * Sr) && s0 + i < n; i++)
            {
                double tt = (double)i / Sr;
                x[s0 + i] += gain * (float)(Math.Sin(2 * Math.PI * (60 + 90 * Math.Exp(-tt * 40)) * tt) * Math.Exp(-tt * 25));
            }
            int h0 = (int)((t + 0.25) * Sr);
            for (int i = 0; i < (int)(0.04 * Sr) && h0 + i < n; i++)
                x[h0 + i] += 0.2f * gain * (float)((rng.NextDouble() * 2 - 1) * Math.Exp(-(double)i / Sr * 120));
        }
        return x;
    }

    private static SongAudioIdentity Id(string key, float[] mono) => new(SongAudioIdentityKind.DecodedPcm, key, StableHash.HashPcm(mono, 1, Sr));

    // ── Snapshot / params ─────────────────────────────────────────────────────────────────────

    [Test]
    public void Snapshot_MapsConfig_AndHashTracksResultAffectingSwitches()
    {
        var cfg = Config();
        var s = SongAnalysisSnapshot.ToSettings(cfg);
        Assert.AreEqual(cfg.bands.Length, s.classificationBands.Length);
        Assert.AreEqual(cfg.visualBandCount, s.visualBandCount);
        Assert.AreEqual(cfg.advancedEnabled && cfg.advancedHarmony, s.computeChroma);
        var p1 = AnalysisParams.Create(s);
        var s2 = SongAnalysisSnapshot.ToSettings(cfg);
        s2.computeChroma = !s2.computeChroma;
        Assert.AreNotEqual(p1.Hash, AnalysisParams.Create(s2).Hash, "a result-affecting switch must change the params hash");
        Assert.AreEqual(p1.Hash, AnalysisParams.Create(SongAnalysisSnapshot.ToSettings(cfg)).Hash, "hash must be stable");
    }

    // ── Identity ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Identity_SameLengthDifferentAudio_Differs()
    {
        // The old cache key (clip name + sample count) treated these two as the same song.
        var a = Drums(4, seed: 1);
        var b = Drums(4, seed: 2);
        Assert.AreEqual(a.Length, b.Length);
        Assert.AreNotEqual(Id("x", a).Hash, Id("x", b).Hash);
        Assert.AreEqual(Id("x", a).Hash, Id("x", (float[])a.Clone()).Hash);

        string dir = Path.Combine(Path.GetTempPath(), "mg_identity_test");
        Directory.CreateDirectory(dir);
        string f1 = Path.Combine(dir, "song.wav"), f2 = Path.Combine(dir, "song2.wav");
        try
        {
            File.WriteAllBytes(f1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            File.WriteAllBytes(f2, new byte[] { 1, 2, 3, 4, 5, 6, 7, 9 });
            var i1 = new SongAudioIdentitySource { Kind = SongAudioIdentityKind.ImportedFile, SongKey = "import", FilePath = f1 }.Resolve();
            var i2 = new SongAudioIdentitySource { Kind = SongAudioIdentityKind.ImportedFile, SongKey = "import", FilePath = f2 }.Resolve();
            Assert.AreNotEqual(i1.Hash, i2.Hash);
            Assert.AreEqual($"import_{i1.Hash:x16}", i1.SongKey);
        }
        finally { File.Delete(f1); File.Delete(f2); Directory.Delete(dir); }
    }

    // ── Raw vs derived ────────────────────────────────────────────────────────────────────────

    [Test]
    public void Derived_NeverMutatesRaw_AndDoesNotCompound()
    {
        var cfg = Config();
        var mono = Drums(6);
        var snap = SongAnalysisSnapshot.Create(cfg, workerBudget: 2);
        var raw = MusicAnalyzer.Analyze(mono, Sr, snap.Params);
        var voiceBefore = (float[])raw.voice.Clone();
        var p = SongProfileFactory.Create(raw, null, 0, Id("t", mono), snap.Params.Hash, snap.Derived);
        Assert.AreSame(raw.voice, p.voiceProbability, "the profile holds the raw array itself");
        CollectionAssert.AreEqual(voiceBefore, p.voiceProbability, "raw voice must stay raw");
        Assert.IsNotNull(p.voiceProbabilitySmoothed);
        var smoothedOnce = (float[])p.voiceProbabilitySmoothed.Clone();
        SongProfileDerived.Compute(p, snap.Derived); // e.g. a second load of the same profile
        CollectionAssert.AreEqual(smoothedOnce, p.voiceProbabilitySmoothed, "derived data must not compound (double smoothing)");
        CollectionAssert.AreEqual(voiceBefore, p.voiceProbability);
    }

    [Test]
    public void Binary_RoundTrip_IsLossless_AndHeaderBindsAudioAndParams()
    {
        var cfg = Config();
        var mono = Drums(5);
        var snap = SongAnalysisSnapshot.Create(cfg, workerBudget: 2);
        var raw = MusicAnalyzer.Analyze(mono, Sr, snap.Params);
        var id = Id("song_test", mono);
        var tags = new[] { new MusicTagScore("dance", 0.7f) };
        var h = SongProfileSerializer.MakeHeader(id, snap, mono.Length, 2, Sr, hasTags: true);
        var bytes = SongProfileSerializer.Write(h, raw, tags);
        var back = SongProfileSerializer.Read(bytes, out var h2, out var tags2);
        CollectionAssert.AreEqual(raw.energy, back.energy);
        CollectionAssert.AreEqual(raw.voice, back.voice);
        Assert.AreEqual(raw.onsets.Length, back.onsets.Length);
        Assert.AreEqual("dance", tags2[0].tag);
        Assert.IsTrue(SongProfileSerializer.RhythmCompatible(h2, snap, id, mono.Length, Sr));
        Assert.IsFalse(SongProfileSerializer.RhythmCompatible(h2, snap, Id("song_test", Drums(5, seed: 9)), mono.Length, Sr), "other audio");
        var s2 = SongAnalysisSnapshot.ToSettings(cfg); s2.visualBandCount += 1;
        Assert.AreNotEqual(snap.Params.Hash, AnalysisParams.Create(s2).Hash);
        Assert.AreEqual(snap.Params.Hash, h2.paramsHash);
        Assert.AreEqual(AnalysisVersions.Pipeline, h2.pipelineVersion);
        Assert.AreEqual(id.Hash, h2.audioIdentity);
    }

    [Test]
    public void RetagPath_KeepsRawVoiceRaw_AndReusesRhythm()
    {
        var cfg = Config();
        var snap = SongAnalysisSnapshot.Create(cfg, workerBudget: 2);
        if (!snap.TaggingEnabled) Assert.Ignore("semantic tagging disabled/unavailable in this project config");
        var mono = Drums(8);
        var id = Id("p2b_test_retag", mono);
        SongProfileCache.InitOnMainThread();
        string path = SongProfileCache.PathFor(id);
        try
        {
            // A cache entry whose rhythm part is current but whose tags are stale (other tag key).
            var raw = MusicAnalyzer.Analyze(mono, Sr, snap.Params);
            var h = SongProfileSerializer.MakeHeader(id, snap, mono.Length, 1, Sr, hasTags: true);
            h.tagReuseKey ^= 0xFFFF;
            Directory.CreateDirectory(SongProfileCache.Root);
            File.WriteAllBytes(path, SongProfileSerializer.Write(h, raw, new[] { new MusicTagScore("stale", 1f) }));

            var o = SongAnalysisPipeline.AnalyzePcmBlocking(mono, Sr, 1, id, snap, CancellationToken.None, null, SongAnalysisCacheMode.ReadWrite);
            Assert.IsTrue(o.RhythmReused, "rhythm part should be reused");
            Assert.IsFalse(o.TagsReused, "stale tags must be recomputed");
            Assert.IsFalse(o.CacheHit);
            var stored = SongProfileSerializer.Read(File.ReadAllBytes(path), out var h2, out var tags2);
            CollectionAssert.AreEqual(raw.voice, stored.voice, "the re-tag path must save the RAW voice, not the smoothed one");
            Assert.AreEqual(snap.TagReuseKey(id.Hash), h2.tagReuseKey);
            Assert.IsTrue(tags2 == null || Array.TrueForAll(tags2, t => t.tag != "stale"));

            var hit = SongAnalysisPipeline.AnalyzePcmBlocking(mono, Sr, 1, id, snap, CancellationToken.None, null, SongAnalysisCacheMode.ReadOnly);
            Assert.IsTrue(hit.CacheHit, "second run must be a full cache hit");
            CollectionAssert.AreEqual(raw.voice, hit.Profile.voiceProbability);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ── Capture (GetData) ─────────────────────────────────────────────────────────────────────

    [Test]
    public void Capture_StreamingClip_FailsExplicitly()
    {
        var clip = AudioClip.Create("p2b_stream", Sr * 4, 1, Sr, true, data => { for (int i = 0; i < data.Length; i++) data[i] = 0.1f; });
        try
        {
            var job = new SongAnalysisJob(SongAnalysisSnapshot.Create(Config(), 1),
                new SongAnalysisInput { Clip = clip, Identity = new SongAudioIdentity(SongAudioIdentityKind.DecodedPcm, "p2b_stream", 1), Cache = SongAnalysisCacheMode.None },
                CancellationToken.None);
            // Unity itself logs why GetData fails on streamed samples; the pipeline must turn it into an error.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("streamed samples"));
            Assert.Throws<InvalidOperationException>(() => SongAnalysisPipeline.RunBlocking(job, null));
            Assert.AreEqual(1, AnalysisConcurrency.Gate.CurrentCount, "gate released after a failure");
        }
        finally { UnityEngine.Object.DestroyImmediate(clip); }
    }

    [Test]
    public void Capture_SilentClip_IsNotAnalysedSilently()
    {
        var clip = AudioClip.Create("p2b_silent", Sr * 3, 2, Sr, false);
        clip.SetData(new float[Sr * 3 * 2], 0);
        try
        {
            var job = new SongAnalysisJob(SongAnalysisSnapshot.Create(Config(), 1),
                new SongAnalysisInput { Clip = clip, Identity = new SongAudioIdentity(SongAudioIdentityKind.DecodedPcm, "p2b_silent", 1), Cache = SongAnalysisCacheMode.None },
                CancellationToken.None);
            var ex = Assert.Throws<InvalidOperationException>(() => SongAnalysisPipeline.RunBlocking(job, null));
            StringAssert.Contains("silence", ex.Message);
        }
        finally { UnityEngine.Object.DestroyImmediate(clip); }
    }

    [Test]
    public void Capture_ChunkedGetData_MatchesCoreMixdown()
    {
        var cfg = Config();
        int n = Sr * 5 + 123; // not a multiple of the capture chunk
        var inter = new float[n * 2];
        var rng = new System.Random(3);
        for (int i = 0; i < inter.Length; i++) inter[i] = (float)(rng.NextDouble() * 2 - 1) * 0.5f;
        var clip = AudioClip.Create("p2b_stereo", n, 2, Sr, false);
        clip.SetData(inter, 0);
        try
        {
            var snap = SongAnalysisSnapshot.Create(cfg, 2);
            var viaClip = SongAnalysisPipeline.RunBlocking(new SongAnalysisJob(snap,
                new SongAnalysisInput { Clip = clip, Identity = new SongAudioIdentity(SongAudioIdentityKind.DecodedPcm, "p2b_stereo", 7), Cache = SongAnalysisCacheMode.None },
                CancellationToken.None), null);
            // Reference: whole-buffer GetData + the core mixdown.
            var all = new float[n * 2];
            Assert.IsTrue(clip.GetData(all, 0));
            var mono = MusicAnalyzer.MixToMono(all, 2);
            var viaPcm = SongAnalysisPipeline.AnalyzePcmBlocking(mono, Sr, 2, new SongAudioIdentity(SongAudioIdentityKind.DecodedPcm, "p2b_stereo", 7), snap, CancellationToken.None);
            CollectionAssert.AreEqual(viaPcm.ProfileBytes, viaClip.ProfileBytes, "chunked capture must equal a full GetData + core mixdown");
        }
        finally { UnityEngine.Object.DestroyImmediate(clip); }
    }

    // ── Old-bug regressions ───────────────────────────────────────────────────────────────────

    [Test]
    public void FirstFullWindow_IsAnalysed_AndAttackAtZeroDetected()
    {
        var cfg = Config();
        var snap = SongAnalysisSnapshot.Create(cfg, 2);
        var x = new float[Sr * 2];
        for (int i = 0; i < (int)(0.1 * Sr); i++) x[i] = 0.8f * (float)(Math.Sin(2 * Math.PI * 80 * i / Sr) * Math.Exp(-(double)i / Sr * 30));
        var r = MusicAnalyzer.Analyze(x, Sr, snap.Params);
        int hop = (int)Math.Round(r.hopTime * Sr);
        Assert.AreEqual((x.Length - 1) / hop + 1, r.frameCount, "frames cover the whole signal, first window included");
        Assert.Greater(r.energy[0], 0f, "frame 0 (window centred on t=0) carries the attack");
        Assert.Greater(r.onsets.Length, 0);
        Assert.Less(r.onsets[0].time, 0.02f, "the attack at t=0 is detected at the start");
    }

    [Test]
    public void Bpm_WithoutEvidence_IsZeroNotArbitrary()
    {
        var snap = SongAnalysisSnapshot.Create(Config(), 2);
        var x = new float[Sr * 6];
        for (int i = 0; i < (int)(0.05 * Sr); i++) x[Sr * 2 + i] = 0.5f * (float)Math.Exp(-(double)i / Sr * 60) * (i % 7 - 3) / 3f; // one isolated hit
        var r = MusicAnalyzer.Analyze(x, Sr, snap.Params);
        Assert.AreEqual(0f, r.estimatedBpm, "no tempo evidence → 0 (the old code returned 120)");
        var p = SongProfileFactory.Create(r, null, 0, Id("bpm", x), snap.Params.Hash, snap.Derived);
        Assert.AreEqual(0f, p.estimatedBPM);
        Assert.AreEqual(0, p.beatTimes.Length);
    }

    [Test]
    public void OnsetTimes_AreTheDetectedEvents_Ascending()
    {
        var snap = SongAnalysisSnapshot.Create(Config(), 2);
        var mono = Drums(6);
        var r = MusicAnalyzer.Analyze(mono, Sr, snap.Params);
        var p = SongProfileFactory.Create(r, null, 0, Id("on", mono), snap.Params.Hash, snap.Derived);
        Assert.AreEqual(p.onsetEvents.Length, p.onsetTimes.Length);
        for (int i = 0; i < p.onsetTimes.Length; i++) Assert.AreEqual(p.onsetEvents[i].time, p.onsetTimes[i]);
        for (int i = 1; i < p.onsetTimes.Length; i++) Assert.GreaterOrEqual(p.onsetTimes[i], p.onsetTimes[i - 1]);
    }

    // ── Determinism / budget / cancellation ───────────────────────────────────────────────────

    [Test]
    public void SequentialAndParallel_ProduceIdenticalProfiles_InUnity()
    {
        var cfg = Config();
        var mono = Drums(10);
        var id = Id("par", mono);
        var seq = SongAnalysisPipeline.AnalyzePcmBlocking(mono, Sr, 1, id, SongAnalysisSnapshot.Create(cfg, 1), CancellationToken.None);
        var par = SongAnalysisPipeline.AnalyzePcmBlocking(mono, Sr, 1, id, SongAnalysisSnapshot.Create(cfg, 4), CancellationToken.None);
        CollectionAssert.AreEqual(seq.ProfileBytes, par.ProfileBytes);
    }

    [Test]
    public void ChunkedInference_MatchesSingleBatch()
    {
        var cfg = Config();
        var mono = Drums(16); // 5 musicnn patches
        var id = Id("chunks", mono);
        try
        {
            AnalysisConcurrency.TagPatchesPerChunkOverride = 64;
            var whole = SongAnalysisPipeline.AnalyzePcmBlocking(mono, Sr, 1, id, SongAnalysisSnapshot.Create(cfg, 2), CancellationToken.None);
            if (whole.Tags == null) Assert.Ignore("semantic tagging disabled/unavailable");
            AnalysisConcurrency.TagPatchesPerChunkOverride = 2; // 2 + 2 + 1
            var chunked = SongAnalysisPipeline.AnalyzePcmBlocking(mono, Sr, 1, id, SongAnalysisSnapshot.Create(cfg, 2), CancellationToken.None);
            Assert.AreEqual(whole.Tags.Length, chunked.Tags.Length);
            for (int i = 0; i < whole.Tags.Length; i++)
            {
                Assert.AreEqual(whole.Tags[i].tag, chunked.Tags[i].tag);
                Assert.AreEqual(whole.Tags[i].score, chunked.Tags[i].score, 1e-5f);
            }
        }
        finally { AnalysisConcurrency.TagPatchesPerChunkOverride = 0; }
    }

    [Test]
    public void Blocking_Cancellation_ThrowsAndReleasesGate()
    {
        var mono = Drums(30);
        using var cts = new CancellationTokenSource();
        int calls = 0;
        Assert.Catch<OperationCanceledException>(() =>
            SongAnalysisPipeline.AnalyzePcmBlocking(mono, Sr, 1, Id("cancel", mono), SongAnalysisSnapshot.Create(Config(), 2), cts.Token,
                p => { if (++calls == 2) cts.Cancel(); }));
        Assert.AreEqual(1, AnalysisConcurrency.Gate.CurrentCount);
        SentisMusicTagger.PumpDeferred(force: true);
        Assert.AreEqual(0, SentisMusicTagger.DeferredCount);
    }
}
#endif
