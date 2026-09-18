using System;
using System.Collections;

/// <summary>
/// Abstraction over "get me a playable song" — Song Selection depends only on this, never on
/// AudioClip-loading details. Concrete sources today: SongCatalogSource (a song from SongCatalogSO)
/// and LocalFileSongSource (a user-picked WAV/MP3). Future sources (Spotify, YouTube
/// Music, Amazon Music...) implement the exact same interface — nothing downstream (GameSession,
/// Song Analysis) needs to know or care which kind of source actually produced a song.
///
/// Coroutine-based (not a plain synchronous call) since loading genuinely takes time — a local
/// file needs decoding, a predefined song is already an AudioClip reference but stays consistent
/// with the same shape as every other source.
/// </summary>
public interface ISongSource
{
    /// <summary>Display-only — never used for lookup/branching logic.</summary>
    string DisplayName { get; }

    /// <summary>Invoked exactly once with the loaded song, or null if the source couldn't produce
    /// one (user cancelled a file picker, load failed, etc.) — callers must treat null as "nothing
    /// selected", never as an error to crash on.</summary>
    IEnumerator Load(Action<SelectedSongInfo?> onComplete);
}
