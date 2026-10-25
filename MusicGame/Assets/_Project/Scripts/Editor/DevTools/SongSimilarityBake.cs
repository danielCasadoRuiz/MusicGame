#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > MusicGame > Song Similarity > Bake Playable Song Vectors (incremental) / … (Force Rebuild All).
/// Since P3 the similarity vectors come from the SAME analysis as the published profiles: this is the catalog
/// bake (SongCatalogBake), which builds each vector from the song's published profile and writes it both into
/// the remote index (SongCatalogIndexSO — what lets a content update add songs with their similarity) and into
/// Configs/Song/SongSimilarityCatalog.asset (the player-shipped fallback). It never reads the developer's
/// personal analysis cache, so the result no longer depends on the machine.
/// </summary>
public static class SongSimilarityBake
{
    [MenuItem("Tools/MusicGame/Song Similarity/Bake Playable Song Vectors")]
    public static void Bake() => Run(force: false);

    [MenuItem("Tools/MusicGame/Song Similarity/Bake Playable Song Vectors (Force Rebuild All)")]
    public static void BakeForce() => Run(force: true);

    public static void Run(bool force) => Debug.Log(SongCatalogBake.Bake(force, null, out _));
}
#endif
