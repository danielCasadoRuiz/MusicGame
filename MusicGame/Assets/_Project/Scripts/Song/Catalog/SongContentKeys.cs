/// <summary>
/// Addressables naming of the PUBLISHED catalog content (P3). One place, shared by the runtime loader,
/// the editor bake/setup/validator and the probes:
///   group   Remote_Songs (remote build/load paths, PackSeparately → one bundle per entry; the built layout
///           is verified by SongContentBuild, never assumed from the group settings);
///   audio   address "SongAudio/&lt;songId&gt;" — ASCII on purpose: Addressables 2.3.1's binary catalog stores a
///           key whose characters are all ≤ U+00FF as single bytes and turns the non-ASCII ones into '?'
///           (measured: "VÅÅV - Dancig Queer" became "V??V - Dancig Queer", so the clip could never be
///           loaded by its name). The friendly name is the index's displayName. Labels "Song" (catalog
///           listing, AudioClip only) + "Song_&lt;songId&gt;" (the song's download key);
///   profile address "SongProfile/&lt;songId&gt;" (TextAsset holding the binary profile), label "Song_&lt;songId&gt;";
///   index   address "SongCatalogIndex", label "SongIndex": identity, revisions, progression order and
///           similarity of every published song — what lets a content update ADD songs without a new player.
/// Audio and profile of one song are SEPARATE bundles bound by revision (profile header ↔ index entry ↔
/// built audio), so a profile-only update never re-downloads the audio (see SongCatalogIndexSO).
/// </summary>
public static class SongContentKeys
{
    public const string GroupName     = "Remote_Songs";
    public const string SongLabel     = "Song";
    public const string IndexAddress  = "SongCatalogIndex";
    public const string IndexLabel    = "SongIndex";
    public const string ProfilePrefix = "SongProfile/";
    public const string AudioPrefix   = "SongAudio/";

    /// <summary>Download key of one song: audio + profile (same label set on both entries).</summary>
    public static string ContentLabel(string songId) => "Song_" + songId;
    public static string ProfileAddress(string songId) => ProfilePrefix + songId;
    public static string AudioAddress(string songId) => AudioPrefix + songId;

    /// <summary>Addresses must survive the binary catalog unchanged (see the class doc).</summary>
    public static bool IsSafeAddress(string address)
    {
        if (string.IsNullOrEmpty(address)) return false;
        foreach (char c in address) if (c < 0x20 || c > 0x7E) return false;
        return true;
    }

    // Editor-side locations of the versionable published assets (never the developer's personal cache).
    public const string ContentFolder  = "Assets/_Project/Content";
    public const string ProfileFolder  = "Assets/_Project/Content/SongProfiles";
    public const string IndexAssetPath = "Assets/_Project/Content/SongCatalogIndex.asset";
    public static string ProfileAssetPath(string songId) => $"{ProfileFolder}/{songId}.mgsp.bytes";
}
