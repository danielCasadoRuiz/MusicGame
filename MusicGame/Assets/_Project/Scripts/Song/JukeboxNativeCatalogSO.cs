using System.Collections.Generic;
using UnityEngine;

public class JukeboxNativeCatalogSO : ScriptableObject
{
    [System.Serializable] public class Entry { public string songId, revision, address; }
    public List<Entry> songs = new();
    public Entry Find(string id) => songs.Find(e => e.songId == id);
}
