/// <summary>
/// Fired ONCE per song, with the FINAL style (early only when the tags were conclusive, else after the full analysis)
/// — a NOTIFICATION for anything that reacts to "we now know the style"
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

/// <summary>Tags were not conclusive (see MusicStyleResolver.ResolveEarly): the style stays pending
/// until the full analysis — nothing user-facing changes yet.</summary>
public struct MusicStylePendingEvent { }

/// <summary>The Analyzing screen finished presenting the final style ("Style detected: X") —
/// SongAnalysisController waits for this (with a timeout fallback) before entering Gameplay.</summary>
public struct MusicStyleRevealFinishedEvent { }
