using System.Collections.Generic;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// The post-win automatic pick (replaces RandomNextSongSelector): the pending assignment if one is
/// still active, else the first uncompleted song on the similarity route (SongProgression), assigned
/// with its rival through RunAssignment. Null when nothing is left — SongProgression.CatalogComplete
/// then tells the caller why. Never repeats a completed song.
/// </summary>
public class SimilarityRouteNextSongSelector : INextSongSelector
{
    /// <param name="previousDisplayName">The just-played song's STABLE songId (name kept from INextSongSelector).</param>
    public IResourceLocation SelectNext(IReadOnlyList<IResourceLocation> catalog, string previousDisplayName)
    {
        var ids = SongProgression.Ids(catalog);
        // Never overwrites an active assignment: a valid pendingRun is returned as-is; otherwise the next
        // route song is assigned (song + rival persisted) — RunAssignment.ResolveContinue.
        var status = RunAssignment.ResolveContinue(ids, out string next);
        if (status == NextSongStatus.NoSeed && !string.IsNullOrEmpty(previousDisplayName) && ids.Contains(previousDisplayName))
        {
            // No seed yet (e.g. a local-file or pre-history run): the just-played catalog song becomes it.
            SongProgression.OnManualSelection(previousDisplayName, ids);
            status = RunAssignment.ResolveContinue(ids, out next);
        }
        return status == NextSongStatus.Found ? SongProgression.Find(catalog, next) : null;
    }
}
