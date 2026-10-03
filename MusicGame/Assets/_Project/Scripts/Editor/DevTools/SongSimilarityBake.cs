#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// Tools > MusicGame > Song Similarity > Bake Playable Song Vectors (incremental) / … (Force Rebuild All).
/// Produces one SongSimilarityVector for EVERY playable song — exactly the PlayableSongScope catalog
/// (Assets/_Project/Audio/Music/**, the same source as SongAddressablesSetup; never Audio/Composers
/// or any opponent music) — into Configs/Song/SongSimilarityCatalog.asset (assigned to
/// AppConfig.songSimilarity), so shipped content never depends on the player's play history.
///
/// Per song, cheapest valid source first:
///   1. REUSE the stored vector when it matches: same source GUID (its songId/address refreshed), same import/dependency
///      hash of the AudioClip (file content or import settings change ⇒ new hash) and the current
///      SongSimilarityVector.CurrentVersion (feature layout/normalisation change ⇒ version bump).
///   2. The exact SongCache analysis of that clip (name + sample count), derived features recomputed.
///   3. A FULL analysis run here, in the Editor (AudioPreAnalyzer: FFT + Sentis musicnn on CPU,
///      driven synchronously). It also writes the SongCache entry, like a runtime analysis would.
/// Force Rebuild skips (1) only. Catalog-wide z-score statistics are computed at route time from all
/// these vectors (SongSimilarityService), so a complete bake = normalisation over the full catalog.
/// </summary>
public static class SongSimilarityBake
{
    private const string AssetPath = "Assets/_Project/Configs/Song/SongSimilarityCatalog.asset";

    [MenuItem("Tools/MusicGame/Song Similarity/Bake Playable Song Vectors")]
    public static void Bake() => Run(force: false);

    [MenuItem("Tools/MusicGame/Song Similarity/Bake Playable Song Vectors (Force Rebuild All)")]
    public static void BakeForce() => Run(force: true);

    public static void Run(bool force)
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
        var config = app != null ? app.audioAnalysis : null;
        var songCatalog = app != null ? app.songCatalog : null;
        var old = new Dictionary<string, SongSimilarityVector>();
        // Keyed by source GUID (not songId/address): survives renames and the songId introduction.
        foreach (var v in catalog.vectors) if (v != null && !string.IsNullOrEmpty(v.sourceGuid)) old[v.sourceGuid] = v;

        var result = new List<SongSimilarityVector>();
        int reused = 0, fromCache = 0, analysed = 0;
        var failed = new List<string>();
        var guids = PlayableSongScope.FindPlayableClipGuids();
        try
        {
            for (int i = 0; i < guids.Count; i++)
            {
                string guid = guids[i], path = AssetDatabase.GUIDToAssetPath(guid);
                var entry = settings != null ? settings.FindAssetEntry(guid) : null;
                string address = entry != null ? entry.address : Path.GetFileNameWithoutExtension(path);
                // Identity = the STABLE songId (PlayableSongCatalogSO, matched by GUID); the address is informational.
                var catEntry = songCatalog != null ? songCatalog.songs.Find(s => s.editorGuid == guid) : null;
                if (catEntry == null) { failed.Add(address + " (no songId — run Setup Song Addressables)"); continue; }
                string songId = catEntry.songId;
                string hash = AssetDatabase.GetAssetDependencyHash(path).ToString();

                if (!force && old.TryGetValue(guid, out var prev) && prev.IsValid && prev.sourceHash == hash)
                { prev.songId = songId; prev.address = address; result.Add(prev); reused++; continue; }

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) { failed.Add(songId); continue; }
                EditorUtility.DisplayProgressBar("Song Similarity Bake", songId, (float)i / guids.Count);

                var profile = LoadSongCache(clip, config);
                if (profile != null) fromCache++;
                else if ((profile = AnalyseInEditor(clip, config)) != null) analysed++;

                var vector = profile != null ? SongSimilarityVector.Build(songId, profile) : null;
                if (vector == null) { failed.Add(songId); continue; }
                vector.sourceGuid = guid;
                vector.address = address;
                vector.sourceHash = hash;
                result.Add(vector);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        catalog.vectors = result; // songs removed from Music drop out here
        EditorUtility.SetDirty(catalog);
        if (app != null && app.songSimilarity != catalog) { app.songSimilarity = catalog; EditorUtility.SetDirty(app); }
        AssetDatabase.SaveAssets();

        Debug.Log($"[SongSimilarityBake] {result.Count}/{guids.Count} playable song vectors in {AssetPath} " +
                  $"(root {PlayableSongScope.MusicRoot}): {reused} reused, {fromCache} from SongCache, {analysed} analysed now" +
                  (failed.Count > 0 ? $"; FAILED: {string.Join(", ", failed)}" : "."));
    }

    /// <summary>Exact SongCache entry for this clip (name + sample count), derived features recomputed.</summary>
    private static SongProfile LoadSongCache(AudioClip clip, AudioAnalysisConfig config)
    {
        string file = Path.Combine(Application.persistentDataPath, "SongCache", clip.name + "_" + clip.samples + ".json");
        if (!File.Exists(file)) return null;
        try
        {
            var data = JsonUtility.FromJson<SongProfileData>(File.ReadAllText(file));
            if (data == null || data.clipName != clip.name || data.clipSamples != clip.samples) return null;
            var profile = data.ToProfile();
            if (!profile.HasMusicTags) return null; // cached before tagging existed: analyse fully instead
            AudioPreAnalyzer.ComputeDerivedFeatures(profile, config);
            return profile;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SongSimilarityBake] Could not read {file}: {e.Message}");
            return null;
        }
    }

    /// <summary>The real runtime analysis (AudioPreAnalyzer.Analyze), run to completion synchronously:
    /// it only ever yields null / nested enumerators (Sentis runs on the CPU backend); a WaitForSeconds
    /// (cache-hit cosmetic delay) is simply skipped.</summary>
    private static SongProfile AnalyseInEditor(AudioClip clip, AudioAnalysisConfig config)
    {
        if (config == null) { Debug.LogWarning("[SongSimilarityBake] No AppConfig.audioAnalysis — cannot analyse."); return null; }
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();

        var host = new GameObject("[SongSimilarityBake Analyzer]") { hideFlags = HideFlags.HideAndDontSave };
        SongProfile result = null;
        try
        {
            var analyzer = host.AddComponent<AudioPreAnalyzer>();
            RunToEnd(analyzer.Analyze(clip, config, p => result = p));
        }
        catch (System.Exception e) { Debug.LogWarning($"[SongSimilarityBake] Analysis of '{clip.name}' failed: {e.Message}"); }
        finally { Object.DestroyImmediate(host); }
        if (result != null && !result.HasMusicTags)
            Debug.LogWarning($"[SongSimilarityBake] '{clip.name}' analysed without music tags (tagging disabled/unavailable) — tag block will be zero.");
        return result;
    }

    private static void RunToEnd(IEnumerator root)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(root);
        for (long steps = 0; stack.Count > 0; steps++)
        {
            if (steps > 50_000_000) throw new System.Exception("analysis did not finish");
            var top = stack.Peek();
            if (!top.MoveNext()) { stack.Pop(); continue; }
            if (top.Current is IEnumerator nested) stack.Push(nested);
        }
    }
}
#endif
