#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Play Mode benchmark + lifecycle probe of the song pre-analysis on REAL catalog clips (as
/// imported: Vorbis q1, DecompressOnLoad, 48 kHz stereo), measured INSIDE Unity (editor play mode,
/// Mono). Per song: wall time, the pipeline's per-stage times, the longest main-thread frame while
/// analysing, managed allocations ("GC Allocated In Frame" summed per frame), peak GC heap use
/// (sampled every 1 ms from a background thread — includes not-yet-collected garbage), Mono heap
/// growth, peak Unity-native allocations (sampled per frame) and the resident PCM (the clip's native
/// size + the analysis' transient mono copy).
/// Runtime path (AudioPreAnalyzer, as the game uses it): full analysis (cache entry removed first) and
/// cache hit. Editor path (SongAnalysisPipeline.AnalyzeClipBlocking): one song, no cache — its binary
/// profile must be byte-identical to the runtime one. Lifecycle checks: duplicate join, supersede
/// (only the newest request publishes), cancellation (no publish, gate + Sentis resources released).
/// Runs in a throwaway scene under Scenes/Debug/ so AppBootstrap does not boot (no flow, no save).
/// The pre-P2B ("old") pipeline was measured with the previous revision of this probe: the numbers
/// are in Logs/p2b_bench_old.txt / the P2B report.
///
/// Batch: -executeMethod AnalysisBenchmarkProbe.RunFromCommandLine [-benchOut path] [-benchSongs N]
/// Exit 0 = finished and every check passed, 1 = a check failed, 2 = exception/timeout.
/// </summary>
[InitializeOnLoad]
public static class AnalysisBenchmarkProbe
{
    private const string KeyActive = "AnalysisBenchmarkProbe.Active";
    private const string KeyOut    = "AnalysisBenchmarkProbe.Out";
    private const string KeySongs  = "AnalysisBenchmarkProbe.Songs";
    public  const string ScenePath = "Assets/_Project/Scenes/Debug/__AnalysisBench.unity";

    public static readonly string[] Songs =
    {
        "Assets/_Project/Audio/Music/Low Megas — Combo Master Tribe.wav",
        "Assets/_Project/Audio/Music/Gym Bros  - Dyin' Alive.wav",
        "Assets/_Project/Audio/Music/Older New — Azure WeekStart.wav",
    };

    private static readonly StringBuilder s_log = new();
    private static bool s_started;
    private static int s_pass, s_fail;

