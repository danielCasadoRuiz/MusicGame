using UnityEngine;

public struct GameStartedEvent      { }
public struct GameEndedEvent
{
    public CollectionStats Stats;
    public int             FallCount;
    // earned / maxPossible weighted score for THIS song's actual generated timeline (perfect
    // timing + zero falls on every generated event = 1.0) — see GameplayManager.ComputeMaxPossibleScore.
    // Comparable across songs regardless of how many events a given song happened to generate.
    public float            NormalizedScore;
    public int              MaxPossibleScore;
    // Per-type breakdown (available/collected/collectionRate/normalizedScore/timing) — see
    // GamePerformance.cs. Built strictly from this song's actual playable timeline.
    public GamePerformance   Performance;
}
public struct LevelGeneratedEvent   { public int RingCount; }
public struct RingCollectedEvent
{
    public RingType Type;
    public Vector3  Position;
    public float    TimingError;   // |ActualTime - ExpectedTime|
    public float    ExpectedTime;  // TimelineEvent.eventTime — the musical moment this was generated for
    public float    ActualTime;    // MusicClock.SongTime when the player actually touched it
    public float    Strength;      // TimelineEvent.strength
    public float    Confidence;    // TimelineEvent.confidence — classification certainty (see GameplayTimeline.ClassifyOnset), or 1.0 for a Macro-as-collectible (Impact/Peak)
    public string   Contributors;  // TimelineEvent.contributors — e.g. "Kick" or "Impact+Peak" (climax-tagged)
}

/// <summary>
/// Fired the instant MusicClock.SongTime reaches a MACRO moment's eventTime (Impact, Drop,
/// BuildupStart, or a standalone Peak) — independent of whether it also became a collectible.
/// Consumed by things like MusicEnvironmentController to consolidate/react to structural
/// musical turning points, without those systems needing to know anything about collectibles.
/// </summary>
public struct MacroEventOccurredEvent
{
    public MacroEventType Type;
    public float          Strength;
    public bool           IsClimax;
}
public struct MusicPathReadyEvent   { public MusicPath Path; }
public struct CheckpointReachedEvent { public int Index; }
// FallSongTime = MusicClock.SongTime at the EXACT moment of the fall — the single source both
// the respawn (FallRespawnSystem) and the score-protection calculation (GameplayManager) read,
// so "where you respawn" and "what counts as protected" can never disagree.
public struct PlayerFellEvent       { public float FallSongTime; }
// CheckpointIndex is a legacy name kept to avoid touching every consumer: 0 still means "a full
// restart happened" (GameplayHUD/PauseController un-freeze on this); -1 means "a normal mid-song
// fall-respawn happened" (no discrete checkpoint involved anymore).
public struct PlayerRespawnedEvent  { public int CheckpointIndex; }

/// <summary>
/// Fired when the player presses "Continue" on the end-of-song results screen.
/// Stub for now — no next gameplay/scene is wired up yet; a future system can subscribe
/// to this to drive whatever comes after the runner (next song, menu, etc.).
/// </summary>
public struct ContinuePressedEvent  { public CollectionStats Stats; }

/// <summary>
/// Fired the instant MusicClock.SongTime reaches a timeline event's musical moment —
/// i.e. exactly when that onset sounds in the song, regardless of whether the player
/// has reached or collected the corresponding ring. Drives beat-synced feedback
/// (ring pulse, camera kick) so the world visibly reacts on the beat itself.
/// </summary>
public struct BeatPulseEvent { public RingType Type; public float Strength; public Vector3 Position; }

/// <summary>
/// Fired by CameraFollow whenever the view mode actually changes (UI button OR the 'V' debug
/// key — both call CameraFollow.ToggleView(), there is only one path). Purely informational: the
/// HUD's view-toggle button subscribes to this to refresh its own icon/text, independent of
/// which input triggered the change. Camera positioning/transition itself is NOT driven by this
/// event — CameraFollow owns that directly.
/// </summary>
public struct CameraViewChangedEvent { public CameraViewMode Mode; }
