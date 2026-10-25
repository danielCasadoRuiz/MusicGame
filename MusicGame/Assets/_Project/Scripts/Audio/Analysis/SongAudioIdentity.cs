using System;
using System.Collections.Generic;
using System.IO;
using MusicGame.Audio.Analysis;
using UnityEngine;

/// <summary>How the audio identity of an analysed song was established.</summary>
public enum SongAudioIdentityKind : byte
{
    None        = 0,
    /// <summary>Editor: hash of the source asset bytes + the import settings that shape the decoded PCM.</summary>
    SourceAsset = 1,
    /// <summary>Imported song: hash of the picked file's bytes.</summary>
    ImportedFile = 2,
    /// <summary>Fallback: hash of the decoded mono PCM in memory (+ channels/rate). Device-local only —
    /// decoders may differ per platform — so it is only used for the local analysis cache.</summary>
    DecodedPcm  = 3,
    /// <summary>Editor bake (P3): hash of the lossless master file bytes + its normalisation policy — the
    /// analysed audio of a PUBLISHED profile (the played representation is bound separately: playbackRevision).</summary>
    Master      = 4,
}

/// <summary>
/// Identity of the analysed AUDIO REVISION (replaces the old "clip name + sample count" key, which
/// two different revisions of the same length shared). `Hash` goes into the profile header
/// (audioIdentity) and the cache file name; `SongKey` names the song (catalog songId, or an
/// import/clip key) so a new revision replaces the previous cache entry of the same song.
/// </summary>
public readonly struct SongAudioIdentity
{
    public readonly SongAudioIdentityKind Kind;
    public readonly string SongKey;
    public readonly ulong  Hash;

    public SongAudioIdentity(SongAudioIdentityKind kind, string songKey, ulong hash) { Kind = kind; SongKey = songKey; Hash = hash; }

    public bool IsValid => Kind != SongAudioIdentityKind.None && Hash != 0UL;
    public override string ToString() => $"{SongKey} [{Kind} {Hash:x16}]";
}

/// <summary>
/// What must be hashed to identify a song's audio, captured on the MAIN thread (paths, settings);
/// <see cref="Resolve"/> does the actual hashing and runs on a WORKER (reading a 40–70 MB file).
/// Results of file hashes are memoised per (path, length, write time, settings) for the session, so
/// a second analysis of the same file does not re-read it.
/// </summary>
public sealed class SongAudioIdentitySource
{
    public SongAudioIdentityKind Kind;
    public string SongKey;
    public string FilePath;      // SourceAsset / ImportedFile
    public ulong  SettingsHash;  // SourceAsset: import settings that shape the decoded PCM

    /// <summary>Editor hook (set by EditorSongAudioIdentity): clip → its source asset path + import
    /// settings hash, or null when the clip is not a project asset (e.g. loaded from a bundle).</summary>
    public static Func<AudioClip, (string path, ulong settingsHash)?> EditorAssetResolver;

    private static readonly Dictionary<string, ulong> s_memo = new();
    private static readonly object s_memoLock = new();

    /// <summary>MAIN THREAD. Picks the cheapest valid identity for this selection: the imported file,
    /// the editor source asset, else the decoded PCM (resolved after capture).</summary>
    public static SongAudioIdentitySource For(AudioClip clip, string songId, string localFilePath)
    {
        if (!string.IsNullOrEmpty(localFilePath) && File.Exists(localFilePath))
            return new SongAudioIdentitySource { Kind = SongAudioIdentityKind.ImportedFile, SongKey = "import", FilePath = Path.GetFullPath(localFilePath) };

        string key = !string.IsNullOrEmpty(songId) ? songId : "clip_" + (clip != null ? clip.name : "unknown");
        var asset = clip != null ? EditorAssetResolver?.Invoke(clip) : null;
        if (asset.HasValue && !string.IsNullOrEmpty(asset.Value.path) && File.Exists(asset.Value.path))
            return new SongAudioIdentitySource { Kind = SongAudioIdentityKind.SourceAsset, SongKey = key, FilePath = Path.GetFullPath(asset.Value.path), SettingsHash = asset.Value.settingsHash };

        return new SongAudioIdentitySource { Kind = SongAudioIdentityKind.DecodedPcm, SongKey = key };
    }

    /// <summary>True when the identity needs the decoded PCM (resolved only after the capture).</summary>
    public bool NeedsPcm => Kind == SongAudioIdentityKind.DecodedPcm;

    /// <summary>WORKER. File-based identities (memoised). Throws on an unreadable file.</summary>
    public SongAudioIdentity Resolve()
    {
        if (NeedsPcm) throw new InvalidOperationException("PCM identity: use ResolvePcm after the capture");
        var fi = new FileInfo(FilePath);
        string memoKey = $"{(int)Kind}|{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{SettingsHash:x16}";
        lock (s_memoLock) if (s_memo.TryGetValue(memoKey, out var known)) return Make(known);

        var h = StableHash.Create();
        h.Add("MusicGame.AudioIdentity");
        h.Add((int)Kind);
        h.Add(fi.Length);
        h.Add(SettingsHash);
        var buf = new byte[1 << 20];
        using (var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        {
            int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0) h.Add(buf, 0, n);
        }
        ulong v = h.Value;
        lock (s_memoLock) s_memo[memoKey] = v;
        return Make(v);
    }

    // Imported songs have no catalog id: the content hash itself names the cache entry.
    private SongAudioIdentity Make(ulong v)
    {
        string key = Kind == SongAudioIdentityKind.ImportedFile ? $"import_{v:x16}" : SongKey;
        return new SongAudioIdentity(Kind, key, v);
    }

    /// <summary>WORKER. Identity of decoded audio held in memory.</summary>
    public SongAudioIdentity ResolvePcm(float[] mono, int channels, int frequency)
        => new SongAudioIdentity(SongAudioIdentityKind.DecodedPcm, SongKey, StableHash.HashPcm(mono, channels, frequency));
}
