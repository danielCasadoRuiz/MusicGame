#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// P3 measurement of the PLAYBACK representation: Unity's own import of the lossless master WAV (no MP3
/// intermediate, no double lossy compression, the masters untouched) compared per compression format × load
/// type on a SMALL subset of real catalog songs. Two batch runs:
///
/// 1. PREPARE (edit mode) — -executeMethod AudioImportComparison.PrepareFromCommandLine -fmtOut DIR [-songs ids]
///    For each song a TEMPORARY copy of its master is imported under each variant (the original asset and its
///    .meta are never touched), and per variant an AssetBundle is built exactly like Addressables does
///    (LZ4 chunk compression, active target) → REAL bundle size. For DecompressOnLoad variants the decoded clip
///    is compared to the analysed master: quality (SNR, gain) and ALIGNMENT at start / middle / end
///    (PlaybackAlignment). Also: decode of the temp copy == decode of the real asset (encoder determinism,
///    which the bake's temp-copy alignment check relies on). Temp assets are deleted at the end of the run.
/// 2. RUNTIME (play mode) — -executeMethod AudioImportComparison.RuntimeFromCommandLine -fmtOut DIR
///    Loads every built bundle: load + prepare time, clip runtime memory and native memory delta, audio CPU
///    while playing (ProfilerRecorder "Audio" counters when available, else main-thread frame time), start
///    latency, seek to the middle and near the end (main-thread cost, time until the position advances,
///    position error), natural end, and memory released after unloading.
/// The bundle directory is the caller's to delete after reading the reports.
/// </summary>
[InitializeOnLoad]
public static class AudioImportComparison
{
    private const string TempFolder = "Assets/__P3FormatTemp";
    private const string KeyActive = "AudioImportComparison.Active";
    private const string KeyOut = "AudioImportComparison.Out";

    private struct Variant
    {
        public string Name; public AudioCompressionFormat Format; public float Quality; public AudioClipLoadType Load;
        public Variant(string n, AudioCompressionFormat f, float q, AudioClipLoadType l) { Name = n; Format = f; Quality = q; Load = l; }
    }

    private static readonly Variant[] Variants = BuildVariants();

    private static Variant[] BuildVariants()
    {
        var v = new List<Variant>();
        foreach (var (fmt, q, tag) in new[] { (AudioCompressionFormat.Vorbis, 1f, "vorbis100"), (AudioCompressionFormat.ADPCM, 1f, "adpcm"), (AudioCompressionFormat.PCM, 1f, "pcm") })
            foreach (var (load, ltag) in new[] { (AudioClipLoadType.DecompressOnLoad, "dol"), (AudioClipLoadType.CompressedInMemory, "cim"), (AudioClipLoadType.Streaming, "str") })
                v.Add(new Variant($"{tag}_{ltag}", fmt, q, load));
        v.Add(new Variant("vorbis70_cim", AudioCompressionFormat.Vorbis, 0.7f, AudioClipLoadType.CompressedInMemory)); // size/quality data point
        v.Add(new Variant("vorbis70_dol", AudioCompressionFormat.Vorbis, 0.7f, AudioClipLoadType.DecompressOnLoad));
        return v.ToArray();
    }

    // ── 1. PREPARE ───────────────────────────────────────────────────────────────

