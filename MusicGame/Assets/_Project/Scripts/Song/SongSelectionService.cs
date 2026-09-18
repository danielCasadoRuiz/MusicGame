using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Orchestrates "the player picked a song" → GameSession.SelectedSong, for whichever ISongSource
/// produced it (predefined or local file — see that interface's own doc). Deliberately a small,
/// addable-anywhere MonoBehaviour rather than a persistent AppBootstrap module: Song Selection is
/// a one-shot action a future Song Selection screen will trigger, not cross-scene state of its
/// own (GameSession already IS that state).
///
/// NOT wired to any real UI or to the GameFlowState.SongSelection state yet — there is no Song
/// Selection screen built yet (that's real UI work, out of this phase's scope), so hooking this up
/// to actually gate the flow there would leave the app stuck with nothing to advance it past
/// SongSelection. FlowConfig.initialState stays at SongAnalysis for now (unchanged): the game
/// keeps working exactly as it already did, analyzing whatever AudioClip is on the scene's
/// AudioSource. This class is the real, working plumbing a future Selection screen calls into.
/// </summary>
public class SongSelectionService : MonoBehaviour
{
    /// <summary>
    /// Loads `source`, and on success writes GameSession.Instance.SelectedSong AND assigns the
    /// loaded clip onto `targetAudioSource` (the same AudioSource AudioSystemBootstrapper reads
    /// from — see RunnerSceneBootstrap, which also re-applies GameSession.SelectedSong.Clip before
    /// starting analysis, so a song picked before a scene even reloads still gets used correctly).
    /// `onComplete(true)` on success, `onComplete(false)` if the source produced nothing
    /// (cancelled picker, failed load) — never throws for that case.
    /// </summary>
    public IEnumerator SelectSong(ISongSource source, AudioSource targetAudioSource, Action<bool> onComplete)
    {
        if (source == null)
        {
            Debug.LogWarning("[SongSelectionService] No ISongSource given.");
            onComplete?.Invoke(false);
            yield break;
        }

        SelectedSongInfo? result = null;
        yield return source.Load(info => result = info);

        if (result == null || result.Value.Clip == null)
        {
            onComplete?.Invoke(false);
            yield break;
        }

        if (GameSession.Instance != null) GameSession.Instance.SelectedSong = result;
        if (targetAudioSource != null) targetAudioSource.clip = result.Value.Clip;

        onComplete?.Invoke(true);
    }
}
