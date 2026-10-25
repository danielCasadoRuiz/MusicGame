#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MusicGame.Audio.Analysis;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Tools > MusicGame > Content > Songs > Bake Published Profiles (missing/stale) / (Force Rebuild All).
/// Pre-analyses EVERY playable catalog song with the validated pipeline (SongAnalysisPipeline.AnalyzePcmBlocking —
/// the same job the runtime runs for imports) from its LOSSLESS MASTER, and writes versionable project assets:
///   Assets/_Project/Content/SongProfiles/&lt;songId&gt;.mgsp.bytes  — the binary profile (raw analysis + tags),
///       header bound to: master identity (bytes + normalisation policy), analysis versions/params/tag model,
///       playback representation revision (import settings of the played clip, MasterAudio.PlaybackRevision),
///       the played clip's frames/channels/rate, the measured master→playback alignment and its conversion;
///   Assets/_Project/Content/SongCatalogIndex.asset — the remote index (identity, order, revisions, similarity);
///   Configs/Song/SongSimilarityCatalog.asset   — the player-shipped similarity fallback, same vectors.
/// Never reads the developer's personal analysis cache.
///
/// Per song, cheapest valid path (unless forced):
///   REUSED   — header already current for this master + analysis + playback revision;
///   REBOUND  — analysis current but the playback representation changed (import settings / editor): the
///              alignment is MEASURED again and only the header binding is rewritten (raw + tags kept);
///   ANALYSED — full analysis from the master.
/// Alignment (PlaybackAlignment): start/middle/end lag, length, gain, SNR. Any non-zero or non-constant lag,
/// length change, resampling or gain mismatch FAILS the song (nothing written): there is no arbitrary offset.
/// Progress bar + cancel (interactive); report of reused / rebound / analysed / failed. A failed song keeps
/// no index entry, so SongContentValidator blocks publishing incomplete content.
/// Batch: -executeMethod SongCatalogBake.BakeFromCommandLine [-force] [-songs song_001,song_008] [-bakeReport path]
/// </summary>
public static class SongCatalogBake
{
    public const float MaxGainMismatchDb = 0.5f;
    private const string SimilarityAssetPath = "Assets/_Project/Configs/Song/SongSimilarityCatalog.asset";

    [MenuItem("Tools/MusicGame/Content/Songs/Bake Published Profiles (missing or stale)")]
    public static void BakeMenu() => Debug.Log(Bake(force: false, only: null, out _));

    [MenuItem("Tools/MusicGame/Content/Songs/Bake Published Profiles (Force Rebuild All)")]
    public static void BakeForceMenu() => Debug.Log(Bake(force: true, only: null, out _));

    public static void BakeFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        bool force = args.Contains("-force");
        string songs = ContentUpdateTools.Arg("-songs");
        string report = Bake(force, string.IsNullOrEmpty(songs) ? null : new HashSet<string>(songs.Split(',')), out bool ok);
        string outPath = ContentUpdateTools.Arg("-bakeReport") ?? "Logs/SongCatalogBake.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        File.WriteAllText(outPath, report, Encoding.UTF8);
        Debug.Log(report);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private sealed class SongResult
    {
        public string songId, address, assetPath, status, error;
        public ProfileHeader header;
        public byte[] bytes;
        public double ms;
        public PlaybackAlignment.Result alignment;
    }

