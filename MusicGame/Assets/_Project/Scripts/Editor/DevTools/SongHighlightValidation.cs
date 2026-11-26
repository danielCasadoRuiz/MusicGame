using System;
using UnityEditor;
using UnityEngine;

public static class SongHighlightValidation
{
    public static void RunFromCommandLine()
    {
        try
        {
            var app = Resources.Load<AppConfigSO>("AppConfig");
            var previews = app.songPreview;
            var core = AssetDatabase.LoadAssetAtPath<MusicRunnerCoreConfig>("Assets/_Project/Configs/Gameplay/MusicRunner/MusicRunnerCoreConfig.asset");
            if (previews.songs.Count != 22) throw new Exception("Expected 22 curated songs");
            foreach (var entry in previews.songs)
            {
                var lyrics = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/_Project/Content/JukeboxLyrics/{entry.songId}.json");
                float duration = JsonUtility.FromJson<Duration>(lyrics.text).duration;
                var preview = previews.Resolve(entry.songId, duration);
                var run = PlayRangeResolver.Resolve(core, duration, entry.songId);
                if (Mathf.Abs(preview.duration - 30) > .001f || Mathf.Abs(run.End - run.Start - 90) > .001f ||
                    preview.start < run.Start || preview.start + preview.duration > run.End || run.End > duration)
                    throw new Exception("Invalid highlight for " + entry.songId);
            }
            if (!previews.TryResolveGameplay("song_001", 12, 90, out var shortRange) || shortRange.start != 0 || shortRange.end != 12)
                throw new Exception("Short clip was not clamped");
            if (previews.TryResolveGameplay("unknown", 200, 90, out _)) throw new Exception("Unknown song should use fallback");
            var fallback = PlayRangeResolver.Resolve(core, 200, "unknown");
            if (fallback.Start != 0 || fallback.End != 90) throw new Exception("Expected 90 second fallback");
            Debug.Log("SONG HIGHLIGHTS PASS: 22 songs, 90s runs, shared 30s previews, short clips and fallback");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    [Serializable] private class Duration { public float duration; }
}
