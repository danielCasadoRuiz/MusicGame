using System;
using System.Collections.Generic;
using System.IO;
using MusicGame.Audio.Analysis;
using UnityEngine;

/// <summary>
/// RAW analysis (core AnalysisResult + semantic tags) → the SongProfile runtime systems read.
/// The profile takes ownership of the raw arrays (callers pass a result nobody else mutates: a fresh
/// analysis or a fresh binary read), then <see cref="SongProfileDerived.Compute"/> adds the derived
/// fields. Pure C#, safe on a worker thread.
///
/// Mapping to the existing gameplay input (P2B; P4 consumes the events directly):
///   onsetTimes      = every detected event's calibrated attack time, ascending (one entry per
///                     OnsetEvent, all families — nothing filtered here; GameplayTimeline keeps
///                     classifying each time from the frame features exactly as before);
///   onsetEvents     = the multi-label events themselves (families + heuristic confidences);
///   estimatedBPM    = the core tempo, NOT rounded, 0 when there is no tempo evidence (no more
///                     arbitrary 120); beatTimes / beatPhase / bpmConfidence added;
///   frame arrays    = centre-aligned frames (frame f centred on f·hop) — floor(t / hop) lookups
///                     return the frame centred at most one hop before t;
///   voiceProbability= RAW (the smoothed copy is derived: voiceProbabilitySmoothed).
/// </summary>
public static class SongProfileFactory
{
    public static SongProfile Create(AnalysisResult r, MusicTagScore[] tags, int tagModelVersion, SongAudioIdentity identity,
                                     ulong paramsHash, DerivedAnalysisSettings derived)
    {
        if (r == null) throw new ArgumentNullException(nameof(r));
        var onsets = r.onsets ?? Array.Empty<OnsetEvent>();
        if (!IsAscending(onsets))
        {
            onsets = (OnsetEvent[])onsets.Clone();
            Array.Sort(onsets, (a, b) => a.time.CompareTo(b.time));
        }
        var times = new float[onsets.Length];
        for (int i = 0; i < onsets.Length; i++) times[i] = onsets[i].time;

        var segs = new SongSegment[r.segments?.Length ?? 0];
        for (int i = 0; i < segs.Length; i++)
        {
            var s = r.segments[i];
            segs[i] = new SongSegment { startTime = s.startTime, endTime = s.endTime, averageEnergy = s.averageEnergy, maxEnergy = s.maxEnergy, level = (EnergyLevel)(int)s.level };
        }

        var p = new SongProfile
        {
            duration            = r.duration,
            sampleRate          = r.sampleRate,
            estimatedBPM        = r.estimatedBpm,
            averageEnergy       = r.averageEnergy,
            maxEnergy           = r.maxEnergy,
            analysisHopTime     = r.hopTime,
            energyEnvelope      = r.energy,
            spectralCentroid    = r.centroid,
            spectralFlux        = r.flux,
            onsetTimes          = times,
            bandEnvelopes       = r.bands,
            visualBandEnvelopes = r.visualBands,
            segments            = segs,
            spectralFlatness    = r.flatness,
            chromaFlat          = r.chroma,
            voiceProbability    = r.voice,
            onsetEvents         = onsets,
            beatTimes           = r.beatTimes ?? Array.Empty<float>(),
            beatPhase           = r.beatPhase,
            bpmConfidence       = r.bpmConfidence,
            estimatedKey        = -1, // HarmonyAnalyzer (derived)
            musicTags           = tags,
            musicTagModelVersion= tags != null ? tagModelVersion : 0,
            songKey             = identity.SongKey,
            audioIdentity       = identity.Hash,
            paramsHash          = paramsHash,
            pipelineVersion     = AnalysisVersions.Pipeline,
        };
        SongProfileDerived.Compute(p, derived);
        return p;
    }

    private static bool IsAscending(OnsetEvent[] e)
    {
        for (int i = 1; i < e.Length; i++) if (e[i].time < e[i - 1].time) return false;
        return true;
    }
}

