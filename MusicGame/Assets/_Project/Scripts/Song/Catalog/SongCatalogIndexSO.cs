using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The REMOTE song index (P3): one small Addressable asset (SongContentKeys.IndexAddress, its own bundle in
/// Remote_Songs) published with every content build, so the catalog is defined by the CONTENT, not by a
/// table shipped inside the player. A content update can add a song with its identity (songId), its place
/// in the progression (order), its similarity vector and the revisions that bind its audio and profile.
///
/// Written ONLY by the editor bake (SongCatalogBake) from the published profiles, and checked by
/// SongContentValidator before any content build. Holds addresses as STRINGS (never object references), so
/// the index bundle never pulls audio or profiles in as dependencies.
///
/// Revision binding (audio and profile are separate bundles, see SongContentKeys):
///   index.masterIdentity   == profile header audioIdentity   (the analysed lossless master)
///   index.playbackRevision == profile header playbackRevision (master + import settings of the played clip)
///   index.profileCrc       == profile header bodyCrc32        (this exact profile body)
/// plus the played clip's samples/channels/frequency, checked against the loaded AudioClip — no PCM decode
/// or hash on the device. All three come from the same content build (the validator refuses to build
/// otherwise), so a stale pairing surfaces as an explicit "incompatible content" error, never silently.
/// </summary>
[CreateAssetMenu(fileName = "SongCatalogIndex", menuName = "MusicGame/Song/Song Catalog Index")]
public class SongCatalogIndexSO : ScriptableObject
{
    public const int CurrentIndexVersion = 1;

    public int indexVersion = CurrentIndexVersion;
    [Tooltip("Hash over every entry's revision fields: changes whenever any published song changes.")]
    public string contentRevision = "";
    public List<SongCatalogIndexEntry> songs = new();
}

[System.Serializable]
public class SongCatalogIndexEntry
{
    // ── Identity / addressing ──
    public string songId;
    public string displayName;
    public string audioAddress;
    public string profileAddress;
    public string contentLabel;
    [Tooltip("Catalog order (progression, listing): stable, from the songId allocation.")]
    public int    order;

    // ── Revision binding ──
    public ulong  masterIdentity;
    public ulong  playbackRevision;
    public uint   profileCrc;
    public int    profileBytes;
    public int    profileFormatVersion;
    public int    pipelineVersion;
    public ulong  paramsHash;
    public int    tagModelVersion;

    // ── Played clip (cheap runtime check against the loaded AudioClip) ──
    public float  durationSeconds;
    public int    playbackSamples;
    public int    playbackChannels;
    public int    playbackFrequency;
    [Tooltip("Informational: the load type the playback clip was published with.")]
    public string playbackLoadType = "";

    // ── Similarity (progression routes) ──
    public SongSimilarityVector similarity;

    public SongCatalogIndexEntry Clone() => (SongCatalogIndexEntry)MemberwiseClone();
}