    public static string Bake(bool force, HashSet<string> only, out bool ok)
    {
        var sb = new StringBuilder($"[SongCatalogBake] {(force ? "FORCE " : "")}bake — {DateTime.Now:yyyy-MM-dd HH:mm:ss}, target {EditorUserBuildSettings.activeBuildTarget}\n");
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var config = app != null ? app.audioAnalysis : null;
        var catalog = app != null ? app.songCatalog : null;
        if (config == null || catalog == null) { ok = false; return sb.AppendLine("  FAIL  AppConfig.audioAnalysis / songCatalog missing").ToString(); }

        var snapshot = SongAnalysisSnapshot.Create(config);
        if (!snapshot.TaggingEnabled) { ok = false; return sb.AppendLine("  FAIL  semantic tagging unavailable — published profiles must carry the style tags").ToString(); }
        sb.AppendLine($"  pipeline {AnalysisVersions.Pipeline}, params {snapshot.Params.Hash:x16}, format {ProfileBinary.FormatVersion}, tag model {snapshot.TagModelVersion}, workers {snapshot.WorkerBudget}");
        if (!AssetDatabase.IsValidFolder(SongContentKeys.ContentFolder)) AssetDatabase.CreateFolder("Assets/_Project", "Content");
        if (!AssetDatabase.IsValidFolder(SongContentKeys.ProfileFolder)) AssetDatabase.CreateFolder(SongContentKeys.ContentFolder, "SongProfiles");

        var results = new List<SongResult>();
        using var cts = new CancellationTokenSource();
        var target = EditorUserBuildSettings.activeBuildTarget;
        var entries = catalog.songs.Where(e => e != null && !string.IsNullOrEmpty(e.songId)).OrderBy(e => e.songId, StringComparer.Ordinal).ToList();
        try
        {
            for (int i = 0; i < entries.Count && !cts.IsCancellationRequested; i++)
            {
                var e = entries[i];
                if (only != null && !only.Contains(e.songId)) continue;
                var r = new SongResult { songId = e.songId, address = e.address, assetPath = AssetDatabase.GUIDToAssetPath(e.editorGuid) };
                results.Add(r);
                float baseP = (float)i / entries.Count, span = 1f / entries.Count;
                Action<float> progress = p =>
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Bake Published Song Profiles", $"{e.songId}  {e.address}", baseP + span * Mathf.Clamp01(p))) cts.Cancel();
                };
                var sw = Stopwatch.StartNew();
                try { BakeSong(r, snapshot, target, force, progress, cts.Token); }
                catch (OperationCanceledException) { r.status = "CANCELLED"; r.error = "cancelled"; }
                catch (Exception ex) { r.status = "FAILED"; r.error = ex.Message; Debug.LogException(ex); }
                r.ms = sw.Elapsed.TotalMilliseconds;
                sb.AppendLine($"  {r.status,-9} {r.songId} '{r.address}' {r.ms / 1000:F1}s" + (r.error != null ? $" — {r.error}" : "") +
                              (r.alignment != null ? $"\n            alignment: {r.alignment}" : "") +
                              (r.header != null ? $"\n            master {r.header.audioIdentity:x16} playback {r.header.playbackRevision:x16} body {r.header.bodyCrc32:x8} {r.bytes?.Length / 1048576.0:F2} MB gain {r.header.analysisGain:F4}" : ""));
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        if (cts.IsCancellationRequested) { ok = false; sb.AppendLine("  CANCELLED — no index written (the previous index stays)"); return sb.ToString(); }

        // Index + similarity from every CURRENT published profile on disk (songs not re-baked in this run included).
        string indexReport = WriteIndex(catalog, config, target, out int indexed, out var missing);
        sb.Append(indexReport);
        int failed = results.Count(r => r.status == "FAILED");
        ok = failed == 0 && missing.Count == 0;
        sb.AppendLine($"  SUMMARY {results.Count(r => r.status == "REUSED")} reused, {results.Count(r => r.status == "REBOUND")} rebound, " +
                      $"{results.Count(r => r.status == "ANALYSED")} analysed, {failed} failed; index {indexed}/{entries.Count} songs" +
                      (missing.Count > 0 ? $"; NOT PUBLISHABLE: {string.Join(", ", missing)}" : "") + (ok ? "" : "\n  FAIL"));
        return sb.ToString();
    }

    private static void BakeSong(SongResult r, SongAnalysisSnapshot snap, BuildTarget target, bool force, Action<float> progress, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(r.assetPath) || !File.Exists(r.assetPath)) throw new FileNotFoundException("master not found (stale editorGuid?)", r.assetPath);
        var importer = AssetImporter.GetAtPath(r.assetPath) as AudioImporter;
        if (importer == null) throw new InvalidOperationException("not an audio asset");
        bool normalizes = MasterAudio.ImportNormalizesLevel(importer);
        var masterId = MasterAudio.Identity(r.songId, r.assetPath, normalizes);
        ulong playbackRev = MasterAudio.PlaybackRevision(masterId.Hash, importer, target);
        string profilePath = SongContentKeys.ProfileAssetPath(r.songId);

        // Existing published profile: reuse / rebind / analyse.
        byte[] existing = File.Exists(profilePath) ? File.ReadAllBytes(profilePath) : null;
        ProfileHeader old = null;
        if (existing != null) try { old = ProfileBinary.ReadHeader(existing, out _); } catch { old = null; }
        bool analysisCurrent = !force && old != null && old.formatVersion == ProfileBinary.FormatVersion
                               && old.pipelineVersion == AnalysisVersions.Pipeline && old.paramsHash == snap.Params.Hash
                               && old.audioIdentity == masterId.Hash && old.songId == r.songId
                               && SongProfileSerializer.TagsCompatible(old, snap, masterId);
        if (analysisCurrent && old.playbackRevision == playbackRev && old.timeConversion == ProfileTimeConversion.Identity)
        {
            r.status = "REUSED"; r.header = old; r.bytes = existing;
            return;
        }

        progress(0.02f);
        var master = MasterAudio.ReadWav(r.assetPath);
        float gain = MasterAudio.AnalysisGain(master, normalizes);
        var mono = MasterAudio.AnalysisMono(master, gain);
        ct.ThrowIfCancellationRequested();

        // Measure the playback representation against the analysed master (start / middle / end).
        progress(0.05f);
        var decoded = PlaybackAlignment.DecodePlaybackMono(r.assetPath, out int pbRate, out int pbChannels);
        var align = PlaybackAlignment.Measure(mono, master.SampleRate, decoded, pbRate);
        r.alignment = align;
        decoded = null;
        if (align.Error != null) throw new InvalidOperationException("alignment: " + align.Error);
        if (align.MaxAbsLag != 0) throw new InvalidOperationException($"playback lags the master by {align.Middle.LagSamples} samples — no conversion is applied by this app; fix the import, never an arbitrary offset");
        if (Math.Abs(align.GainRatioDb) > MaxGainMismatchDb) throw new InvalidOperationException($"normalisation policy mismatch: playback/analysis gain {align.GainRatioDb:+0.00;-0.00} dB");
        ct.ThrowIfCancellationRequested();

        AnalysisResult raw; MusicTagScore[] tags; ProfileHeader header;
        if (analysisCurrent)
        {
            raw = SongProfileSerializer.Read(existing, out header, out tags);
            r.status = "REBOUND";
        }
        else
        {
            var outcome = SongAnalysisPipeline.AnalyzePcmBlocking(mono, master.SampleRate, master.Channels, masterId, snap, ct,
                                                                  p => progress(0.1f + 0.85f * p), SongAnalysisCacheMode.None);
            if (outcome?.Raw == null) throw new InvalidOperationException("analysis produced no result");
            if (outcome.Tags == null || outcome.Tags.Length == 0) throw new InvalidOperationException("analysis produced no style tags");
            raw = outcome.Raw; tags = outcome.Tags; header = outcome.Header;
            r.status = "ANALYSED";
        }

        header.playbackRevision      = playbackRev;
        header.playbackSamples       = align.PlaybackFrames; // the played clip's own frame count (runtime check)
        header.playbackChannels      = pbChannels;
        header.playbackFrequency     = pbRate;
        header.analysisGain          = gain;
        header.timeConversion        = ProfileTimeConversion.Identity;
        header.playbackOffsetSeconds = 0f;
        header.alignmentMaxErrorMs   = (float)align.MaxAbsLagMs;
        header.playbackPlatform      = target.ToString();
        var bytes = SongProfileSerializer.Write(header, raw, tags);
        File.WriteAllBytes(profilePath, bytes);
        AssetDatabase.ImportAsset(profilePath, ImportAssetOptions.ForceSynchronousImport);
        r.header = header; r.bytes = bytes;
        progress(1f);
    }

