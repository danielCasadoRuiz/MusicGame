using System.Collections.Generic;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// The post-win automatic pick (replaces RandomNextSongSelector): the first uncompleted song on the
/// similarity route (SongProgression). Null when nothing is left — SongProgression.CatalogComplete
/// then tells the caller why. Never repeats a completed song.
/// </summary>
public class SimilarityRouteNextSongSelector : INextSongSelector
{
    public IResourceLocation SelectNext(IReadOnlyList<IResourceLocation> catalog, string previousDisplayName)
    {
        var ids = SongProgression.Ids(catalog);
        var status = SongProgression.ResolveNext(ids, out string next);
        if (status == NextSongStatus.NoSeed && !string.IsNullOrEmpty(previousDisplayName) && ids.Contains(previousDisplayName))
        {
            // No seed yet (e.g. a local-file or pre-history run): the just-played catalog song becomes it.
            SongProgression.OnManualSelection(previousDisplayName, ids);
            status = SongProgression.ResolveNext(ids, out next);
        }
        return status == NextSongStatus.Found ? SongProgression.Find(catalog, next) : null;
    }
}
