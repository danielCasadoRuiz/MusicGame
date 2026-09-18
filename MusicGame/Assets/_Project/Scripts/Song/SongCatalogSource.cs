using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// ISongSource wrapping one SongCatalogSO.SongDefinition — unlike the old PredefinedSongSource
/// (instant, since it held a strong AudioClip reference), this actually loads the clip via
/// Addressables the moment it's selected as the song to PLAY, not before (see SongCatalogSO's own
/// doc on why the catalog only ever holds a lightweight reference).
///
/// KNOWN LIMITATION: does not release its Addressables handle — fine for a single active selection
/// (there's currently no UI path that loads a second catalog song's audio without the app
/// restarting), but revisit with proper reference counting (same shape as ThemeAssetLoader) once
/// Song Selection lets a player swap songs freely within one session.
/// </summary>
public class SongCatalogSource : ISongSource
{
    private readonly SongCatalogSO.SongDefinition _definition;

    public SongCatalogSource(SongCatalogSO.SongDefinition definition) => _definition = definition;

    public string DisplayName => _definition.title;

    public IEnumerator Load(Action<SelectedSongInfo?> onComplete)
    {
        if (_definition.audioClip == null || !_definition.audioClip.RuntimeKeyIsValid())
        {
            Debug.LogWarning($"[SongCatalogSource] '{_definition.title}' has no valid audio Addressable reference.");
            onComplete?.Invoke(null);
            yield break;
        }

        AsyncOperationHandle<AudioClip> handle = Addressables.LoadAssetAsync<AudioClip>(_definition.audioClip);
        yield return handle;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogWarning($"[SongCatalogSource] Failed to load audio for '{_definition.title}' — " +
                              "is it actually marked Addressable and included in a build?");
            onComplete?.Invoke(null);
            yield break;
        }

        onComplete?.Invoke(new SelectedSongInfo
        {
            DisplayName   = _definition.title,
            Clip          = handle.Result,
            LocalFilePath = null,
        });
    }
}