/// <summary>
/// The DERIVED pass (loudness, timbre, dynamics, structure, harmony, voice smoothing/zones,
/// similarity): reads a profile's raw arrays, writes only derived fields (each a new array). Runs on
/// every build and every load (cache hit, published profile), so it never compounds (no double
/// smoothing) and a derived-analyzer change needs no re-analysis. Pure C#; any thread.
/// </summary>
public static class SongProfileDerived
{
    public static void Compute(SongProfile profile, DerivedAnalysisSettings s)
    {
        if (profile == null || s == null || !s.advancedEnabled) return;

        var ctx = new OfflineAnalysisContext
        {
            Config           = s,
            SampleRate       = profile.sampleRate,
            HopTime          = profile.analysisHopTime,
            Duration         = profile.duration,
            NumFrames        = profile.energyEnvelope?.Length ?? 0,
            AvgEnergy        = profile.averageEnergy,
            MaxEnergy        = profile.maxEnergy,
            EstimatedBPM     = profile.estimatedBPM,
            EnergyEnvelope   = profile.energyEnvelope,
            SpectralCentroid = profile.spectralCentroid,
            SpectralFlux     = profile.spectralFlux,
            OnsetTimes       = profile.onsetTimes,
            BandEnvelopes    = profile.bandEnvelopes,
            SpectralFlatness = profile.spectralFlatness,
            ChromaFlat       = profile.chromaFlat,
            VoiceProb        = profile.voiceProbability, // RAW — VoiceAnalyzer writes voiceProbabilitySmoothed
        };

        // Order matters: TimbralAnalyzer must run before DynamicsAnalyzer (writes intensity)
        IOfflineAnalyzer[] analyzers =
        {
            new LoudnessAnalyzer(),
            new TimbralAnalyzer(),    // writes intensity → needed by DynamicsAnalyzer
            new DynamicsAnalyzer(),   // reads intensity
            new StructureAnalyzer(),  // reads intensity, timbralChange
            new HarmonyAnalyzer(),
            new VoiceAnalyzer(),
            new SimilarityAnalyzer(),
        };
        foreach (var a in analyzers)
            if (a.IsEnabled(s))
                a.Analyze(ctx, profile);
    }

    /// <summary>MAIN THREAD convenience (editor tools, legacy JSON profiles): snapshot + compute.</summary>
    public static void Compute(SongProfile profile, AudioAnalysisConfig config)
    {
        if (profile != null && config != null) Compute(profile, new DerivedAnalysisSettings(config));
    }
}

/// <summary>
/// The persisted profile: ProfileBinary ("MGSP", versioned, CRC-checked) holding the RAW analysis
/// + tags, with a header binding it to the audio revision and the analysis that produced it:
///   formatVersion (ProfileBinary.FormatVersion) · pipelineVersion (AnalysisVersions.Pipeline) ·
///   paramsHash (AnalysisParams.Hash) · tagModelVersion (SentisMusicTagger.ModelVersion, 0 = no tags) ·
///   tagReuseKey (audio + model + preprocessing + filterbank + topN/minScore) · songId (= song key) ·
///   audioIdentity (SongAudioIdentity.Hash) · clipSamples / clipChannels / clipFrequency.
/// </summary>
public static class SongProfileSerializer
{
    public static ProfileHeader MakeHeader(SongAudioIdentity id, SongAnalysisSnapshot snap, int samples, int channels, int frequency, bool hasTags)
        => new ProfileHeader
        {
            pipelineVersion = AnalysisVersions.Pipeline,
            paramsHash      = snap.Params.Hash,
            tagModelVersion = hasTags ? snap.TagModelVersion : 0,
            tagReuseKey     = hasTags ? snap.TagReuseKey(id.Hash) : 0UL,
            songId          = id.SongKey ?? "",
            audioIdentity   = id.Hash,
            clipSamples     = samples,
            clipChannels    = channels,
            clipFrequency   = frequency,
        };

    public static byte[] Write(ProfileHeader header, AnalysisResult raw, MusicTagScore[] tags)
        => ProfileBinary.Write(header, raw, SentisMusicTagger.ToCore(tags));

    /// <summary>Full read (CRC verified). Every array is fresh: the caller owns it.</summary>
    public static AnalysisResult Read(byte[] bytes, out ProfileHeader header, out MusicTagScore[] tags)
    {
        var r = ProfileBinary.Read(bytes, out header, out var coreTags, verifyCrc: true);
        tags = header.HasTags ? SentisMusicTagger.FromCore(coreTags) : null;
        return r;
    }

