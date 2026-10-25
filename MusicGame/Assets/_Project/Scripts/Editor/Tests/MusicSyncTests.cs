#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit-mode tests of the music sync layer (phase P1): MusicTime conversions, the MusicClockCore
/// state machine (warmup cap / mid-song start / hold / pause / stall / natural end), cursor
/// fast-forward, the live detectors' cooldown + reset behaviour and clip-time lookups, the
/// analyzer's discontinuity resets, and the camera-pulse dedup. Pure logic only — the real
/// AudioSource behaviour is covered by the play-mode probe (MusicSyncProbe).
/// Run: Unity -batchmode -projectPath &lt;proj&gt; -runTests -testPlatform EditMode
///      -testFilter MusicSyncTests -testResults &lt;xml&gt;
/// </summary>
public class MusicSyncTests
{
    private const int   Freq   = 48000;
    private const float Warmup = 3f;
    private const float Dt     = 1f / 60f;

    private static AudioPlaybackReading Reading(bool playing, double clipTime, double clipLength = 10.0)
    {
        return new AudioPlaybackReading
        {
            HasClip      = true,
            IsPlaying    = playing,
            TimeSamples  = (int)System.Math.Round(clipTime * Freq),
            Frequency    = Freq,
            TotalSamples = (int)System.Math.Round(clipLength * Freq),
        };
    }

    /// <summary>A clock that played from clip 0 for `seconds` (audio exactly tracking game time).</summary>
    private static MusicClockCore PlayingClock(float seconds, double clipLength = 10.0)
    {
        var c = new MusicClockCore();
        c.Initialize(Warmup, 10f, 0f);
        for (float t = 0f; t < Warmup; t += Dt) c.Tick(Dt, Dt, default);
        c.BeginPlayback(0.0, clipLength);
        double clip = 0.0;
        c.Tick(0f, 0f, Reading(true, clip, clipLength));
        for (float t = 0f; t < seconds; t += Dt)
        {
            clip += Dt;
            c.Tick(Dt, Dt, Reading(true, clip, clipLength));
        }
        return c;
    }

    // ── MusicTime ────────────────────────────────────────────────────────────

    [Test]
    public void MusicTime_ConversionsRoundTrip()
    {
        Assert.AreEqual(7f, MusicTime.ClipToSong(4f, 3f), 1e-6f);
        Assert.AreEqual(4f, MusicTime.SongToClip(7f, 3f), 1e-6f);
        Assert.AreEqual(MusicTime.ClipToSong(1.25f, 3f), MusicTime.OnsetToEventTime(1.25f, 3f), 1e-6f);
        Assert.AreEqual(-3f, MusicTime.SongToClip(0f, 3f), 1e-6f, "countdown from clip 0 is negative clip time");
        Assert.AreEqual(70f, MusicTime.SongToDistance(7f, 10f), 1e-5f);
        Assert.AreEqual(7f, MusicTime.DistanceToSong(70f, 10f), 1e-5f);
        Assert.AreEqual(0f, MusicTime.DistanceToSong(70f, 0f));
        Assert.AreEqual(2.5, MusicTime.SamplesToSeconds(120000, 48000), 1e-12);
        Assert.AreEqual(479999, MusicTime.SecondsToSampleIndex(20.0, 48000, 480000), "clamped inside the clip");
        Assert.AreEqual(0, MusicTime.SecondsToSampleIndex(-1.0, 48000, 480000));
    }

    [Test]
    public void MusicTime_FirstEventAtOrAfter()
    {
        var events = new[] { Evt(3.5f), Evt(10f), Evt(31f), Evt(33.2f), Evt(34f) };
        Assert.AreEqual(0, MusicTime.FirstEventAtOrAfter(events, 0f));
        Assert.AreEqual(1, MusicTime.FirstEventAtOrAfter(events, 10f), "an event exactly at the time is kept");
        Assert.AreEqual(3, MusicTime.FirstEventAtOrAfter(events, 33f));
        Assert.AreEqual(5, MusicTime.FirstEventAtOrAfter(events, 40f));
        Assert.AreEqual(0, MusicTime.FirstEventAtOrAfter(null, 5f));
    }