    public static void PrepareFromCommandLine()
    {
        string outDir = ContentUpdateTools.Arg("-fmtOut") ?? Path.GetFullPath("Temp/P3Formats");
        string songs = ContentUpdateTools.Arg("-songs") ?? "song_008,song_016,song_005";
        int code = 0;
        var sb = new StringBuilder($"[AudioImportComparison] PREPARE {DateTime.Now:yyyy-MM-dd HH:mm:ss} target {EditorUserBuildSettings.activeBuildTarget}\n");
        sb.AppendLine("song\tvariant\tbundleBytes\timportMs\tdecodeOk\tframes\trate\tch\tlagStart\tlagMid\tlagEnd\tgainDb\tsnrDb\talignError");
        try { Prepare(outDir, songs.Split(','), sb); }
        catch (Exception e) { sb.AppendLine("EXCEPTION " + e); code = 2; }
        finally
        {
            AssetDatabase.DeleteAsset(TempFolder);
            AssetDatabase.Refresh();
        }
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "prepare_report.tsv"), sb.ToString(), Encoding.UTF8);
        Debug.Log(sb.ToString());
        EditorApplication.Exit(code);
    }

    private static void Prepare(string outDir, string[] songIds, StringBuilder sb)
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        Directory.CreateDirectory(outDir);
        if (!AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.CreateFolder("Assets", "__P3FormatTemp");
        var target = EditorUserBuildSettings.activeBuildTarget;
        foreach (var id in songIds)
        {
            var entry = app.songCatalog.songs.First(s => s.songId == id);
            string master = AssetDatabase.GUIDToAssetPath(entry.editorGuid);
            var origImporter = (AudioImporter)AssetImporter.GetAtPath(master);
            bool normalizes = MasterAudio.Normalizes(origImporter);
            var pcm = MasterAudio.ReadWav(master);
            float gain = MasterAudio.AnalysisGain(pcm, MasterAudio.ImportNormalizesLevel(origImporter));
            var masterMono = MasterAudio.AnalysisMono(pcm, gain);
            sb.AppendLine($"# {id} '{entry.address}': master {new FileInfo(master).Length} bytes, {pcm.Frames} frames × {pcm.Channels} @ {pcm.SampleRate} Hz, peak {pcm.Peak:F4}, gain {gain:F4}");

            // Encoder determinism: the real asset (currently DecompressOnLoad) vs a temp copy with the same settings.
            float[] realDecode = null;
            if (origImporter.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad)
            {
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>(master);
                realDecode = DecodeMono(c);
            }

            string temp = $"{TempFolder}/{id}.wav";
            File.Copy(master, temp, true);
            AssetDatabase.ImportAsset(temp, ImportAssetOptions.ForceSynchronousImport);
            foreach (var v in Variants)
            {
                var ti = (AudioImporter)AssetImporter.GetAtPath(temp);
                var so = new SerializedObject(ti); so.FindProperty("m_Normalize").boolValue = normalizes; so.ApplyModifiedPropertiesWithoutUndo();
                ti.forceToMono = origImporter.forceToMono;
                var s = origImporter.defaultSampleSettings;
                s.compressionFormat = v.Format; s.quality = v.Quality; s.loadType = v.Load; s.preloadAudioData = false;
                ti.defaultSampleSettings = s;
                var sw = Stopwatch.StartNew();
                ti.SaveAndReimport();
                double importMs = sw.Elapsed.TotalMilliseconds;

                string bundleName = $"{id}_{v.Name}";
                string vdir = Path.Combine(outDir, bundleName + "_out"); // ≠ bundle name (the folder names the manifest bundle)
                Directory.CreateDirectory(vdir);
                BuildPipeline.BuildAssetBundles(vdir, new[] { new AssetBundleBuild { assetBundleName = bundleName, assetNames = new[] { temp } } },
                                                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
                long size = new FileInfo(Path.Combine(vdir, bundleName)).Length;

                string row = $"{id}\t{v.Name}\t{size}\t{importMs:F0}";
                if (v.Load == AudioClipLoadType.DecompressOnLoad)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(temp);
                    var mono = DecodeMono(clip);
                    var a = PlaybackAlignment.Measure(masterMono, pcm.SampleRate, mono, clip.frequency);
                    row += $"\tyes\t{clip.samples}\t{clip.frequency}\t{clip.channels}\t{a.Start.LagSamples}\t{a.Middle.LagSamples}\t{a.End.LagSamples}\t{a.GainRatioDb:F3}\t{a.SnrDb:F1}\t{a.Error ?? "-"}";
                    if (realDecode != null && v.Name == "vorbis100_dol")
                    {
                        double maxDiff = 0; int n = Math.Min(realDecode.Length, mono.Length);
                        for (int i = 0; i < n; i++) maxDiff = Math.Max(maxDiff, Math.Abs(realDecode[i] - mono[i]));
                        sb.AppendLine($"# {id} determinism: temp-copy Vorbis decode vs real asset decode: lengths {mono.Length}/{realDecode.Length}, max |diff| {maxDiff:G4}");
                    }
                    if (v.Name == "vorbis100_dol") sb.AppendLine($"# {id} alignment (current import): {a}");
                }
                else row += "\tn/a (GetData needs DecompressOnLoad: same encoded data as the _dol variant)";
                sb.AppendLine(row);
            }
            AssetDatabase.DeleteAsset(temp);
        }
    }

    private static float[] DecodeMono(AudioClip clip)
    {
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        var t0 = DateTime.UtcNow;
        while (clip.loadState == AudioDataLoadState.Loading && (DateTime.UtcNow - t0).TotalSeconds < 60) System.Threading.Thread.Sleep(5);
        var inter = new float[clip.samples * clip.channels];
        if (!clip.GetData(inter, 0)) throw new InvalidOperationException("GetData failed: " + clip.name);
        var mono = new float[clip.samples];
        for (int i = 0; i < mono.Length; i++) { float s = 0; for (int c = 0; c < clip.channels; c++) s += inter[i * clip.channels + c]; mono[i] = s / clip.channels; }
        return mono;
    }

    // ── 2. RUNTIME (play mode) ───────────────────────────────────────────────────

    static AudioImportComparison()
    {
        if (SessionState.GetBool(KeyActive, false)) EditorApplication.update += Tick;
    }

    public static void RuntimeFromCommandLine()
    {
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, ContentUpdateTools.Arg("-fmtOut") ?? Path.GetFullPath("Temp/P3Formats"));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static IEnumerator<bool> s_routine;
    private static readonly StringBuilder s_rt = new();

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            s_routine ??= Runtime();
            if (!s_routine.MoveNext()) Finish(0);
        }
        catch (Exception e) { s_rt.AppendLine("EXCEPTION " + e); Finish(2); }
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        string dir = SessionState.GetString(KeyOut, "Temp/P3Formats");
        File.WriteAllText(Path.Combine(dir, "runtime_report.tsv"), s_rt.ToString(), Encoding.UTF8);
        Debug.Log(s_rt.ToString());
        EditorApplication.Exit(code);
    }

    // Yields once per frame (editor update while playing).
    private static IEnumerator<bool> Runtime()
    {
        string dir = SessionState.GetString(KeyOut, "Temp/P3Formats");
        s_rt.AppendLine($"[AudioImportComparison] RUNTIME {DateTime.Now:yyyy-MM-dd HH:mm:ss}, editor play mode, {AudioSettings.outputSampleRate} Hz, DSP {AudioSettings.GetConfiguration().dspBufferSize}");
        var audioStats = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(audioStats);
        var recorders = new List<(string, ProfilerRecorder)>();
        foreach (var h in audioStats)
        {
            var d = ProfilerRecorderHandle.GetDescription(h);
            if (d.Category == ProfilerCategory.Audio && (d.Name.Contains("CPU") || d.Name.Contains("Memory") || d.Name.Contains("Streaming")))
                recorders.Add((d.Name, ProfilerRecorder.StartNew(d.Category, d.Name)));
        }
        s_rt.AppendLine("# audio counters: " + (recorders.Count > 0 ? string.Join(", ", recorders.Select(r => r.Item1)) : "none available — CPU = main-thread frame time only"));
        s_rt.AppendLine("bundle\tbundleBytes\tloadMs\tprepareMs\tclipRuntimeMB\tnativeDeltaMB\tstartLatencyMs\tavgFrameMs\tseekCallMsMid\tseekSettleMsMid\tseekErrMsMid\tseekCallMsEnd\tseekSettleMsEnd\tnaturalEnd\tnativeAfterUnloadMB\taudioCounters");
        var go = new GameObject("FormatProbe");
        var src = go.AddComponent<AudioSource>();
        go.AddComponent<AudioListener>();
        for (int w = 0; w < 30; w++) yield return true; // settle

        foreach (var vdir in Directory.GetDirectories(dir).OrderBy(x => x))
        {
            string name = Path.GetFileName(vdir).Replace("_out", "");
            string file = Path.Combine(vdir, name);
            if (!File.Exists(file)) continue;
            GC.Collect();
            long native0 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            var sw = Stopwatch.StartNew();
            var req = AssetBundle.LoadFromFileAsync(file);
            while (!req.isDone) yield return true;
            var bundle = req.assetBundle;
            var areq = bundle.LoadAssetAsync<AudioClip>(bundle.GetAllAssetNames()[0]);
            while (!areq.isDone) yield return true;
            var clip = (AudioClip)areq.asset;
            double loadMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            while (clip.loadState == AudioDataLoadState.Loading) yield return true;
            double prepareMs = sw.Elapsed.TotalMilliseconds;
            long clipBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(clip);
            long nativeDelta = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() - native0;

            // Start latency + CPU while playing ~3 s.
            src.clip = clip; src.timeSamples = 0;
            sw.Restart();
            src.Play();
            while (src.timeSamples == 0 && sw.Elapsed.TotalSeconds < 5) yield return true;
            double startMs = sw.Elapsed.TotalMilliseconds;
            var sums = new double[recorders.Count]; int frames = 0; double frameSum = 0;
            var playT = Stopwatch.StartNew();
            while (playT.Elapsed.TotalSeconds < 3)
            {
                for (int i = 0; i < recorders.Count; i++) sums[i] += recorders[i].Item2.LastValue;
                frameSum += Time.unscaledDeltaTime; frames++;
                yield return true;
            }

            // Seek to the middle.
            var (callMid, settleMid, errMid) = default((double, double, double));
            {
                int target = clip.samples / 2;
                var c = Stopwatch.StartNew(); src.timeSamples = target; callMid = c.Elapsed.TotalMilliseconds;
                c.Restart();
                int first = src.timeSamples;
                while (src.timeSamples <= target + 1 && c.Elapsed.TotalSeconds < 5) yield return true;
                settleMid = c.Elapsed.TotalMilliseconds;
                // Position error: reported position minus where it should be given the elapsed real time.
                errMid = (src.timeSamples - target) * 1000.0 / clip.frequency - settleMid;
            }
            // Seek near the end and let it end naturally.
            double callEnd, settleEnd;
            {
                int target = Math.Max(0, clip.samples - clip.frequency * 2);
                var c = Stopwatch.StartNew(); src.timeSamples = target; callEnd = c.Elapsed.TotalMilliseconds;
                c.Restart();
                while (src.timeSamples <= target + 1 && c.Elapsed.TotalSeconds < 5) yield return true;
                settleEnd = c.Elapsed.TotalMilliseconds;
            }
            var endT = Stopwatch.StartNew();
            while (src.isPlaying && endT.Elapsed.TotalSeconds < 6) yield return true;
            bool natural = !src.isPlaying && endT.Elapsed.TotalSeconds < 6;

            src.Stop(); src.clip = null;
            bundle.Unload(true);
            for (int w = 0; w < 5; w++) yield return true;
            GC.Collect();
            long nativeAfter = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() - native0;
            string counters = string.Join(" ", recorders.Select((r, i) => $"{r.Item1}={(frames > 0 ? sums[i] / frames : 0):G4}"));
            s_rt.AppendLine($"{name}\t{new FileInfo(file).Length}\t{loadMs:F1}\t{prepareMs:F1}\t{clipBytes / 1048576.0:F2}\t{nativeDelta / 1048576.0:F2}\t{startMs:F1}\t{(frames > 0 ? frameSum / frames * 1000 : 0):F2}\t" +
                            $"{callMid:F3}\t{settleMid:F1}\t{errMid:F1}\t{callEnd:F3}\t{settleEnd:F1}\t{natural}\t{nativeAfter / 1048576.0:F2}\t{counters}");
        }
        foreach (var r in recorders) r.Item2.Dispose();
    }
}
#endif
