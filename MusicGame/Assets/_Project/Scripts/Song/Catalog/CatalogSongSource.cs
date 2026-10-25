using System;
using System.Collections;

/// <summary>
/// ISongSource of a published CATALOG song (stable songId). Selecting it only records WHICH song is the
/// selection — it loads nothing: the audio + published profile are prepared afterwards by the readiness
/// barrier (SongAnalysisController → CatalogSongLoader), which owns download / load / validation and the
/// handle lease, bound to the selection generation this selection starts. (Replaces the old
/// AddressableSongSource, which loaded the clip here and never released its handle.)
/// </summary>
public class CatalogSongSource : ISongSource
{
    private readonly string _songId;
    private readonly string _displayName;

    public CatalogSongSource(string songId, string displayName)
    {
        _songId = songId;
        _displayName = displayName;
    }

    public string DisplayName => _displayName ?? _songId ?? "";

    public IEnumerator Load(Action<SelectedSongInfo?> onComplete)
    {
        if (string.IsNullOrEmpty(_songId))
        {
            UnityEngine.Debug.LogWarning($"[CatalogSongSource] '{_displayName}' has no stable songId — not in the published index.");
            onComplete?.Invoke(null);
            yield break;
        }
        onComplete?.Invoke(new SelectedSongInfo { DisplayName = DisplayName, SongId = _songId, Clip = null, LocalFilePath = null });
    }
}