    private static TimelineEvent Evt(float eventTime) => new TimelineEvent { eventTime = eventTime, eventDistance = eventTime * 10f };

    // ── MusicClockCore ───────────────────────────────────────────────────────

    [Test]
    public void Clock_WarmupCapsAtHandoff_LoadWaitNeverOverrunsOrSnapsBack()
    {
        var c = new MusicClockCore();
        c.Initialize(Warmup, 10f, 0f);
        Assert.AreEqual(MusicClockState.Warmup, c.State);
        Assert.AreEqual(0f, c.SongTime);

        float prev = c.SongTime;
        // 3 s of warmup + 1.5 s waiting for the clip to finish loading.
        for (int i = 0; i < 270; i++)
        {
            c.Tick(Dt, Dt, default);
            Assert.GreaterOrEqual(c.SongTime, prev);
            prev = c.SongTime;
        }
        Assert.AreEqual(Warmup, c.SongTime, 1e-5f, "warmup is capped at the playback start");
        Assert.AreEqual(MusicClockState.Warmup, c.State);

        c.BeginPlayback(0.0, 10.0);
        c.Tick(0f, 0f, Reading(true, 0.0));
        Assert.AreEqual(MusicClockState.Playing, c.State);
        Assert.AreEqual(Warmup, c.SongTime, 1e-4f, "no snap back at the handoff");
        Assert.IsTrue(c.HasPlaybackStarted);
    }

    [Test]
    public void Clock_MidSongStart_CountdownLeadsIntoStartOffset_NoJump()
    {
        const float start = 30f;
        var c = new MusicClockCore();
        c.Initialize(Warmup, 10f, start);
        Assert.AreEqual(start, c.SongTime, 1e-6f, "the clock starts AT the offset");
        Assert.AreEqual(start - Warmup, c.ClipTime, 1e-6f);
        Assert.AreEqual(Warmup + start, c.PlaybackStartSongTime, 1e-6f);

        float prev = c.SongTime, maxStep = 0f;
        for (int i = 0; i < 200; i++)
        {
            c.Tick(Dt, Dt, default);
            maxStep = Mathf.Max(maxStep, c.SongTime - prev);
            prev = c.SongTime;
        }
        c.BeginPlayback(start, 60.0);
        c.Tick(0f, 0f, Reading(true, start, 60.0));
        maxStep = Mathf.Max(maxStep, c.SongTime - prev);
        Assert.AreEqual(Warmup + start, c.SongTime, 1e-4f);
        Assert.LessOrEqual(maxStep, Dt + 1e-4f, "no time jump anywhere in the start sequence");
        Assert.AreEqual(start, c.ClipTime, 1e-4f);
    }

    [Test]
    public void Clock_MidSongStart_CursorFastForward_FiresNoEarlierEvent()
    {
        // GameplayManager's pulse loop, reproduced: cursor placed with MusicTime.FirstEventAtOrAfter
        // at the playback start, then only ever moved forward while SongTime >= eventTime - lead.
        const float start = 30f, lead = 0.05f;
        var events = new[] { Evt(3.5f), Evt(10f), Evt(31f), Evt(33.2f), Evt(34f) };
        var c = new MusicClockCore();
        c.Initialize(Warmup, 10f, start);
        int cursor = MusicTime.FirstEventAtOrAfter(events, c.PlaybackStartSongTime);
        var fired = new List<int>();

        void Frame()
        {
            while (cursor < events.Length && c.SongTime >= events[cursor].eventTime - lead)
                fired.Add(cursor++);
        }

        for (int i = 0; i < 180; i++) { c.Tick(Dt, Dt, default); Frame(); }
        c.BeginPlayback(start, 60.0);
        double clip = start;
        c.Tick(0f, 0f, Reading(true, clip, 60.0));
        Frame();
        Assert.IsEmpty(fired, "nothing fires at the handoff (first event is 33.2 > 33)");
        for (int i = 0; i < 120; i++) { clip += Dt; c.Tick(Dt, Dt, Reading(true, clip, 60.0)); Frame(); }
        CollectionAssert.AreEqual(new[] { 3, 4 }, fired, "only moments inside the played window fire");
    }

