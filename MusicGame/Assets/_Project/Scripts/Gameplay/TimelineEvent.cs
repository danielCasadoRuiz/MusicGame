public enum EventType { Ring }

/// <summary>Where a pickup comes from — kept on every event so diagnostics (and the player-facing
/// pulses) never present a synthetic or structural item as a detected hit.</summary>
public enum BonusOrigin : byte
{
    /// <summary>A detected attack of the profile (SongProfile.onsetEvents[onsetIndex]).</summary>
    Detected      = 0,
    /// <summary>Gap filler on a TRACKED beat (BonusSelectionSettings.syntheticFill) — not a detection.</summary>
    SyntheticGrid = 1,
    /// <summary>A structural moment (Impact / Peak) from the profile's dynamics.</summary>
    Structural    = 2,
    /// <summary>Combat resources (Life / Special) — not musical.</summary>
    Resource      = 3,
    /// <summary>Old single-label classifier (a profile without onsetEvents).</summary>
    Legacy        = 4,
}

/// <summary>
/// A single pre-calculated gameplay event anchored to a musical moment.
/// World position is NOT stored — it is derived at activation time:
///   pos = path.GetSample(eventDistance).position
///       + path.right * lateralOffset
///       + path.up    * verticalOffset
///
/// eventDistance = eventTime * unitsPerSecond
/// eventTime     = MusicClock.SongTime at which the player should reach the event
///               = config.warmupTime + onsetTime
///
/// lateralOffset/verticalOffset are decided ONCE, here, when the timeline is generated —
/// never re-rolled when the event's pooled GameObject is activated/recycled.
/// </summary>
[System.Serializable]
public struct TimelineEvent
{
    public float     eventTime;       // MusicClock.SongTime of the musical moment (the musical front reaches it; the player meets it playerTrailSeconds later)
    public float     eventDistance;   // = eventTime * unitsPerSecond (path arc-length)
    public EventType eventType;
    public RingType  ringType;
    public float     lateralOffset;   // continuous, path-local right units (negative = left)
    public float     verticalOffset;  // centre height (m, world up) above the real local ground at this spot
    // Minimum clearance (pickup bottom above the surface) the generator reserved for this type —
    // informational since placement became ground-true (GameplayManager.PickupWorldPosition puts the
    // centre verticalOffset straight above the REAL local ground and keeps the bottom clear itself).
    public float     floorClearance;
    // Decided ONCE at generation time (GameplayTimeline.EmitSingle) — never inferred later from
    // lateralOffset/track width at collection time. Carried through Activate() into
    // RingController's own _sourceEvent, which is what RingCollectedEvent.IsOffTrack reads.
    public bool      isOffTrack;
    public float     scoreMultiplier; // difficulty reward decided at generation: off-track x2, else height x1..x1.5 (0 = treat as 1)
    public float     strength;        // 0..1 musical signal strength (post-fusion)
    public string    sourceFeature;   // dominant raw signal: "Kick", "Snare", "HiHat", "Beat", "Onset", "Impact", "Peak"
    public float     confidence;      // 0..1 — how many independent raw signals agreed this is a real moment
    public string    contributors;    // e.g. "Kick+Impact" — every raw signal fused into this event
    public BonusOrigin origin;        // detected / synthetic / structural / resource (see BonusOrigin)
    public int       onsetIndex;      // Detected: index into SongProfile.onsetEvents; −1 otherwise
}
