/// <summary>
/// Fired once, right after Song Analysis finishes and this song's MusicStyleId has been resolved
/// (see SceneBootstrap) — a NOTIFICATION for anything that reacts to "we now know the style"
/// (the future Theme system's MusicStyleVisual resolution, UI, debug tools) without needing a
/// direct dependency on whoever ran the classification.
/// </summary>
public struct MusicStyleDetectedEvent
{
    public MusicStyleId Style;
    public SongProfile  Profile;
}