    [Test]
    public void Clock_Hold_FreezesWhileAudioStopped_NoWallClockAdvance()
    {
        var c = PlayingClock(5f);
        float atHold = c.SongTime;
        Assert.IsTrue(c.Hold());
        Assert.AreEqual(MusicClockState.Held, c.State);
        double paused = c.AudioClipTime;
        for (int i = 0; i < 120; i++) c.Tick(Dt, Dt, Reading(false, paused));
        Assert.AreEqual(atHold, c.SongTime, 1e-6f, "2 s of a stopped source moved nothing");

        int gen = c.Generation;
        c.BeginPlayback(4.0, 10.0); // respawn seek back to the fall point
        c.Tick(0f, 0f, Reading(true, 4.0));
        Assert.AreEqual(MusicClockState.Playing, c.State);
        Assert.AreEqual(Warmup + 4f, c.SongTime, 1e-4f);
        Assert.Greater(c.Generation, gen, "seek counts as a discontinuity");
    }

    [Test]
    public void Clock_HoldDuringWarmup_IsNoOp()
    {
        var c = new MusicClockCore();
        c.Initialize(Warmup, 10f, 0f);
        c.Tick(Dt, Dt, default);
        Assert.IsFalse(c.Hold());
        Assert.AreEqual(MusicClockState.Warmup, c.State);
        float t = c.SongTime;
        c.Tick(Dt, Dt, default);
        Assert.Greater(c.SongTime, t, "the countdown keeps running");
    }

    [Test]
    public void Clock_UnexplainedStop_StallsFrozen_NotNaturalEnd_ThenResyncs()
    {
        var c = PlayingClock(5f);
        float atStop = c.SongTime;
        // Paused externally: keeps its position.
        c.Tick(Dt, Dt, Reading(false, 5.0));
        Assert.AreEqual(MusicClockState.Stalled, c.State);
        for (int i = 0; i < 120; i++) Assert.IsFalse(c.Tick(Dt, Dt, Reading(false, 5.0)));
        Assert.AreEqual(atStop, c.SongTime, 1e-6f);
        Assert.IsFalse(c.NaturalEndDetected);

        Assert.IsTrue(c.Tick(Dt, Dt, Reading(true, 5.02)), "resuming by itself reports a resync");
        Assert.AreEqual(MusicClockState.Playing, c.State);
        Assert.AreEqual(Warmup + 5.02f, c.SongTime, 1e-4f);
    }

    [Test]
    public void Clock_StopWithPositionReset_MidSong_IsNotNaturalEnd()
    {
        var c = PlayingClock(5f);
        c.Tick(Dt, Dt, Reading(false, 0.0)); // AudioSource.Stop() resets timeSamples to 0 too
        Assert.AreEqual(MusicClockState.Stalled, c.State);
        Assert.IsFalse(c.NaturalEndDetected);
    }

    [Test]
    public void Clock_NaturalEnd_RequiresEvidence()
    {
        var c = PlayingClock(9.99f);
        Assert.AreEqual(MusicClockState.Playing, c.State);
        c.Tick(Dt, Dt, Reading(false, 0.0)); // Unity stops + rewinds a finished clip
        Assert.AreEqual(MusicClockState.Ended, c.State);
        Assert.IsTrue(c.NaturalEndDetected);
        Assert.AreEqual(Warmup + 10f, c.SongTime, 1e-3f);

        float t = c.SongTime;
        c.Tick(Dt, Dt, Reading(false, 0.0));
        Assert.AreEqual(t, c.SongTime, "Ended is frozen until farewell");
        c.BeginFarewell();
        c.Tick(Dt, Dt, Reading(false, 0.0));
        Assert.AreEqual(t + Dt, c.SongTime, 1e-5f, "farewell continues smoothly from the end");
    }

