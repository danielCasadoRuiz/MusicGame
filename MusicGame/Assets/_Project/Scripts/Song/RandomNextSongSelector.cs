using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// Today's INextSongSelector — picks uniformly at random from the whole catalog, excluding the
/// just-played song when at least one real alternative exists (task's own explicit "intenta no
/// seleccionar immediatament la mateixa cançó" requirement — best-effort, not a hard guarantee: a
/// 1-song catalog has no alternative, so the same song is returned rather than nothing). No musical
/// similarity reasoning at all — see INextSongSelector's own doc on the future
/// SimilarityNextSongSelector this seam exists for.
/// </summary>
public class RandomNextSongSelector : INextSongSelector
{
    public IResourceLocation SelectNext(IReadOnlyList<IResourceLocation> catalog, string previousDisplayName)
    {
        if (catalog == null || catalog.Count == 0) return null;
        if (catalog.Count == 1) return catalog[0];

        var candidates = new List<IResourceLocation>(catalog.Count);
        foreach (var location in catalog)
            if (location != null && location.PrimaryKey != previousDisplayName) candidates.Add(location);

        // Every entry matched the previous song's key (shouldn't normally happen with a real
        // catalog) — fall back to the full catalog rather than returning null.
        if (candidates.Count == 0) candidates.AddRange(catalog);

        return candidates[Random.Range(0, candidates.Count)];
    }
}
