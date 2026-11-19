using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>Offline line timings for original game songs. Negative times mean an untimed line.</summary>
[Serializable]
public sealed class SongLyrics
{
    [Serializable] public sealed class Line
    {
        public float start, end;
        public string text;
        public bool Timed => start >= 0 && end > start;
    }
    public int schemaVersion;
    public string songId, language, textSource, audioSha256;
    public float offset;
    public Line[] lines;
    private static readonly Dictionary<string, SongLyrics> Cache = new();
    public string PlainText
    {
        get
        {
            var result = new StringBuilder();
            if (lines != null) foreach (var line in lines)
                if (!string.IsNullOrWhiteSpace(line?.text)) result.AppendLine(line.text);
            return result.ToString().TrimEnd();
        }
    }
    public int LineAt(float seconds)
    {
        if (lines == null || float.IsNaN(seconds) || float.IsInfinity(seconds)) return -1;
        seconds -= offset;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i]?.Timed == true && seconds >= lines[i].start && seconds < lines[i].end) return i;
        return -1;
    }
    public static SongLyrics Parse(string json, string expectedSongId)
    {
        var data = JsonUtility.FromJson<SongLyrics>(json);
        if (data == null || data.schemaVersion != 1 || data.songId != expectedSongId || data.lines == null ||
            float.IsNaN(data.offset) || float.IsInfinity(data.offset)) throw new FormatException("Invalid song lyrics header");
        float previous = -1;
        foreach (var line in data.lines)
        {
            if (line == null || string.IsNullOrWhiteSpace(line.text) || float.IsNaN(line.start) || float.IsNaN(line.end) ||
                float.IsInfinity(line.start) || float.IsInfinity(line.end)) throw new FormatException("Invalid lyric line");
            if (line.start == -1 && line.end == -1) continue;
            if (!line.Timed || line.start < previous) throw new FormatException("Invalid lyric timing order");
            previous = line.end;
        }
        return data;
    }
    public static void Load(string id, Action<SongLyrics> complete)
    {
        if (string.IsNullOrEmpty(id) || !id.StartsWith("song_", StringComparison.Ordinal))
        {
            complete(null);
            return;
        }
        if (Cache.TryGetValue(id, out var cached))
        {
            complete(cached);
            return;
        }

        var locations = Addressables.LoadResourceLocationsAsync(SongContentKeys.LyricsAddress(id), typeof(TextAsset));
        locations.Completed += found =>
        {
            if (found.Status != AsyncOperationStatus.Succeeded || found.Result.Count == 0)
            {
                Addressables.Release(found);
                complete(null);
                return;
            }

            var asset = Addressables.LoadAssetAsync<TextAsset>(found.Result[0]);
            Addressables.Release(found);
            asset.Completed += loaded =>
            {
                SongLyrics result = null;
                try
                {
                    if (loaded.Status == AsyncOperationStatus.Succeeded)
                        Cache[id] = result = Parse(loaded.Result.text, id);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Jukebox] Lyrics unavailable for " + id + ": " + e.Message);
                }
                finally { Addressables.Release(loaded); }
                complete(result);
            };
        };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => Cache.Clear();
}
