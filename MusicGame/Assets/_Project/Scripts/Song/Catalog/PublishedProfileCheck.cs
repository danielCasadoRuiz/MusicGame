using MusicGame.Audio.Analysis;

/// <summary>
/// The analysis versions THIS app understands (taken on the main thread without touching Sentis: the
/// params hash comes from AudioAnalysisConfig alone).
/// </summary>
public readonly struct AppAnalysisVersions
{
    public readonly int   FormatVersion;
    public readonly int   PipelineVersion;
    public readonly ulong ParamsHash;
    public readonly int   TagModelVersion;

    public AppAnalysisVersions(int format, int pipeline, ulong paramsHash, int tagModel)
    { FormatVersion = format; PipelineVersion = pipeline; ParamsHash = paramsHash; TagModelVersion = tagModel; }

    /// <summary>MAIN THREAD (reads the config asset). No model load, no snapshot.</summary>
    public static AppAnalysisVersions Current(AudioAnalysisConfig config) => new(
        ProfileBinary.FormatVersion, AnalysisVersions.Pipeline,
        AnalysisParams.Create(SongAnalysisSnapshot.ToSettings(config)).Hash, SentisMusicTagger.ModelVersion);
}

/// <summary>
/// Pure compatibility rules of a PUBLISHED profile (runtime loader and editor validator share them).
/// Every method returns null when compatible, otherwise the reason — an explicit "incompatible content"
/// error, never a trigger for a silent re-analysis.
/// </summary>
public static class PublishedProfileCheck
{
    /// <summary>Header vs the index entry that published it vs this app's analysis versions.</summary>
    public static string Validate(ProfileHeader h, SongCatalogIndexEntry e, in AppAnalysisVersions app)
    {
        if (h == null) return "no profile header";
        if (e == null) return "song not in the published index";
        if (h.formatVersion != app.FormatVersion) return $"profile format {h.formatVersion} (app reads {app.FormatVersion})";
        if (h.pipelineVersion != app.PipelineVersion) return $"analysis pipeline {h.pipelineVersion} (app expects {app.PipelineVersion})";
        if (h.paramsHash != app.ParamsHash) return $"analysis params {h.paramsHash:x16} (app expects {app.ParamsHash:x16})";
        if (h.HasTags && h.tagModelVersion != app.TagModelVersion) return $"tag model {h.tagModelVersion} (app expects {app.TagModelVersion})";
        if (h.songId != e.songId) return $"profile is for '{h.songId}', index entry is '{e.songId}'";
        if (!h.HasPlaybackBinding) return "profile is not bound to a playback representation (not a published profile)";
        if (h.audioIdentity != e.masterIdentity) return $"master revision {h.audioIdentity:x16} ≠ index {e.masterIdentity:x16}";
        if (h.playbackRevision != e.playbackRevision) return $"playback revision {h.playbackRevision:x16} ≠ index {e.playbackRevision:x16}";
        if (h.bodyCrc32 != e.profileCrc) return $"profile body {h.bodyCrc32:x8} ≠ index {e.profileCrc:x8}";
        // Only the identity mapping is consumed by this app version: the bake refuses to publish a profile
        // whose measured master→playback lag is non-zero, so an offset can never be silently ignored here.
        if (h.timeConversion != ProfileTimeConversion.Identity)
            return $"time conversion {h.timeConversion} (offset {h.playbackOffsetSeconds * 1000f:F2} ms) is not applied by this app version";
        return null;
    }

    /// <summary>The loaded playback clip matches the representation the profile was bound to (cheap: no PCM).</summary>
    public static string ValidateClip(ProfileHeader h, int samples, int channels, int frequency)
    {
        if (h.playbackSamples != samples || h.playbackChannels != channels || h.playbackFrequency != frequency)
            return $"audio {samples} frames × {channels} ch @ {frequency} Hz ≠ profile binding {h.playbackSamples} × {h.playbackChannels} @ {h.playbackFrequency}";
        return null;
    }
}
