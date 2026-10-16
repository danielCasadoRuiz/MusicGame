public enum EventType { Ring }

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
    public float     eventTime;       // MusicClock.SongTime when player reaches this event
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
}
