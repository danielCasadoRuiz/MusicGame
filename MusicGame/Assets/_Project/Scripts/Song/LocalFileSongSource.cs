using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// ISongSource for "Play Your Song" (Section 5.B of the app-flow/Theme refactor plan) — asks an
/// ILocalSongPicker for a path, then decodes it via LocalAudioClipLoader. Neither step is
/// hardcoded here: swap the picker for a real native-dialog implementation later (see
/// LocalSongPickerFactory) without touching this class at all.
/// </summary>
public class LocalFileSongSource : ISongSource
{
    private readonly ILocalSongPicker _picker;
    private string _pickedPath;

    public LocalFileSongSource(ILocalSongPicker picker) => _picker = picker;

    public string DisplayName => _pickedPath != null ? Path.GetFileNameWithoutExtension(_pickedPath) : Loc.Get("SongSelection.LocalFileDefaultName");

    public IEnumerator Load(Action<SelectedSongInfo?> onComplete)
    {
        _pickedPath = _picker?.PickFile();
        if (string.IsNullOrEmpty(_pickedPath))
        {
            onComplete?.Invoke(null); // cancelled — not an error
            yield break;
        }

        AudioClip loadedClip = null;
        yield return LocalAudioClipLoader.Load(_pickedPath, clip => loadedClip = clip);

        if (loadedClip == null)
        {
            onComplete?.Invoke(null);
            yield break;
        }

        onComplete?.Invoke(new SelectedSongInfo
        {
            DisplayName   = Path.GetFileNameWithoutExtension(_pickedPath),
            Clip          = loadedClip,
            LocalFilePath = _pickedPath,
        });
    }
}