    [TestCase(0.0)]
    [TestCase(10.0)]
    [TestCase(5.0)]
    public void Clock_NaturalEnd_WhateverPositionTheStoppedSourceReports(double reportedPosition)
    {
        // Observed with a real AudioSource (MusicSyncProbe): after a natural end the stopped clip
        // reported 0, its full length, or the position playback was last started from.
        var c = PlayingClock(9.99f);
        c.Tick(Dt, Dt, Reading(false, reportedPosition));
        Assert.AreEqual(MusicClockState.Ended, c.State);
    }

    [Test]
    public void Clock_NaturalEnd_AfterLongHitch()
    {
        var c = PlayingClock(9.5f);
        c.Tick(0.6f, 0.6f, Reading(false, 0.0));
        Assert.AreEqual(MusicClockState.Ended, c.State, "last position + elapsed real time reached the end");
    }

    [Test]
    public void Clock_NotPlayingBeforeFirstObservation_Waits()
    {
        var c = new MusicClockCore();
        c.Initialize(Warmup, 10f, 0f);
        c.BeginPlayback(9.99, 10.0);
        float t = c.SongTime;
        for (int i = 0; i < 10; i++) c.Tick(Dt, Dt, Reading(false, 0.0));
        Assert.AreEqual(MusicClockState.Playing, c.State, "playback never observed — no end, no stall");
        Assert.AreEqual(t, c.SongTime, "and frozen, never wall clock");
    }

    [Test]
    public void Clock_Pause_FreezesAndResumeResyncs()
    {
        var c = PlayingClock(2f);
        float atPause = c.SongTime;
        c.Pause();
        Assert.AreEqual(MusicClockState.Paused, c.State);
        Assert.AreEqual(MusicClockState.Playing, c.EffectiveState);
        for (int i = 0; i < 60; i++) c.Tick(Dt, Dt, Reading(false, c.AudioClipTime));
        Assert.AreEqual(atPause, c.SongTime);
        c.Resume();
        c.Tick(Dt, Dt, Reading(true, 2.5)); // pretend the audio is a bit elsewhere: hard resync
        Assert.AreEqual(Warmup + 2.5f, c.SongTime, 1e-4f);
    }

    [Test]
    public void Clock_PauseNearEnd_IsNotNaturalEnd_AndPauseDuringHoldStaysHeld()
    {
        var c = PlayingClock(9.99f);
        c.Pause();
        c.Tick(Dt, Dt, Reading(false, 0.0));
        Assert.AreEqual(MusicClockState.Paused, c.State);
        Assert.IsFalse(c.NaturalEndDetected);

        var h = PlayingClock(4f);
        h.Hold();
        h.Pause();
        h.Resume();
        Assert.AreEqual(MusicClockState.Held, h.State);
    }

    [Test]
    public void Clock_Filter_EasesSmallDrift_SnapsLargeError()
    {
        var c = PlayingClock(2f);
        float before = c.SongTime;
        c.Tick(Dt, Dt, Reading(true, c.AudioClipTime + Dt + 0.05)); // 50 ms ahead → eased
        float eased = c.SongTime - (before + Dt);
        Assert.Greater(eased, 0f);
        Assert.Less(eased, 0.05f);

        c.Tick(Dt, Dt, Reading(true, 5.0)); // way off → snapped
        Assert.AreEqual(Warmup + 5f, c.SongTime, 1e-4f);
    }

    // ── Live detectors ───────────────────────────────────────────────────────

    private readonly List<KickDetectedEvent> _kicks = new();
    private System.Action<KickDetectedEvent> _onKick;

    [SetUp]
    public void SetUp()
    {
        _kicks.Clear();
        _onKick = e => _kicks.Add(e);
        EventBus.Subscribe(_onKick);
    }

