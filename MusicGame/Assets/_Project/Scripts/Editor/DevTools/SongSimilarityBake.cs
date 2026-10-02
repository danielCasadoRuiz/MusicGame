#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// Tools > MusicGame > Song Similarity > Bake Playable Song Vectors.
/// Pre-computes one SongSimilarityVector per PLAYABLE song — exactly the PlayableSongScope catalog
/// (Music root + subfolders, the same source as SongAddressablesSetup; never composer/opponent music)
/// — from the analyses already in SongCache (persistentDataPath/SongCache, shared by Editor Play mode):
/// no audio re-analysis, no ML, only the derived features are recomputed. Writes
/// Configs/Song/SongSimilarityCatalog.asset and assigns it to AppConfig.songSimilarity.
/// Songs never analysed yet are reported (play/analyse them once, then re-bake); at runtime they get
/// a vector automatically after their first analysis anyway.
/// </summary>
public static class SongSimilarityBake
{
    private const string AssetPath = "Assets/_Project/Configs/Song/SongSimilarityCatalog.asset";

    [MenuItem("Tools/MusicGame/Song Similarity/Bake Playable Song Vectors")]
    public static void Bake()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<SongSimilarityCatalogSO>(AssetPath);
        if (catalog == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            catalog = ScriptableObject.CreateInstance<SongSimilarityCatalogSO>();
            AssetDatabase.CreateAsset(catalog, AssetPath);
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var app = Resources.Load<AppConfigSO>("AppConfig");
        int baked = 0;
        var missing = new System.Collections.Generic.List<string>();
        catalog.vectors.Clear();

        foreach (var guid in PlayableSongScope.FindPlayableClipGuids())
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) continue;
            // Stable id = the runtime Addressable address (SongAddressablesSetup), else the file name.
            var entry = settings != null ? settings.FindAssetEntry(guid) : null;
            string songId = entry != null ? entry.address : Path.GetFileNameWithoutExtension(path);

            var profile = LoadSongCache(clip, app);
            var vector = profile != null ? SongSimilarityVector.Build(songId, profile) : null;
            if (vector == null) { missing.Add(songId); continue; }
            catalog.vectors.Add(vector);
            baked++;
        }

        EditorUtility.SetDirty(catalog);
        if (app != null && app.songSimilarity != catalog) { app.songSimilarity = catalog; EditorUtility.SetDirty(app); }
        AssetDatabase.SaveAssets();

        Debug.Log($"[SongSimilarityBake] {baked} playable song vector(s) baked into {AssetPath} (root {PlayableSongScope.MusicRoot})." +
                  (missing.Count > 0 ? $" {missing.Count} without a SongCache analysis yet (analyse once, re-bake): {string.Join(", ", missing)}" : ""));
    }

    /// <summary>Exact SongCache entry for this clip (name + sample count), derived features recomputed.</summary>
    private static SongProfile LoadSongCache(AudioClip clip, AppConfigSO app)
    {
        string file = Path.Combine(Application.persistentDataPath, "SongCache", clip.name + "_" + clip.samples + ".json");
        if (!File.Exists(file)) return null;
        try
        {
            var data = JsonUtility.FromJson<SongProfileData>(File.ReadAllText(file));
            if (data == null || data.clipName != clip.name || data.clipSamples != clip.samples) return null;
            var profile = data.ToProfile();
            AudioPreAnalyzer.ComputeDerivedFeatures(profile, app != null ? app.audioAnalysis : null);
            return profile;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SongSimilarityBake] Could not read {file}: {e.Message}");
            return null;
        }
    }
}
#endif
