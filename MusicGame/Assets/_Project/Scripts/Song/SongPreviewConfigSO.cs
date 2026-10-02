using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-song PREVIEW range — where the recognizable part of each song starts and how long a preview
/// lasts. Keyed by the song's EXISTING id (no second song catalog): a Runner song's Addressable
/// address (= SelectedSongInfo.DisplayName / the "Song" label PrimaryKey), or a composer track's
/// AudioClip name (OpponentLevelConfig.songs). Songs without an entry use the defaults below.
/// Used by the Song Selection preview (full previewDuration) and by the rival roulette's short
/// music snippets (only the start point). AppConfigSO.songPreview.
/// AUTOMATIC default (no entry): the CENTRED window — start = length/2 − duration/2 (a 300 s song
/// previews 135–165 s). An entry is a manual override of that start (and optionally the duration).
/// </summary>
[CreateAssetMenu(fileName = "SongPreviewConfig", menuName = "MusicGame/Song/Song Preview Config")]
public class SongPreviewConfigSO : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Addressable address of a Runner song, or the AudioClip name of a composer track.")]
        public string songId;
        [Tooltip("Seconds into the song where the preview starts (its most recognizable section).")]
        [Min(0f)] public float previewStartTime;
        [Tooltip("Preview length in seconds. 0 = defaultPreviewDuration.")]
        [Min(0f)] public float previewDuration;
    }

    [Tooltip("Preview length for songs whose entry leaves it at 0 (or have no entry).")]
    [Min(1f)] public float defaultPreviewDuration = 30f;
    [Tooltip("Manual overrides only — a song listed here starts at its own previewStartTime instead of " +
             "the automatic centred window.")]
    public List<Entry> songs = new();

    /// <summary>Start (s) and duration (s) of `songId`'s preview, clamped to the clip length.</summary>
    public (float start, float duration) Resolve(string songId, float clipLength)
    {
        var e = string.IsNullOrEmpty(songId) ? null : songs.Find(s => s != null && s.songId == songId);
        float duration = e != null && e.previewDuration > 0f ? e.previewDuration : defaultPreviewDuration;
        if (clipLength <= 0f) return (e != null ? e.previewStartTime : 0f, duration);
        duration = Mathf.Min(duration, clipLength);                       // a song shorter than 30 s previews whole
        float start = e != null ? e.previewStartTime                      // manual override
                                : clipLength * 0.5f - duration * 0.5f;    // automatic centred window
        start = Mathf.Clamp(start, 0f, Mathf.Max(0f, clipLength - duration));
        return (start, duration);
    }
}
