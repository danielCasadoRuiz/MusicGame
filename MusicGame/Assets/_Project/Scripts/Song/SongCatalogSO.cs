using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Local, always-available list of songs shipped WITH the project (as opposed to a locally-picked
/// or streamed file) — see Section 8 of the multi-scene refactor plan. Supersedes the earlier
/// PredefinedSongLibrarySO: a real Song Selection screen needs to show every song's title/artist/
/// cover WITHOUT loading every song's audio into memory just to list them, so each entry's audio is
/// an AssetReferenceT (load-on-demand, released once no longer selected — see SongCatalogSource),
/// never a strong AudioClip reference. The catalog asset itself (this ScriptableObject) stays local/
/// always-available — only the audio CONTENT is Addressable, same "config vs content" split as the
/// Theme registries.
/// </summary>
[CreateAssetMenu(fileName = "SongCatalog", menuName = "MusicGame/Song/Song Catalog")]
public class SongCatalogSO : ScriptableObject
{
    [Serializable]
    public struct SongDefinition
    {
        public string id;
        public string title;
        public string artist;
        public Sprite cover;
        public AssetReferenceT<AudioClip> audioClip;
    }

    public SongDefinition[] songs = Array.Empty<SongDefinition>();
}