    [TearDown]
    public void TearDown() => EventBus.Unsubscribe(_onKick);

    private static AudioAnalysisConfig DetectorConfig(float threshold = 0.1f, float cooldown = 0.2f)
    {
        var cfg = ScriptableObject.CreateInstance<AudioAnalysisConfig>();
        cfg.kickThreshold = threshold;
        cfg.kickCooldown  = cooldown;
        return cfg;
    }

    private static AudioData Frame(float clipTime, float subBass)
    {
        var bands = new FrequencyBand[6];
        bands[0].smoothedValue = subBass;
        return new AudioData { bands = bands, clipTime = clipTime, spectrum = new float[8], waveform = new float[8] };
    }

    [Test]
    public void Kick_UpdatesHistoryDuringCooldown()
    {
        var k = new KickDetector();
        k.Initialize(DetectorConfig());
        k.Analyze(Frame(0.00f, 0.05f)); // primes
        k.Analyze(Frame(0.02f, 0.90f)); // fires
        Assert.AreEqual(1, _kicks.Count);
        k.Analyze(Frame(0.05f, 0.30f)); // cooldown — history must still follow
        k.Analyze(Frame(0.10f, 0.20f)); // cooldown
        k.Analyze(Frame(0.30f, 0.60f)); // rising vs 0.20 → a real new attack
        Assert.AreEqual(2, _kicks.Count, "a stale pre-cooldown value (0.9) would have hidden this attack");
        Assert.AreEqual(0.30f, _kicks[1].Time, 1e-6f, "events carry clip time");
    }

    [Test]
    public void Kick_ResetForgetsCooldownAndHistory()
    {
        var k = new KickDetector();
        k.Initialize(DetectorConfig());
        k.Analyze(Frame(5.00f, 0.05f));
        k.Analyze(Frame(5.02f, 0.90f));
        Assert.AreEqual(1, _kicks.Count);

        k.Reset(); // seek back to clip 1.0
        k.Analyze(Frame(1.00f, 0.95f));
        Assert.AreEqual(1, _kicks.Count, "the first frame after a reset only primes (no delta vs pre-seek state)");
        k.Analyze(Frame(1.02f, 1.00f));
        Assert.AreEqual(2, _kicks.Count, "cooldown from the pre-seek clip time no longer blocks");
    }

    [Test]
    public void Kick_QueriesProfileWithClipTime()
    {
        // Band 0 is very loud during the first second of the clip and silent afterwards.
        var profile = new SongProfile { analysisHopTime = 0.01f, bandEnvelopes = new float[6][] };
        profile.bandEnvelopes[0] = new float[1000];
        for (int i = 0; i < 100; i++) profile.bandEnvelopes[0][i] = 10f;

        var k = new KickDetector();
        k.Initialize(DetectorConfig());
        k.SetSongProfile(profile);
        k.Analyze(Frame(0.50f, 0.1f));
        k.Analyze(Frame(0.52f, 0.5f));
        Assert.AreEqual(0, _kicks.Count, "at clip 0.5 s the profile-adapted threshold is 18");

        k.Reset();
        k.Analyze(Frame(5.00f, 0.1f));
        k.Analyze(Frame(5.02f, 0.5f));
        Assert.AreEqual(1, _kicks.Count, "at clip 5 s the profile is quiet, the base threshold applies");
    }

    [Test]
    public void ClipTimeContinuity_DetectsJumpsOnly()
    {
        var c = new ClipTimeContinuity();
        Assert.IsFalse(c.Observe(1.00, false, 0));
        Assert.IsFalse(c.Observe(1.02, true, Dt), "normal frame");
        Assert.IsFalse(c.Observe(1.52, true, 0.5), "a 0.5 s hitch is still continuous");
        Assert.IsFalse(c.Observe(1.53, false, 0), "resume after a pause gap at the same position");
        Assert.IsTrue(c.Observe(0.80, true, Dt), "backward seek");
        Assert.IsTrue(c.Observe(4.00, true, Dt), "forward seek");
        Assert.IsTrue(c.Observe(6.00, false, 0), "seek during a pause gap");
    }

