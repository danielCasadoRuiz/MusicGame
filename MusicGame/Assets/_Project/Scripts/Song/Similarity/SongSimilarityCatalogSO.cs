using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PRE-COMPUTED similarity vectors for the built-in PLAYABLE catalog (Music folder only), baked by
/// Tools > MusicGame > Song Similarity > Bake Playable Song Vectors from the existing SongCache
/// analyses — so runtime only LOADS vectors. Referenced by AppConfigSO.songSimilarity.
/// Composer/opponent music never appears here.
/// </summary>
[CreateAssetMenu(fileName = "SongSimilarityCatalog", menuName = "MusicGame/Song/Song Similarity Catalog")]
public class SongSimilarityCatalogSO : ScriptableObject
{
    public List<SongSimilarityVector> vectors = new();
}
