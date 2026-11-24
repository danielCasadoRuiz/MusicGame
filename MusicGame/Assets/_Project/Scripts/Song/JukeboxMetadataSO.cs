using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional authored metadata for our songs only. Never used for imported lyrics.</summary>
[CreateAssetMenu(menuName = "MusicGame/Song/Jukebox Metadata", fileName = "JukeboxMetadata")]
public class JukeboxMetadataSO : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string songId;
        public string title;
        public string artist;
        public Sprite cover;
        [Min(0f)] public float durationSeconds;
        [TextArea(6, 30)] public string lyrics;
    }
    public List<Entry> songs = new();
    public Entry Find(string id) => songs.Find(e => e != null && e.songId == id);
}
