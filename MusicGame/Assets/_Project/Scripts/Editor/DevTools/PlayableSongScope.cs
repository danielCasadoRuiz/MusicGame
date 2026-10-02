#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// The ONE definition of "playable Runner song" for Editor tooling: an AudioClip under
/// Assets/_Project/Audio/Music, directly or in ANY subfolder. Shared by SongAddressablesSetup (the
/// "Songs" group / "Song" label) and SongSimilarityBake (the similarity vectors), so both always see
/// exactly the same catalog. Composer/opponent music (Assets/_Project/Audio/Composers, referenced by
/// OpponentDefinition.songs) is outside this root and therefore never a playable song.
/// </summary>
public static class PlayableSongScope
{
    public const string MusicRoot = "Assets/_Project/Audio/Music";

    /// <summary>True for a path inside MusicRoot (any depth). Explicit prefix check — never inferred.</summary>
    public static bool IsPlayablePath(string assetPath) =>
        !string.IsNullOrEmpty(assetPath) && assetPath.Replace('\\', '/').StartsWith(MusicRoot + "/", System.StringComparison.Ordinal);

    /// <summary>GUIDs of every AudioClip under MusicRoot, recursively (AssetDatabase searches a folder
    /// and all its subfolders), double-checked against the root. Sorted by path (deterministic).</summary>
    public static List<string> FindPlayableClipGuids()
    {
        var result = new List<string>();
        if (!AssetDatabase.IsValidFolder(MusicRoot)) return result;
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { MusicRoot }))
            if (IsPlayablePath(AssetDatabase.GUIDToAssetPath(guid))) result.Add(guid);
        result.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));
        return result;
    }
}
#endif
