using System.Collections.Generic;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// Abstraction over "which song comes next" for the post-Match-Won Continue flow (see
/// NextSongTransitionController's own doc) — deliberately never a raw Random.Range call inline in a
/// controller, so a future SimilarityNextSongSelector (a feature vector derived from the just-played
/// SongProfile, picking a stylistically close song and progressively relaxing that similarity as
/// PlayerLevel rises — see this phase's own explicit "NO implementis encara similitud" scope note)
/// can replace RandomNextSongSelector below without touching NextSongTransitionController at all.
/// </summary>
public interface INextSongSelector
{
    /// <summary>Picks one entry from `catalog` (every song currently tagged with the "Song"
    /// Addressables label — see SongSelectionController's own doc). `previousDisplayName` is the
    /// just-played song's own DisplayName (== its Addressables PrimaryKey — the existing song
    /// identity, see SelectedSongInfo's own doc; never a duplicated/new song-ID scheme).
    /// Implementations should avoid repeating it when a genuine alternative exists in `catalog`.
    /// Returns null only if `catalog` is null/empty.</summary>
    IResourceLocation SelectNext(IReadOnlyList<IResourceLocation> catalog, string previousDisplayName);
}
