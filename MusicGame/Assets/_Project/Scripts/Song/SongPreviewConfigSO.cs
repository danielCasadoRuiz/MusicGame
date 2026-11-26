using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Curated per-song preview and Runner start times, keyed by the existing song id.
/// Song Selection and Jukebox resolve the same 30-second preview. Runner uses a longer
/// window with a lead-in to the same main hook. Unlisted previews use the centred window.
/// </summary>
[CreateAssetMenu(fileName = "SongPreviewConfig", menuName = "MusicGame/Song/Song Preview Config")]
public class SongPreviewConfigSO : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("songId of a Runner song (song_NNN), or the AudioClip name of a composer track.")]
        public string songId;
        [Tooltip("Seconds into the song where the preview starts (its most recognizable section).")]
        [Min(0f)] public float previewStartTime;
        [Tooltip("Preview length in seconds. 0 = defaultPreviewDuration.")]
        [Min(0f)] public float previewDuration;
        [Tooltip("Runner start in clip seconds, chosen to include the main hook with its lead-in.")]
        [Min(0f)] public float gameplayStartTime;
    }

    [Tooltip("Preview length for songs whose entry leaves it at 0 (or have no entry).")]
    [Min(1f)] public float defaultPreviewDuration = 30f;
    [Tooltip("Manual overrides only — a song listed here starts at its own previewStartTime instead of " +
             "the automatic centred window.")]
    public List<Entry> songs = new();

    public bool TryResolveGameplay(string songId, float clipLength, float duration, out (float start, float end) range)
    {
        var entry = songs.Find(s => s != null && s.songId == songId);
        if (entry == null || string.IsNullOrEmpty(songId)) { range = default; return false; }
        duration = Mathf.Clamp(duration, 0f, Mathf.Max(0f, clipLength));
        float start = Mathf.Clamp(entry.gameplayStartTime, 0f, Mathf.Max(0f, clipLength - duration));
        range = (start, start + duration);
        return true;
    }

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
