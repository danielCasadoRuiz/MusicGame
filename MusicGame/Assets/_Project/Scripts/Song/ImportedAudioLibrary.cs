using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

[Serializable]
public class ImportedAudioRecord
{
    public string id, title, fileName;
    public GameMusicStyle style;
}

/// <summary>App-owned copies and the one-time trial live separately from progression and purchases.
/// Deleting an entry never deletes an original file and never restores the free import.</summary>
public static class ImportedAudioLibrary
{
    [Serializable] private class Manifest
    {
        public string trialId;
        public bool trialRetryAllowed;
        public List<ImportedAudioRecord> tracks = new();
    }
    private static Manifest s_data;
#if UNITY_EDITOR
    public static string TestFolder { get; set; }
    public static void ReloadForTests() => s_data = null;
#endif
    public static string Folder =>
#if UNITY_EDITOR
        TestFolder ??
#endif
        Path.Combine(Application.persistentDataPath, "ImportedAudio");
    private static string ManifestPath => Path.Combine(Folder, "library.json");
    public static IReadOnlyList<ImportedAudioRecord> Tracks { get { Load(); return s_data.tracks; } }
    public static bool CanImport { get { Load(); return MusicAccess.CustomAudio || string.IsNullOrEmpty(s_data.trialId); } }
    public static string PathFor(ImportedAudioRecord track) => track == null || string.IsNullOrEmpty(track.fileName) || Path.GetFileName(track.fileName) != track.fileName
        ? null : Path.Combine(Folder, track.fileName);
    public static ImportedAudioRecord Find(string id) { Load(); return s_data.tracks.Find(t => t.id == id); }
    public static ImportedAudioRecord FindByPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        Load();
        return s_data.tracks.Find(t => string.Equals(PathFor(t), path, StringComparison.OrdinalIgnoreCase));
    }
    public static bool CanSelectIdentity(string id)
    {
        Load();
        return CanImport || Find(id) != null || (s_data.trialRetryAllowed && s_data.trialId == id);
    }
    public static void DiscardUnwon()
    {
        Load();
        foreach (var track in new List<ImportedAudioRecord>(s_data.tracks))
            if (PlayerProgressService.Instance?.IsJukeboxUnlocked(track.id) != true) Delete(track.id, allowTrialRetry: true);
    }
    public static string Identity(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return "import_" + BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    // Called only AFTER the selected file was successfully decoded. No trial is consumed on cancel/error.
    public static ImportedAudioRecord Import(string source, string title)
    {
        Load();
        var existing = FindByPath(source);
        if (existing != null) return existing;
        string id = Identity(source);
        existing = Find(id);
        if (existing != null && File.Exists(PathFor(existing))) return existing;
        if (!CanSelectIdentity(id)) return null;
        string ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext != ".wav" && ext != ".mp3" && ext != ".ogg" && ext != ".aif" && ext != ".aiff") return null;
        Directory.CreateDirectory(Folder);
        var record = new ImportedAudioRecord { id = id, title = title, fileName = id + ext };
        string target = PathFor(record), temp = target + ".tmp";
        try
        {
            File.Copy(source, temp, true);
            if (File.Exists(target)) File.Delete(target);
            File.Move(temp, target);
            if (existing != null) s_data.tracks.Remove(existing);
            s_data.tracks.Add(record);
            string oldTrial = s_data.trialId;
            if (string.IsNullOrEmpty(s_data.trialId)) s_data.trialId = id;
            try { Save(); }
            catch { s_data.tracks.Remove(record); s_data.trialId = oldTrial; if (existing != null) s_data.tracks.Add(existing); throw; }
            return record;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static bool Delete(string id, bool allowTrialRetry = false)
    {
        Load();
        var track = Find(id);
        if (track == null) return false;
        // Persist removal before deleting only the app-owned copy. The trial tombstone stays.
        s_data.tracks.Remove(track);
        bool previousRetry = s_data.trialRetryAllowed;
        if (s_data.trialId == id) s_data.trialRetryAllowed = allowTrialRetry;
        try { Save(); } catch { s_data.trialRetryAllowed = previousRetry; s_data.tracks.Add(track); throw; }
        PlayerProgressService.Instance?.RemoveJukeboxImport(id);
        string path = PathFor(track);
        if (path != null && File.Exists(path)) File.Delete(path);
        // Remove the native player's derived copy too; the source document is never touched.
        string native = NativeMusicPlayer.CachedImportPath(id);
        if (File.Exists(native)) File.Delete(native);
        if (File.Exists(native + ".tmp")) File.Delete(native + ".tmp");
        return true;
    }
    public static void RecordStyle(string path, GameMusicStyle style)
    {
        var track = FindByPath(path);
        if (track == null || track.style == style) return;
        track.style = style; Save();
    }
    private static void Load()
    {
        if (s_data != null) return;
        string path = ManifestPath;
        // Recover a previous atomic save if the current manifest was damaged. Never silently reset a trial.
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try { s_data = JsonUtility.FromJson<Manifest>(File.ReadAllText(candidate)); if (s_data != null) break; }
            catch (Exception e) { Debug.LogWarning("[ImportedAudio] " + e.Message); }
        }
        if (s_data == null && (File.Exists(path) || File.Exists(path + ".bak")))
            throw new IOException("Imported audio library could not be read.");
        s_data ??= new Manifest();
        s_data.tracks ??= new List<ImportedAudioRecord>();
        s_data.tracks.RemoveAll(t => t == null || string.IsNullOrEmpty(t.id) || PathFor(t) == null);
    }
    private static void Save()
    {
        Directory.CreateDirectory(Folder);
        string path = ManifestPath, temp = path + ".tmp";
        File.WriteAllText(temp, JsonUtility.ToJson(s_data, true));
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => s_data = null;
}