    // ── Index + similarity ───────────────────────────────────────────────────────

    private static string WriteIndex(PlayableSongCatalogSO catalog, AudioAnalysisConfig config, BuildTarget target, out int indexed, out List<string> missing)
    {
        var sb = new StringBuilder();
        var index = AssetDatabase.LoadAssetAtPath<SongCatalogIndexSO>(SongContentKeys.IndexAssetPath);
        if (index == null)
        {
            index = ScriptableObject.CreateInstance<SongCatalogIndexSO>();
            AssetDatabase.CreateAsset(index, SongContentKeys.IndexAssetPath);
        }
        var app = AppAnalysisVersions.Current(config);
        var derived = new DerivedAnalysisSettings(config);
        var list = new List<SongCatalogIndexEntry>();
        var vectors = new List<SongSimilarityVector>();
        missing = new List<string>();
        int order = 0;
        foreach (var e in catalog.songs.Where(x => x != null && !string.IsNullOrEmpty(x.songId)).OrderBy(x => x.songId, StringComparer.Ordinal))
        {
            order++;
            string assetPath = AssetDatabase.GUIDToAssetPath(e.editorGuid);
            string profilePath = SongContentKeys.ProfileAssetPath(e.songId);
            string problem = SongContentValidator.CheckProfileAgainstSource(e.songId, assetPath, profilePath, app, target, out var header, out var bytes);
            if (problem != null) { missing.Add($"{e.songId} ({problem})"); continue; }
            var importer = (AudioImporter)AssetImporter.GetAtPath(assetPath);
            var profile = SongAnalysisPipeline.LoadProfile(bytes, derived, out _);
            var vector = SongSimilarityVector.Build(e.songId, profile);
            if (vector != null)
            {
                vector.address = SongContentKeys.AudioAddress(e.songId);
                vector.sourceGuid = e.editorGuid;
                vector.sourceHash = $"{header.audioIdentity:x16}:{header.bodyCrc32:x8}";
                vectors.Add(vector);
            }
            list.Add(new SongCatalogIndexEntry
            {
                songId = e.songId, displayName = e.address, audioAddress = SongContentKeys.AudioAddress(e.songId),
                profileAddress = SongContentKeys.ProfileAddress(e.songId), contentLabel = SongContentKeys.ContentLabel(e.songId), order = order,
                masterIdentity = header.audioIdentity, playbackRevision = header.playbackRevision, profileCrc = header.bodyCrc32, profileBytes = bytes.Length,
                profileFormatVersion = header.formatVersion, pipelineVersion = header.pipelineVersion, paramsHash = header.paramsHash, tagModelVersion = header.tagModelVersion,
                durationSeconds = header.playbackFrequency > 0 ? header.playbackSamples / (float)header.playbackFrequency : 0f,
                playbackSamples = header.playbackSamples, playbackChannels = header.playbackChannels, playbackFrequency = header.playbackFrequency,
                playbackLoadType = MasterAudio.EffectiveSettings(importer, target).loadType.ToString(),
                similarity = vector,
            });
        }
        index.indexVersion = SongCatalogIndexSO.CurrentIndexVersion;
        index.songs = list;
        index.contentRevision = ContentRevision(list);
        EditorUtility.SetDirty(index);

        var sim = AssetDatabase.LoadAssetAtPath<SongSimilarityCatalogSO>(SimilarityAssetPath);
        if (sim != null) { sim.vectors = vectors; EditorUtility.SetDirty(sim); }
        AssetDatabase.SaveAssets();
        indexed = list.Count;
        sb.AppendLine($"  index  {SongContentKeys.IndexAssetPath}: {list.Count} songs, revision {index.contentRevision}; similarity vectors {vectors.Count} (also {SimilarityAssetPath})");
        return sb.ToString();
    }

    public static string ContentRevision(List<SongCatalogIndexEntry> list)
    {
        var h = StableHash.Create();
        h.Add("MusicGame.SongIndex");
        foreach (var e in list.OrderBy(x => x.songId, StringComparer.Ordinal))
        {
            h.Add(e.songId); h.Add(e.audioAddress); h.Add(e.masterIdentity); h.Add(e.playbackRevision); h.Add((long)e.profileCrc); h.Add(e.order);
        }
        return h.Value.ToString("x16");
    }
}
#endif
