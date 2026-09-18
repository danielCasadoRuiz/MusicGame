using System;
using System.Collections;

/// <summary>
/// ISongSource wrapping one PredefinedSongLibrarySO.Entry — "loading" is instant (the AudioClip
/// reference already exists), but still coroutine-shaped so callers never need to branch on which
/// kind of ISongSource they're holding.
/// </summary>
public class PredefinedSongSource : ISongSource
{
    private readonly PredefinedSongLibrarySO.Entry _entry;

    public PredefinedSongSource(PredefinedSongLibrarySO.Entry entry) => _entry = entry;

    public string DisplayName => _entry.displayName;

    public IEnumerator Load(Action<SelectedSongInfo?> onComplete)
    {
        onComplete?.Invoke(new SelectedSongInfo
        {
            DisplayName   = _entry.displayName,
            Clip          = _entry.clip,
            LocalFilePath = null,
        });
        yield break;
    }
}
