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
    public float     verticalOffset;  // path-local up units above the path surface
    // The portion of verticalOffset that MUST clear the collectible's own max-scale half-height
    // (see GameplayTimeline.CollectibleMaxHalfHeight) — GameplayManager.ActivateEvent places
    // exactly this much along the TRUE surface normal (correct even on a tilted/sloped bump),
    // and only the remainder (verticalOffset - floorClearance, extra jump-gameplay height)
    // along the path's stable up direction. See ActivateEvent's own comment for why the split
    // matters: using path-up for the WHOLE offset on a tilted surface can under-clear the real
    // surface; using surface-normal for the WHOLE offset can shove a tall jump sideways.
    public float     floorClearance;
    public float     strength;        // 0..1 musical signal strength (post-fusion)
    public string    sourceFeature;   // dominant raw signal: "Kick", "Snare", "HiHat", "Beat", "Onset", "Impact", "Peak"
    public float     confidence;      // 0..1 — how many independent raw signals agreed this is a real moment
    public string    contributors;    // e.g. "Kick+Impact" — every raw signal fused into this event
}