    /// <summary>The raw analysis can be reused for this audio under these params.</summary>
    public static bool RhythmCompatible(ProfileHeader h, SongAnalysisSnapshot snap, SongAudioIdentity id, int samples, int frequency)
        => h.IsCompatible(snap.Params, id.Hash, samples, frequency);

    /// <summary>The stored tags can be reused (same audio, model, preprocessing, selection settings).</summary>
    public static bool TagsCompatible(ProfileHeader h, SongAnalysisSnapshot snap, SongAudioIdentity id)
        => snap.TaggingEnabled && h.HasTags && h.tagModelVersion == snap.TagModelVersion && h.tagReuseKey == snap.TagReuseKey(id.Hash);
}

/// <summary>
/// Local analysis cache of DYNAMICALLY analysed songs (imports, and catalog songs until P3 publishes
/// their profiles): persistentDataPath/SongProfileCache/&lt;songKey&gt;_&lt;audioIdentity&gt;.mgsp.
/// A dev/user cache, never a source of truth. Writing a new revision of a song removes that song's
/// older entries (same key prefix). The legacy JSON SongCache folder is left untouched.
/// File access is plain System.IO: call from a worker thread.
/// </summary>
public static class SongProfileCache
{
    public const string FolderName = "SongProfileCache";
    public const string Extension  = ".mgsp";

    private static string s_root;
    /// <summary>MAIN THREAD once (Application.persistentDataPath), cached for workers.</summary>
    public static string Root => s_root ??= Path.Combine(Application.persistentDataPath, FolderName);
    public static void InitOnMainThread() { _ = Root; }

    public static string PathFor(SongAudioIdentity id) => Path.Combine(Root, $"{Sanitize(id.SongKey)}_{id.Hash:x16}{Extension}");

    public static string Sanitize(string key)
    {
        if (string.IsNullOrEmpty(key)) return "song";
        var chars = key.ToCharArray();
        var bad = Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++)
            if (Array.IndexOf(bad, chars[i]) >= 0) chars[i] = '-';
        return new string(chars);
    }

    /// <summary>WORKER. Null when absent or unreadable/corrupt (a corrupt entry is ignored, then
    /// overwritten by the next save).</summary>
    public static byte[] TryReadBytes(SongAudioIdentity id)
    {
        string path = PathFor(id);
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (Exception e) { Debug.LogWarning($"[SongProfileCache] Read failed ({path}): {e.Message}"); return null; }
    }

    /// <summary>WORKER. Atomic-ish write (temp + replace), then removes older revisions of the same song.</summary>
    public static void Save(SongAudioIdentity id, byte[] bytes)
    {
        string path = PathFor(id);
        try
        {
            Directory.CreateDirectory(Root);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            string prefix = Sanitize(id.SongKey) + "_";
            if (id.Kind != SongAudioIdentityKind.ImportedFile)
                foreach (var f in Directory.GetFiles(Root, prefix + "*" + Extension))
                {
                    string rest = Path.GetFileNameWithoutExtension(f).Substring(prefix.Length);
                    if (!string.Equals(f, path, StringComparison.OrdinalIgnoreCase) && rest.Length == 16) File.Delete(f);
                }
        }
        catch (Exception e) { Debug.LogWarning($"[SongProfileCache] Save failed ({path}): {e.Message}"); }
    }

    /// <summary>WORKER. Newest cache entry of a song key regardless of audio revision (similarity
    /// fallback only — it cannot verify the audio without decoding it).</summary>
    public static string FindAnyFor(string songKey)
    {
        try
        {
            if (!Directory.Exists(Root)) return null;
            string prefix = Sanitize(songKey) + "_";
            string best = null; DateTime bestTime = DateTime.MinValue;
            foreach (var f in Directory.GetFiles(Root, prefix + "*" + Extension))
            {
                if (Path.GetFileNameWithoutExtension(f).Length != prefix.Length + 16) continue;
                var t = File.GetLastWriteTimeUtc(f);
                if (t > bestTime) { best = f; bestTime = t; }
            }
            return best;
        }
        catch { return null; }
    }
}
