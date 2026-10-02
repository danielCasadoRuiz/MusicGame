/// <summary>
/// Fired once, right after Song Analysis finishes and this song's MusicStyleId has been resolved
/// (see RunnerSceneBootstrap) — a NOTIFICATION for anything that reacts to "we now know the style"
/// (the future Theme system's MusicStyleVisual resolution, UI, debug tools) without needing a
/// direct dependency on whoever ran the classification.
/// </summary>
public struct MusicStyleDetectedEvent
{
    /// <summary>Visual-theme key of the detected style (legacy enum — themes / runner animations).</summary>
    public MusicStyleId Style;
    /// <summary>The game-facing style (MusicStyleResolver) — what the player is told.</summary>
    public GameMusicStyle GameStyle;
    public SongProfile  Profile;
}