    static AnalysisBenchmarkProbe()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        EditorApplication.update += Tick;
    }

    public static void RunFromCommandLine()
    {
        string outPath = Path.GetFullPath("Logs/AnalysisBenchmark.txt");
        int songs = Songs.Length;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-benchOut") outPath = args[i + 1];
            if (args[i] == "-benchSongs") int.TryParse(args[i + 1], out songs);
        }
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, outPath);
        SessionState.SetInt(KeySongs, Mathf.Clamp(songs, 1, Songs.Length));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || s_started) return;
        s_started = true;
        var go = new GameObject("[AnalysisBench]");
        var runner = go.AddComponent<BenchRunner>();
        runner.StartCoroutine(Guard(runner));
    }

    private static IEnumerator Guard(BenchRunner runner)
    {
        Log($"Analysis benchmark — Unity {Application.unityVersion}, {SystemInfo.processorType} ({SystemInfo.processorCount} logical), " +
            $"{SystemInfo.systemMemorySize} MB RAM, batch {Application.isBatchMode}, editor play mode (Mono, not IL2CPP), " +
            $"worker budget {AnalysisConcurrency.ComputeWorkerBudget()} (overlap {AnalysisConcurrency.OverlapWorkers(AnalysisConcurrency.ComputeWorkerBudget())}).");
        IEnumerator body = NewPipeline(runner, SessionState.GetInt(KeySongs, Songs.Length));
        double deadline = BenchMetrics.Clock + 900;
        while (true)
        {
            bool next;
            try { next = body.MoveNext(); }
            catch (Exception e) { Log("EXCEPTION " + e); Finish(2); yield break; }
            if (!next) break;
            if (BenchMetrics.Clock > deadline) { Log("TIMEOUT"); Finish(2); yield break; }
            yield return body.Current;
        }
        Finish(s_fail == 0 ? 0 : 1);
    }

    // ── New pipeline ────────────────────────────────────────────────────────────────────────

    private static IEnumerator NewPipeline(BenchRunner runner, int songCount)
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var config = app.audioAnalysis;
        var pre = runner.gameObject.AddComponent<AudioPreAnalyzer>();
        int readyEvents = 0;
        SongProfile lastPublished = null;
        Action<SongProfileReadyEvent> onReady = e => { readyEvents++; lastPublished = e.Profile; };
        EventBus.Subscribe(onReady);
        var progress = new List<float>();
        Action<PreAnalysisProgressEvent> onProg = e => progress.Add(e.Progress);
        EventBus.Subscribe(onProg);
        var benchFiles = new List<string>();
        byte[] runtimeBytesFirst = null;
        AudioClip firstClip = null;

        for (int s = 0; s < songCount; s++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Songs[s]);
            yield return EnsureLoaded(clip);
            long pcmNative = Profiler.GetRuntimeMemorySizeLong(clip);
            Log($"\n## {clip.name}: {clip.length:F1} s, {clip.frequency} Hz, {clip.channels} ch, loadType {clip.loadType}; " +
                $"resident PCM: clip native {Mb(pcmNative)} (whole song, held by the clip itself) + transient mono copy {Mb((long)clip.samples * 4)} during capture→rhythm");

            string key = "clip_" + clip.name;
            SongProfileCache.InitOnMainThread();
            DeleteCacheEntries(key);

            // ── Runtime full analysis (cache miss) ──
            for (int pass = 0; pass < 2; pass++)
            {
                string label = pass == 0 ? "RUNTIME full analysis (cache miss)" : "RUNTIME cache HIT";
                progress.Clear();
                int before = readyEvents;
                SongProfile result = null; MusicTagScore[] tags = null; double tTags = -1, tDone = -1;
                var m = BenchMetrics.Begin(runner);
                double t0 = m.Now;
                pre.Begin(new SongAnalysisRequest
                {
                    Clip = clip,
                    OnTagsReady = t => { tags = t; tTags = BenchMetrics.Clock; },
                    OnProfileReady = p => { result = p; tDone = BenchMetrics.Clock; },
                    OnFailed = e => Log("  FAILED: " + e),
                }, config);
                while (result == null && pre.IsBusy) yield return null;
                var r = m.End();
                Log($"{label}: request→profile {(tDone - t0) * 1000:F0} ms, request→tags {(tTags - t0) * 1000:F0} ms");
                Log("  " + r);
                var o = LastOutcomeTimings;
                if (result != null)
                {
                    int multi = 0, k = 0, sn = 0, hh = 0, gen = 0;
                    foreach (var e in result.onsetEvents)
                    {
                        int bits = 0;
                        if (e.Has(MusicGame.Audio.Analysis.OnsetFamily.Kick)) { k++; bits++; }
                        if (e.Has(MusicGame.Audio.Analysis.OnsetFamily.Snare)) { sn++; bits++; }
                        if (e.Has(MusicGame.Audio.Analysis.OnsetFamily.HiHat)) { hh++; bits++; }
                        if (e.Has(MusicGame.Audio.Analysis.OnsetFamily.Generic)) gen++;
                        if (bits > 1) multi++;
                    }
                    Log($"  onsets {result.onsetTimes.Length} (Kick {k}, Snare {sn}, HiHat {hh}, Generic {gen}, multi-label {multi}), BPM {result.estimatedBPM:F2} conf {result.bpmConfidence:F2}, " +
                        $"beats {result.beatTimes.Length}, frames {result.energyEnvelope.Length}, hop {result.analysisHopTime * 1000:F3} ms, tags {(tags != null ? tags.Length : -1)} " +
                        $"[{(tags != null && tags.Length > 0 ? tags[0].tag + " " + tags[0].score.ToString("F2") : "-")}], key {result.KeyName}, voice zones {result.vocalZones?.Length}");
                }
                Check($"{label}: exactly one SongProfileReadyEvent", readyEvents - before == 1 && lastPublished == result, $"events {readyEvents - before}");
                Check($"{label}: progress monotonic, ends at 1", Monotonic(progress) && progress.Count > 0 && progress[progress.Count - 1] >= 1f, $"{progress.Count} events, last {(progress.Count > 0 ? progress[progress.Count - 1] : -1):F3}");
                Check($"{label}: raw voice kept raw (smoothed copy separate)", result != null && result.voiceProbabilitySmoothed != null && !ReferenceEquals(result.voiceProbability, result.voiceProbabilitySmoothed));
                if (pass == 1) Check("cache HIT reported as such", s_lastHit, "");
                if (pass == 0 && s == 0) { runtimeBytesFirst = ReadCacheEntry(key); firstClip = clip; }
                foreach (var f in Directory.GetFiles(SongProfileCache.Root, SongProfileCache.Sanitize(key) + "_*")) if (!benchFiles.Contains(f)) benchFiles.Add(f);
                result = null;
                yield return null;
            }
        }

        // ── Sentis chunk size: native peak vs time; tags must not depend on it ──
        if (firstClip != null)
        {
            Log($"\n## Sentis patches-per-chunk sweep on {firstClip.name} (runtime path, full analysis each)");
            MusicTagScore[] reference = null;
            foreach (int chunk in new[] { 8, 2, 4, 16 })
            {
                AnalysisConcurrency.TagPatchesPerChunkOverride = chunk;
                DeleteCacheEntries("clip_" + firstClip.name);
                SongProfile result = null; MusicTagScore[] tags = null;
                var m = BenchMetrics.Begin(runner);
                pre.Begin(new SongAnalysisRequest { Clip = firstClip, OnTagsReady = t => tags = t, OnProfileReady = p => result = p }, config);
                while (result == null && pre.IsBusy) yield return null;
                var r = m.End();
                Log($"  chunk {chunk,2}: {r}");
                if (reference == null) reference = tags;
                else Check($"tags identical with chunk {chunk} vs 8", SameTags(reference, tags));
                result = null;
                yield return null;
            }
            AnalysisConcurrency.TagPatchesPerChunkOverride = 0;
        }

        // ── Editor blocking entry point: same pipeline, byte-identical output ──
        if (firstClip != null)
        {
            var m = BenchMetrics.Begin(runner);
            var outcome = SongAnalysisPipeline.AnalyzeClipBlocking(firstClip, "clip_" + firstClip.name, config, CancellationToken.None, null, SongAnalysisCacheMode.None);
            var r = m.End();
            Log($"\n## EDITOR blocking entry (AnalyzeClipBlocking, no cache) on {firstClip.name}: {outcome.Timings}");
            Log("  " + r + "  (the whole call blocks the main thread by design — editor tools only)");
            Check("editor blocking profile is byte-identical to the runtime (worker budget, overlap) profile",
                  runtimeBytesFirst != null && Same(runtimeBytesFirst, outcome.ProfileBytes),
                  $"runtime {runtimeBytesFirst?.Length} bytes vs editor {outcome.ProfileBytes?.Length} bytes");
            var reread = SongAnalysisPipeline.LoadProfile(outcome.ProfileBytes, new DerivedAnalysisSettings(config), out var hdr);
            Check("binary round-trip: onsets/beats/frames identical", reread.onsetTimes.Length == outcome.Profile.onsetTimes.Length &&
                  reread.beatTimes.Length == outcome.Profile.beatTimes.Length && reread.energyEnvelope.Length == outcome.Profile.energyEnvelope.Length &&
                  hdr.audioIdentity == outcome.Identity.Hash);
            Log($"  binary profile {Mb(outcome.ProfileBytes.Length)}; header: format {hdr.formatVersion}, pipeline {hdr.pipelineVersion}, params {hdr.paramsHash:x16}, " +
                $"tagModel {hdr.tagModelVersion}, tagKey {hdr.tagReuseKey:x16}, songId '{hdr.songId}', audio {hdr.audioIdentity:x16}, clip {hdr.clipSamples}/{hdr.clipChannels}/{hdr.clipFrequency}");
            outcome = null;
            yield return null;

            // ── Lifecycle: duplicate join, supersede, cancel ──
            var clipB = AssetDatabase.LoadAssetAtPath<AudioClip>(Songs[songCount > 1 ? 1 : 0]);
            DeleteCacheEntries("clip_" + firstClip.name);
            int before = readyEvents;
            SongProfile gotA = null, gotB = null;
            int idA = pre.Begin(new SongAnalysisRequest { Clip = firstClip, OnProfileReady = p => gotA = p }, config);
            int idA2 = pre.Begin(new SongAnalysisRequest { Clip = firstClip, OnProfileReady = p => gotA = p }, config);
            Check("duplicate Begin (same clip/selection) joins the running request", idA == idA2, $"{idA} vs {idA2}");
            for (int i = 0; i < 20; i++) yield return null;
            int idB = pre.Begin(new SongAnalysisRequest { Clip = clipB, OnProfileReady = p => gotB = p }, config);
            double tWait = BenchMetrics.Clock;
            while (gotB == null && pre.IsBusy && BenchMetrics.Clock - tWait < 120) yield return null;
            Check("superseded request publishes nothing; the newest publishes once", gotA == null && gotB != null && readyEvents - before == 1 && idB != idA,
                  $"A {(gotA != null)}, B {(gotB != null)}, events {readyEvents - before}");

            // Cancel mid-way (during capture / inference / rhythm, depending on timing).
            DeleteCacheEntries("clip_" + firstClip.name);
            before = readyEvents;
            SongProfile gotC = null;
            pre.Begin(new SongAnalysisRequest { Clip = firstClip, OnProfileReady = p => gotC = p }, config);
            for (int i = 0; i < 40; i++) yield return null;
            double tCancel = BenchMetrics.Clock;
            pre.Cancel();
            // The gate frees once the job's workers observed the token (a fresh request can then start).
            while (AnalysisConcurrency.Gate.CurrentCount == 0 && BenchMetrics.Clock - tCancel < 30) yield return null;
            double releaseMs = (BenchMetrics.Clock - tCancel) * 1000;
            double tDrain = BenchMetrics.Clock;
            while (SentisMusicTagger.DeferredCount > 0 && BenchMetrics.Clock - tDrain < 30) yield return null;
            for (int i = 0; i < 10; i++) yield return null;
            Check("cancel: no profile published, gate released, Sentis resources disposed",
                  gotC == null && readyEvents == before && AnalysisConcurrency.Gate.CurrentCount == 1 && SentisMusicTagger.DeferredCount == 0,
                  $"gate released {releaseMs:F0} ms after Cancel(), deferred inference drained {(BenchMetrics.Clock - tDrain) * 1000:F0} ms later");
            Log($"  cancel→gate released {releaseMs:F0} ms");
        }

        EventBus.Unsubscribe(onReady);
        EventBus.Unsubscribe(onProg);
        // Bench entries only (keys "clip_<name>" — the game keys catalog songs by songId).
        foreach (var path in Songs) DeleteCacheEntries("clip_" + Path.GetFileNameWithoutExtension(path));
    }

    // Outcome flags of the last request (read from the log line AudioPreAnalyzer prints).
    private static bool s_lastHit;
    private static string LastOutcomeTimings;

    [InitializeOnLoadMethod]
    private static void HookLog()
    {
        Application.logMessageReceived += (msg, _, __) =>
        {
            if (!msg.StartsWith("[AudioPreAnalyzer] Request #") || !msg.Contains(" done — ")) return;
            s_lastHit = msg.Contains("cache HIT");
            int nl = msg.IndexOf('\n');
            LastOutcomeTimings = nl > 0 ? msg.Substring(nl + 1).Trim() : "";
            Log("  stages: " + LastOutcomeTimings.Replace("\n", "\n  "));
        };
    }

    private static void DeleteCacheEntries(string key)
    {
        if (!Directory.Exists(SongProfileCache.Root)) return;
        foreach (var f in Directory.GetFiles(SongProfileCache.Root, SongProfileCache.Sanitize(key) + "_*" + SongProfileCache.Extension)) File.Delete(f);
    }

    private static byte[] ReadCacheEntry(string key)
    {
        var f = SongProfileCache.FindAnyFor(key);
        return f != null ? File.ReadAllBytes(f) : null;
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static bool SameTags(MusicTagScore[] a, MusicTagScore[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i].tag != b[i].tag || a[i].score != b[i].score) return false;
        return true;
    }

    private static bool Monotonic(List<float> v)
    {
        for (int i = 1; i < v.Count; i++) if (v[i] < v[i - 1]) return false;
        return true;
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"  [{(ok ? "PASS" : "FAIL")}] {name}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}");
    }

    public static IEnumerator EnsureLoaded(AudioClip clip)
    {
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        while (clip.loadState == AudioDataLoadState.Loading || clip.loadState == AudioDataLoadState.Unloaded) yield return null;
    }

    public static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):F1} MB";

    public static void Log(string s) { s_log.AppendLine(s); Debug.Log("[AnalysisBench] " + s); }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        Log($"\nRESULT: {s_pass} checks passed, {s_fail} failed (exit {code})");
        try
        {
            string outPath = SessionState.GetString(KeyOut, Path.GetFullPath("Logs/AnalysisBenchmark.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, s_log.ToString());
        }
        catch (Exception ex) { Debug.LogError("[AnalysisBench] could not write report: " + ex.Message); }
        EditorApplication.Exit(code);
    }

    public class BenchRunner : MonoBehaviour
    {
        public double maxFrameMs;
        public int frames;
        public string slowFrames = "";
        private double _last = -1;
        private string _lastStage = "";
        private int _slowCount;
        public void ResetFrames() { maxFrameMs = 0; frames = 0; _last = -1; slowFrames = ""; _slowCount = 0; }
        private void Update()
        {
            double now = BenchMetrics.Clock;
            var pre = GetComponent<AudioPreAnalyzer>();
            string stage = pre != null ? pre.CurrentStage.ToString() : "-";
            if (_last >= 0)
            {
                double ms = (now - _last) * 1000.0;
                maxFrameMs = Math.Max(maxFrameMs, ms);
                // Frames > 50 ms with the pipeline stage before → after (where the main thread stalled).
                if (ms > 50 && _slowCount++ < 6) slowFrames += $" [{ms:F0} ms {_lastStage}→{stage}]";
            }
            _last = now;
            _lastStage = stage;
            frames++;
        }
    }

    /// <summary>Memory / frame instrumentation of one measured window.</summary>
    public sealed class BenchMetrics
    {
        private static readonly System.Diagnostics.Stopwatch s_clock = System.Diagnostics.Stopwatch.StartNew();
        public static double Clock => s_clock.Elapsed.TotalSeconds;

        private BenchRunner _runner;
        private long _heapStart, _usedStart, _nativeStart, _peakUsed, _peakNative, _gcAlloc;
        private int _gcCount0;
        private double _t0;
        private volatile bool _sampling;
        private Thread _sampler;
        private ProfilerRecorder _allocRec;
        public double Now => Clock;

        public static BenchMetrics Begin(BenchRunner runner)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var m = new BenchMetrics { _runner = runner };
            m._heapStart   = Profiler.GetMonoHeapSizeLong();
            m._usedStart   = GC.GetTotalMemory(false);
            m._nativeStart = Profiler.GetTotalAllocatedMemoryLong();
            m._peakUsed    = m._usedStart;
            m._peakNative  = m._nativeStart;
            m._gcCount0    = GC.CollectionCount(0);
            m._allocRec    = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            m._sampling    = true;
            m._sampler = new Thread(() =>
            {
                while (m._sampling)
                {
                    long u = GC.GetTotalMemory(false);
                    if (u > Interlocked.Read(ref m._peakUsed)) Interlocked.Exchange(ref m._peakUsed, u);
                    Thread.Sleep(1);
                }
            }) { IsBackground = true, Name = "BenchMemSampler" };
            m._sampler.Start();
            runner.ResetFrames();
            m._t0 = Clock;
            runner.StartCoroutine(m.SampleFrames());
            return m;
        }

        private IEnumerator SampleFrames()
        {
            while (_sampling)
            {
                _peakNative = Math.Max(_peakNative, Profiler.GetTotalAllocatedMemoryLong());
                yield return null;
                if (_allocRec.Valid) _gcAlloc += _allocRec.LastValue;
            }
        }

        public Result End()
        {
            _sampling = false;
            _sampler.Join();
            if (_allocRec.Valid) _gcAlloc += _allocRec.LastValue;
            var r = new Result
            {
                wallMs          = (Clock - _t0) * 1000.0,
                maxFrameMs      = _runner.maxFrameMs,
                slowFrames      = _runner.slowFrames,
                frames          = _runner.frames,
                allocated       = _allocRec.Valid ? _gcAlloc : -1,
                gcCollections   = GC.CollectionCount(0) - _gcCount0,
                peakUsedDelta   = Interlocked.Read(ref _peakUsed) - _usedStart,
                heapGrowth      = Profiler.GetMonoHeapSizeLong() - _heapStart,
                peakNativeDelta = Math.Max(_peakNative, Profiler.GetTotalAllocatedMemoryLong()) - _nativeStart,
            };
            _allocRec.Dispose();
            return r;
        }

        public struct Result
        {
            public double wallMs, maxFrameMs;
            public string slowFrames;
            public int frames, gcCollections;
            public long allocated, peakUsedDelta, heapGrowth, peakNativeDelta;
            public override string ToString() =>
                $"window {wallMs:F0} ms, {frames} frames, max main-thread frame {maxFrameMs:F1} ms | GC allocated (profiler counter) {(allocated >= 0 ? Mb(allocated) : "n/a")}, {gcCollections} GCs | " +
                $"peak GC heap use +{Mb(peakUsedDelta)} | Mono heap growth +{Mb(heapGrowth)} | peak native (Unity allocators, per frame) +{Mb(peakNativeDelta)}" +
                (string.IsNullOrEmpty(slowFrames) ? "" : "\n    frames > 50 ms:" + slowFrames);
        }
    }
}
#endif
