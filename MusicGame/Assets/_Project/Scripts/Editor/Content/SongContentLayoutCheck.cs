#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// Verifies the BUILT layout of the published song catalog (not the group settings): opens every bundle of
/// the last content build — remote (RemoteBuildPath, e.g. ServerData/&lt;target&gt;) and local (Library/aa) —
/// and lists what each one really contains. Expected (SongContentKeys): one bundle per song AUDIO, one per
/// song PROFILE, one for the INDEX, all remote; no song audio / profile / index in any local bundle (local
/// bundles ship inside the player). Reports bundle sizes per song and in total.
/// Batch: -executeMethod SongContentLayoutCheck.CheckFromCommandLine [-profile LocalContentServer] [-layoutReport path]
/// (the profile whose RemoteBuildPath the content was built with)
/// </summary>
public static class SongContentLayoutCheck
{
    [MenuItem("Tools/MusicGame/Content/Songs/Check Built Song Layout")]
    public static void CheckMenu() => Debug.Log(Check(out _));

    public static void CheckFromCommandLine()
    {
        int errors = 0;
        string report = ContentUpdateTools.WithProfile(ContentUpdateTools.Arg("-profile"), () => Check(out errors));
        File.WriteAllText(ContentUpdateTools.Arg("-layoutReport") ?? "Logs/SongContentLayout.txt", report, Encoding.UTF8);
        Debug.Log(report);
        EditorApplication.Exit(errors > 0 ? 1 : 0);
    }

    public static string Check(out int errors)
    {
        var sb = new StringBuilder($"[SongContentLayoutCheck] target {EditorUserBuildSettings.activeBuildTarget}\n");
        int err = 0;
        void Error(string m) { err++; sb.AppendLine("  ERROR " + m); }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var songs = app.songCatalog.songs.Where(s => s != null && !string.IsNullOrEmpty(s.songId)).ToList();
        var audioPaths = songs.ToDictionary(s => AssetDatabase.GUIDToAssetPath(s.editorGuid).ToLowerInvariant(), s => s.songId);
        var profilePaths = songs.ToDictionary(s => SongContentKeys.ProfileAssetPath(s.songId).ToLowerInvariant(), s => s.songId);
        string indexPath = SongContentKeys.IndexAssetPath.ToLowerInvariant();

        string remoteDir = Path.GetFullPath(settings.RemoteCatalogBuildPath.GetValue(settings));
        string localDir = Path.GetFullPath(Path.Combine(UnityEngine.AddressableAssets.Addressables.BuildPath, EditorUserBuildSettings.activeBuildTarget.ToString()));
        sb.AppendLine($"  remote {remoteDir}\n  local  {localDir}");

        // Only the bundles of the LAST build (its content state): the remote folder keeps older builds' files.
        var state = UnityEditor.AddressableAssets.Build.ContentUpdateScript.LoadContentState(
            UnityEditor.AddressableAssets.Build.ContentUpdateScript.GetContentStateDataPath(false, settings));
        var current = state?.cachedBundles != null
            ? new HashSet<string>(state.cachedBundles.Select(b => Path.GetFileName(b.bundleFileId)), StringComparer.OrdinalIgnoreCase)
            : null;
        if (current == null) Error("no content state of the last build — cannot tell current bundles from stale ones");
        else sb.AppendLine($"  last build: {current.Count} bundles in its content state");

        var perSong = songs.ToDictionary(s => s.songId, s => (audio: new List<(string, long)>(), profile: new List<(string, long)>()));
        int indexBundles = 0;
        long remoteSongBytes = 0;
        foreach (var (dir, remote) in new[] { (remoteDir, true), (localDir, false) })
        {
            if (!Directory.Exists(dir)) { if (remote) Error($"no remote build at {dir}"); continue; }
            foreach (var file in Directory.GetFiles(dir, "*.bundle", SearchOption.AllDirectories))
            {
                if (current != null && !current.Contains(Path.GetFileName(file))) continue; // stale file of an older build
                var bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null) { Error($"cannot open {Path.GetFileName(file)}"); continue; }
                try
                {
                    var names = bundle.GetAllAssetNames().Select(n => n.ToLowerInvariant()).ToArray(); // case varies by build
                    long size = new FileInfo(file).Length;
                    var audio = names.Where(audioPaths.ContainsKey).ToList();
                    var prof = names.Where(profilePaths.ContainsKey).ToList();
                    bool index = names.Contains(indexPath);
                    if (audio.Count + prof.Count + (index ? 1 : 0) == 0) continue;
                    string name = Path.GetFileName(file);
                    if (!remote) { Error($"LOCAL bundle {name} (ships in the player) contains catalog content: {string.Join(", ", names)}"); continue; }
                    if (names.Length != 1) Error($"{name} holds {names.Length} assets ({string.Join(", ", names)}) — expected one");
                    foreach (var a in audio) perSong[audioPaths[a]].audio.Add((name, size));
                    foreach (var p in prof) perSong[profilePaths[p]].profile.Add((name, size));
                    if (index) { indexBundles++; sb.AppendLine($"  index   {name}  {size / 1024.0:F1} KB"); }
                    remoteSongBytes += size;
                }
                finally { bundle.Unload(true); }
            }
        }

        long maxSong = 0;
        foreach (var s in songs)
        {
            var (audio, profile) = perSong[s.songId];
            if (audio.Count != 1) Error($"{s.songId}: {audio.Count} bundles hold its audio (expected 1)");
            if (profile.Count != 1) Error($"{s.songId}: {profile.Count} bundles hold its profile (expected 1)");
            if (audio.Count == 1 && profile.Count == 1 && audio[0].Item1 == profile[0].Item1) Error($"{s.songId}: audio and profile share a bundle");
            long a = audio.Sum(x => x.Item2), p = profile.Sum(x => x.Item2);
            maxSong = Math.Max(maxSong, a + p);
            sb.AppendLine($"  {s.songId}  audio {a / 1048576.0:F2} MB ({audio.FirstOrDefault().Item1})  profile {p / 1048576.0:F2} MB ({profile.FirstOrDefault().Item1})");
        }
        if (indexBundles != 1) Error($"{indexBundles} bundles hold the index (expected 1)");
        sb.AppendLine($"  total remote song content {remoteSongBytes / 1048576.0:F1} MB in {songs.Count * 2 + indexBundles} bundles; largest song download {maxSong / 1048576.0:F2} MB");
        sb.AppendLine(err == 0 ? "  PASS  one remote bundle per audio, per profile and for the index; nothing in local bundles" : $"  FAIL  {err} error(s)");
        errors = err;
        return sb.ToString();
    }
}
#endif
