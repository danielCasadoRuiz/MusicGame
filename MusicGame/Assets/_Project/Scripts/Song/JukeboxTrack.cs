using System.Collections.Generic;
using UnityEngine;

public class JukeboxTrack
{
    public string id, title, artist, lyrics, path, address;
    public GameMusicStyle style;
    public Sprite cover;
    public SongLyrics lyricData;
    public float durationSeconds;
    public bool Imported => !string.IsNullOrEmpty(path);
    public bool Unlocked => PlayerProgressService.Instance?.IsJukeboxUnlocked(id) == true;

    public static List<JukeboxTrack> Build()
    {
        var all = new Dictionary<string, JukeboxTrack>();
        var metadata = Resources.Load<JukeboxMetadataSO>("JukeboxMetadata");
        void Add(string id, string name, string address, GameMusicStyle style, float duration = 0)
        {
            if (string.IsNullOrEmpty(id)) return;
            SplitName(name ?? id, out string artist, out string title);
            var m = metadata != null ? metadata.Find(id) : null;
            all[id] = new JukeboxTrack { id = id, title = string.IsNullOrEmpty(m?.title) ? title : m.title,
                artist = string.IsNullOrEmpty(m?.artist) ? artist : m.artist, address = address, style = style,
                lyrics = m?.lyrics, cover = m?.cover, durationSeconds = duration > 0 ? duration : m?.durationSeconds ?? 0 };
        }
        var app = Resources.Load<AppConfigSO>("AppConfig");
        if (app != null && app.songCatalog != null)
            foreach (var e in app.songCatalog.songs)
                if (e != null) Add(e.songId, e.address, SongContentKeys.AudioAddress(e.songId), GameMusicStyle.Unknown);
        foreach (var e in SongCatalogService.Entries) Add(e.songId, e.displayName, e.audioAddress, e.gameStyle, e.durationSeconds);
        foreach (var e in ImportedAudioLibrary.Tracks)
            if (PlayerProgressService.Instance?.IsJukeboxUnlocked(e.id) == true) all[e.id] = new JukeboxTrack { id = e.id, title = e.title, path = ImportedAudioLibrary.PathFor(e), style = e.style, durationSeconds = e.durationSeconds };
        var result = new List<JukeboxTrack>();
        var progress = PlayerProgressService.Instance;
        if (progress != null)
            foreach (string id in progress.JukeboxUnlockedIds)
                if (all.TryGetValue(id, out var track)) { result.Add(track); all.Remove(id); }
        // The Jukebox is a collection of won songs only, in first-unlock order.
        return result;
    }
    public static void SplitName(string name, out string artist, out string title)
    {
        int split = name.IndexOf(" — ", System.StringComparison.Ordinal);
        int length = 3;
        if (split < 0) split = name.IndexOf(" - ", System.StringComparison.Ordinal);
        if (split < 0) { artist = ""; title = name; return; }
        artist = name.Substring(0, split).Trim();
        title = name.Substring(split + length).Trim();
    }
}
