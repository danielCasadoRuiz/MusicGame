using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>Selects the next campaign song under the same screen that prepares it for the Runner.</summary>
public class NextSongTransitionController : MonoBehaviour
{
    public static NextSongTransitionController Instance { get; private set; }
    private const string SongLabel = "Song";
    private SongSelectionService _service;
    private readonly INextSongSelector _selector = new SimilarityRouteNextSongSelector();
    private Coroutine _routine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _service = gameObject.AddComponent<SongSelectionService>();
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void BeginWin()
    {
        if (_routine != null) StopCoroutine(_routine);
        FightMusicController.Instance?.Stop();
        if (CampaignService.LapFinished)
        {
            _routine = null;
            AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
            return;
        }
        // No second card or minimum hold: keep this one view from selection through world readiness.
        FindFirstObjectByType<AnalyzingScreenController>()?.ShowPendingNextSong();
        _routine = StartCoroutine(RunWinSequence());
    }

    private IEnumerator RunWinSequence()
    {
        // The just-played song's own identity — see INextSongSelector's own doc on why this (its
        // Addressables PrimaryKey) is reused as-is rather than inventing a new Song ID scheme.
        // Stable songId of the just-defended song (null for a local file).
        string previousDisplayName = GameSession.Instance != null && GameSession.Instance.SelectedSong.HasValue
            ? GameSession.Instance.SelectedSong.Value.SongId
            : null;

        yield return SongCatalogService.EnsureReady(); // published identities, after the catalog check
        var catalog = new List<IResourceLocation>();
        AsyncOperationHandle<IList<IResourceLocation>> locationsHandle =
            Addressables.LoadResourceLocationsAsync(SongLabel, typeof(AudioClip));
        yield return locationsHandle;
        if (locationsHandle.Status == AsyncOperationStatus.Succeeded)
            catalog.AddRange(locationsHandle.Result);
        else
            Debug.LogWarning("[NextSongTransitionController] Failed to query the 'Song' Addressables label.");
        Addressables.Release(locationsHandle);

        var nextLocation = _selector.SelectNext(catalog, previousDisplayName);
        if (nextLocation == null)
        {
            // Catalog complete (every playable song done) is a normal end state, not an error: the
            // Main Menu shows it (SongProgression.CatalogComplete / SongCatalogCompletedEvent).
            if (SongProgression.CatalogComplete) Debug.Log("[NextSongTransitionController] Playable catalog complete — back to Main Menu.");
            else Debug.LogWarning("[NextSongTransitionController] No song available in the 'Song' catalog — cannot continue. Falling back to Main Menu.");
            FightMusicController.Instance?.Stop();
            AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
            yield break;
        }

        bool loaded = false;
        yield return _service.SelectSong(new CatalogSongSource(SongProgression.IdOf(nextLocation), SongCatalog.DisplayNameForAddress(nextLocation.PrimaryKey)), null, success => loaded = success);

        if (!loaded)
        {
            Debug.LogWarning($"[NextSongTransitionController] Failed to load '{nextLocation.PrimaryKey}' — falling back to Main Menu.");
            FightMusicController.Instance?.Stop();
            AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
            yield break;
        }

        _routine = null;
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.SongAnalysis);
    }
}