    private class CountingDetector : IAudioDetector
    {
        public int Resets, Analyses;
        public bool IsEnabled => true;
        public void Initialize(AudioAnalysisConfig config) { }
        public void SetSongProfile(SongProfile profile) { }
        public void Analyze(AudioData data) => Analyses++;
        public void Reset() => Resets++;
    }

    [Test]
    public void Analyzer_ResetsDetectorsOnDiscontinuityEventAndClipJump()
    {
        var go  = new GameObject("AnalyzerTest");
        var det = new CountingDetector();
        try
        {
            var sampler  = go.AddComponent<AudioSampler>();
            var analyzer = go.AddComponent<AudioAnalyzer>();
            var cfg      = ScriptableObject.CreateInstance<AudioAnalysisConfig>();
            sampler.Initialize(null, cfg);
            analyzer.Initialize(sampler, cfg, new IAudioDetector[] { det });

            var process  = typeof(AudioAnalyzer).GetMethod("Process", BindingFlags.NonPublic | BindingFlags.Instance);
            var spectrum = new float[1024];
            var wave     = new float[1024];
            process.Invoke(analyzer, new object[] { spectrum, wave, 1.00 });
            process.Invoke(analyzer, new object[] { spectrum, wave, 1.05 });
            Assert.AreEqual(0, det.Resets, "continuous clip time");
            process.Invoke(analyzer, new object[] { spectrum, wave, 3.00 });
            Assert.AreEqual(1, det.Resets, "unannounced clip-time jump");

            EventBus.Publish(new MusicTimeDiscontinuityEvent { Reason = MusicTimeDiscontinuityReason.Seek });
            Assert.AreEqual(2, det.Resets, "clock seek");
            EventBus.Publish(new PlayerRespawnedEvent { CheckpointIndex = -1 });
            Assert.AreEqual(3, det.Resets, "respawn");
            Assert.AreEqual(3, det.Analyses);
        }
        finally
        {
            // Edit mode never sends OnDestroy to a non-[ExecuteAlways] component, so invoke the
            // lifecycle message the way play mode would before destroying.
            var analyzer = go.GetComponent<AudioAnalyzer>();
            typeof(AudioAnalyzer).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(analyzer, null);
            Object.DestroyImmediate(go);
        }
        EventBus.Publish(new MusicTimeDiscontinuityEvent());
        Assert.AreEqual(3, det.Resets, "a destroyed analyzer is unsubscribed");
    }

    // ── Camera pulse dedup ───────────────────────────────────────────────────

    [Test]
    public void PulseDedup_SameEventOrSameOnsetMerges_DistinctTimesAdd()
    {
        var d = new MusicalPulseDeduplicator(0.03f);
        Assert.AreEqual(0.5f, d.Accept(1, 10.00f, 0.5f), 1e-6f);
        Assert.AreEqual(0f,   d.Accept(1, 10.00f, 0.5f), 1e-6f, "the same event twice");
        Assert.AreEqual(0.3f, d.Accept(2, 10.01f, 0.8f), 1e-6f, "same onset, stronger: only the extra");
        Assert.AreEqual(0f,   d.Accept(3, 10.02f, 0.4f), 1e-6f, "same onset, weaker: nothing");
        // A slow frame delivering two real, distinct hits: both apply in full, immediately.
        Assert.AreEqual(0.5f, d.Accept(4, 10.10f, 0.5f), 1e-6f);
        Assert.AreEqual(0.5f, d.Accept(5, 10.20f, 0.5f), 1e-6f);

        d.Reset(); // respawn: the same moments legitimately pulse again
        Assert.AreEqual(0.5f, d.Accept(1, 10.00f, 0.5f), 1e-6f);
    }
}
#endif
