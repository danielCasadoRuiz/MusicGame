using UnityEngine;

/// <summary>Authoring-only song presentation. Runtime receives scalar metadata through the published index.</summary>
[CreateAssetMenu(fileName="SongMetadata",menuName="MusicGame/Song/Song Metadata")]
public sealed class SongMetadataSO : ScriptableObject
{
    public string songId;
    public string title;
    public string artist;
    [Tooltip("Publication year of the original reference recording; zero means unknown.")]
    public int releaseYear;
    public Sprite artwork;
    public AudioClip audio;
    public GameMusicStyle style;
    public string referenceTitle;
    public string referenceArtist;
    public string metadataSource;
    public int Decade => releaseYear>=1970&&releaseYear<2030?releaseYear/10*10:0;
}
