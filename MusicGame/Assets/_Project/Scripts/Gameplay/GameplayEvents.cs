using System.Collections.Generic;
using UnityEngine;

// WarmupTime lets a countdown UI (CountdownController) show exactly as many "3, 2, 1, GO"-style
// ticks as config.core.warmupTime actually calls for, instead of assuming a hardcoded duration —
// GameplayManager publishes this right before its own WaitForSeconds(warmupTime) before playing
// the song, so the countdown and the real warmup are driven by the same single config value.
public struct GameStartedEvent      { public float WarmupTime; }
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
    // Combat resources earned this run (Triple/Quad combos, Lives, Specials) — NOT score. See
    // RunnerResourceTracker.
    public RunnerResourceCounts Resources;
}
// Fired once, exactly when the song's fade-out finishes (SongPlayEnd reached) — the song has
// genuinely ended, but the run keeps going through the silent/flat farewell stretch (see
// GameplayManager's own ending sequence's real elapsed-time countdown, _farewellElapsed) until
// GameEndedEvent actually replaces the live HUD with the end screen. FinishBannerController's
// "FINISH!" banner reacts to this directly.
public struct SongFinishedEvent { }
/// <summary>RingCount = MUSICAL pickups the player can actually meet (played window; Life/Special
/// excluded). AvailableByType = pickups of each type the
/// player can ACTUALLY meet this run (inside the played window) — a type with no entry (or 0)
/// does not exist in this song's gameplay (the HUD hides it; performance treats it as N/A).</summary>
public struct LevelGeneratedEvent
{
    public int RingCount;
    public IReadOnlyDictionary<RingType, int> AvailableByType;
}

/// <summary>Fired whenever TotalScore or the visible per-type collected counters change (pickup,
/// fall penalty, no-fall bonus, restart) — the HUD refreshes from this instead of polling.</summary>
public struct RunnerStatsChangedEvent { public CollectionStats Stats; }

/// <summary>Fired whenever Triple/Quad combo counts, Lives or Specials change.</summary>
public struct RunnerResourcesChangedEvent { public RunnerResourceCounts Counts; }

/// <summary>Fired the moment a pickup sequence resolves to a Triple or Quad combo.</summary>
public struct PickupComboEvent { public PickupComboTier Tier; public RunnerResourceCounts Counts; }

/// <summary>Pickup-combo sequence progress (feedback only): Count = pickups in the open sequence
/// (0 = none open); Broken = an open sequence just ended without becoming a Triple/Quad.</summary>
public struct PickupComboProgressEvent { public int Count; public bool Broken; }

/// <summary>Fired by GameSession right after it finalized RunnerResults (musical performance,
/// resources, combat build) from GameEndedEvent — the results screen reads the conversion from
/// here, never by recomputing it.</summary>
public struct RunnerResultsReadyEvent { public RunnerResults Results; }
public struct RingCollectedEvent
{
    public RingType Type;
    public Vector3  Position;
    public float    TimingError;   // |ActualTime - ExpectedTime|
    public float    ExpectedTime;  // eventTime + the player's trail (MusicRunnerCoreConfig.playerTrailSeconds) — when a non-surging player meets it
    public float    ActualTime;    // MusicClock.SongTime when the player actually touched it
    public float    Strength;      // TimelineEvent.strength
    public float    Confidence;    // TimelineEvent.confidence — classification certainty (see GameplayTimeline.ClassifyOnset), or 1.0 for a Macro-as-collectible (Impact/Peak)
    public string   Contributors;  // TimelineEvent.contributors — e.g. "Kick" or "Impact+Peak" (climax-tagged)
    public bool     IsOffTrack;    // TimelineEvent.isOffTrack — decided at generation time, never inferred here
    public float    ScoreMultiplier; // TimelineEvent.scoreMultiplier — off-track / height difficulty reward
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
// Cause = WHY the respawn penalty fired (a real fall, the chaser's contact, trapped by an obstacle) — kept
// distinct so each can be tuned separately (RunnerHazardsConfig penalty scales); Position = where it happened.
public struct PlayerFellEvent       { public float FallSongTime; public RunnerPenaltyCause Cause; public Vector3 Position; }

public enum RunnerPenaltyCause
{
    Fall,
    ChaserContact,
    Trapped,
}

/// <summary>Published once per penalty with what was REALLY lost (score + collected pickups removed) and
/// where — the hook for a future "pickups knocked out and bouncing" effect. Pickups lost can be 0.</summary>
public struct RunnerPenaltyEvent
{
    public RunnerPenaltyCause Cause;
    public Vector3 Position;
    public float   SongTime;
    public int     ScoreLost;
    public int     PickupsLost;
}
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
/// Fired the instant MusicClock.SongTime reaches a timeline event's musical moment (minus
/// pulseLeadTime) — i.e. when that onset sounds in the song, regardless of whether the player
/// has reached or collected the corresponding ring. Drives beat-synced feedback
/// (ring pulse, camera kick) so the world visibly reacts on the beat itself.
///
/// EventIndex + EventTime are the pulse's musical IDENTITY: the timeline index (unique per run,
/// -1 = unknown) and the moment's eventTime (SongTime domain, NOT the frame it was processed in).
/// Consumers that must not stack coinciding moments (CameraFollow's kick) deduplicate on these —
/// never on "arrived in the same frame": a slow frame can legitimately carry several real hits.
/// </summary>
public struct BeatPulseEvent
{
    public RingType Type;
    public float    Strength;
    public Vector3  Position;
    public int      EventIndex;
    public float    EventTime;
}

/// <summary>
/// Published by MusicClock whenever the musical timeline jumps instead of flowing: a new run
/// (Initialize — fresh start, restart, song change), the audio starting after the warmup, an
/// explicit seek (fall-respawn) or an automatic resync after a stall. Anything holding per-moment
/// state (live detector histories/cooldowns, pulse dedup, cursors) resets on it. Times are
/// SongTime (FromSongTime/ToSongTime) and clip time (ToClipTime) — see MusicTime.
/// </summary>
public struct MusicTimeDiscontinuityEvent
{
    public MusicTimeDiscontinuityReason Reason;
    public float FromSongTime;
    public float ToSongTime;
    public float ToClipTime;
    public int   Generation;
}

/// <summary>
/// Fired by CameraFollow whenever the view mode actually changes (UI button OR the 'V' debug
/// key — both call CameraFollow.ToggleView(), there is only one path). Purely informational: the
/// HUD's view-toggle button subscribes to this to refresh its own icon/text, independent of
/// which input triggered the change. Camera positioning/transition itself is NOT driven by this
/// event — CameraFollow owns that directly.
/// </summary>
public struct CameraViewChangedEvent { public CameraViewMode Mode; }
