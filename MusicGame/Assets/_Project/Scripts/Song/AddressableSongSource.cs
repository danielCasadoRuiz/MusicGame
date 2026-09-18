using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// ISongSource for a song coming from the "Song" Addressables label (see SongAddressablesSetup) —
/// unlike the old SongCatalogSO-backed source, there is no hand-authored per-song metadata: the
/// display name IS the Addressable address itself (set once, in the Editor tool, from the audio
/// file's name — see its own FriendlyNames table). Adding, replacing, or removing a song is purely
/// a content-side operation (mark/unmark Addressable, or later: push a new remote content release)
/// — nothing in code needs to change or ship a new build for the catalog to reflect it.
///
/// Same "load only once actually selected to play" shape as the old SongCatalogSource, and the same
/// KNOWN LIMITATION: does not release its Addressables handle — fine for a single active selection
/// per session.
/// </summary>
public class AddressableSongSource : ISongSource
{
    private readonly IResourceLocation _location;

    public AddressableSongSource(IResourceLocation location) => _location = location;

    public string DisplayName => _location != null ? _location.PrimaryKey : "";

    public IEnumerator Load(Action<SelectedSongInfo?> onComplete)
    {
        if (_location == null)
        {
            Debug.LogWarning("[AddressableSongSource] No resource location given.");
            onComplete?.Invoke(null);
            yield break;
        }

        AsyncOperationHandle<AudioClip> handle = Addressables.LoadAssetAsync<AudioClip>(_location);
        yield return handle;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogWarning($"[AddressableSongSource] Failed to load audio for '{DisplayName}'.");
            onComplete?.Invoke(null);
            yield break;
        }

        onComplete?.Invoke(new SelectedSongInfo
        {
            DisplayName   = DisplayName,
            Clip          = handle.Result,
            LocalFilePath = null,
        });
    }
}
