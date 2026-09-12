/// <summary>
/// A structural/macro musical moment (Impact, Drop, BuildupStart, or a standalone Peak).
///
/// Distinct from TimelineEvent: a MacroEvent does NOT necessarily correspond to a spawned
/// GameObject. Only Impact currently also becomes a placed collectible (see
/// GameplayTimeline.Generate); Drop/BuildupStart/standalone-Peak are pure signals consumed by
/// GameplayManager (fires MacroEventOccurredEvent at eventTime) and MusicEnvironmentController
/// (background mood) — not invented as visual objects of their own in this iteration.
/// </summary>
public struct MacroEvent
{
    public float          eventTime;     // matches MusicClock.SongTime when this happens
    public float          eventDistance; // = eventTime * unitsPerSecond (path arc-length)
    public MacroEventType type;
    public float          strength;      // 0..1
    public bool           isClimax;      // true if the song's single Peak coincides with this moment
}
