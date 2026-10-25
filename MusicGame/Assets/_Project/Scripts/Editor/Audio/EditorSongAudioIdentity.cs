#if UNITY_EDITOR
using MusicGame.Audio.Analysis;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor side of SongAudioIdentity: a clip that is a project asset is identified by its SOURCE file
/// bytes (hashed on a worker by SongAudioIdentitySource) plus the import settings that shape the PCM
/// the analysis reads — force-to-mono, normalize, and the effective sample settings of the active
/// build target (compression format, quality, sample-rate setting/override). Settings that only change
/// how the data is held (load type, preload, load in background) are deliberately NOT part of it.
/// Clips that are not project assets (loaded from a built bundle, created at runtime) fall back to
/// the decoded-PCM identity.
/// </summary>
[InitializeOnLoad]
public static class EditorSongAudioIdentity
{
    static EditorSongAudioIdentity()
    {
        SongAudioIdentitySource.EditorAssetResolver = Resolve;
    }

    /// <summary>MAIN THREAD. (absolute source path, settings hash) or null.</summary>
    public static (string path, ulong settingsHash)? Resolve(AudioClip clip)
    {
        string assetPath = clip != null ? AssetDatabase.GetAssetPath(clip) : null;
        if (string.IsNullOrEmpty(assetPath)) return null;
        var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
        if (importer == null) return null;
        return (System.IO.Path.GetFullPath(assetPath), SettingsHash(importer));
    }

    public static ulong SettingsHash(AudioImporter importer)
    {
        var target = EditorUserBuildSettings.activeBuildTarget;
        string platform = BuildPipeline.GetBuildTargetGroup(target).ToString();
        var s = importer.ContainsSampleSettingsOverride(platform) ? importer.GetOverrideSampleSettings(platform) : importer.defaultSampleSettings;
        var normalize = new SerializedObject(importer).FindProperty("m_Normalize");

        var h = StableHash.Create();
        h.Add("AudioImporter.pcm");
        h.Add(platform);
        h.Add(importer.forceToMono);
        h.Add(normalize != null && normalize.boolValue);
        h.Add((int)s.compressionFormat);
        h.Add(s.quality);
        h.Add((int)s.sampleRateSetting);
        h.Add((int)s.sampleRateOverride);
        return h.Value;
    }
}
#endif
