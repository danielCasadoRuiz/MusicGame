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

        // Kick the actual PCM decode off NOW, in the background, instead of leaving it to the
        // first audioSource.Play() — a big WAV (uncompressed, so this is genuinely a lot of raw
        // sample data) decoding synchronously right as gameplay starts is exactly what showed up
        // as a hitch immediately after the "3, 2, 1, GO" countdown. Firing it here means the
        // WHOLE analyzing screen + world generation + countdown (several seconds) is spent
        // loading it in the background instead — see GameplayManager.GenerateAndStart's own
        // safety-net wait, which only actually blocks if this somehow hasn't finished by then.
        // Harmless no-op if the asset's own "Preload Audio Data" import setting already did this
        // as part of the LoadAssetAsync above (LoadAudioData on an already-Loaded/Loading clip is
        // a safe no-op per Unity's own docs).
        handle.Result.LoadAudioData();

        onComplete?.Invoke(new SelectedSongInfo
        {
            DisplayName   = DisplayName,
            Clip          = handle.Result,
            LocalFilePath = null,
        });
    }
}
