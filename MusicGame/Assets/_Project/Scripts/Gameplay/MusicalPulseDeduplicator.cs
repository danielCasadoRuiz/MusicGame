using System.Collections.Generic;

/// <summary>
/// Decides how much of a BeatPulseEvent's strength a cumulative effect (CameraFollow's kick) should
/// still add, deduplicating by MUSICAL identity — never by arrival frame, and with no delay:
///   • the same event (EventIndex) arriving twice adds nothing;
///   • a different event whose eventTime lies within MergeWindow of an already-applied moment is
///     the same audible hit (e.g. an Impact plus a confident Kick on one onset): only the strength
///     above what that moment already applied is added (max, not sum);
///   • distinct musical times always add in full, even when a slow frame delivers several of
///     them together — those are legitimately separate hits.
/// The result is applied immediately (no grouping window). Reset() on every timeline
/// discontinuity (MusicTimeDiscontinuityEvent): after a seek/respawn/restart the same event
/// indices legitimately pulse again.
/// </summary>
public sealed class MusicalPulseDeduplicator
{
    /// <summary>Applied moments older/newer than this (s, event time) are forgotten.</summary>
    public const float RetentionSeconds = 1f;

    private struct Moment { public float time; public float strength; }

    private readonly List<Moment>  _moments = new();
    private readonly HashSet<int>  _seenIds = new();

    public float MergeWindow { get; set; }

    public MusicalPulseDeduplicator(float mergeWindow) => MergeWindow = mergeWindow;

    /// <summary>Returns the strength to add for this pulse (0 = fully duplicate).</summary>
    public float Accept(int eventIndex, float eventTime, float strength)
    {
        if (eventIndex >= 0 && !_seenIds.Add(eventIndex)) return 0f;

        // Forget moments far from this one (keeps the list tiny; also handles any backward jump
        // that arrives without a Reset).
        for (int i = _moments.Count - 1; i >= 0; i--)
            if (System.Math.Abs(_moments[i].time - eventTime) > RetentionSeconds) _moments.RemoveAt(i);

        float window = System.Math.Max(0f, MergeWindow);
        for (int i = 0; i < _moments.Count; i++)
        {
            var m = _moments[i];
            if (System.Math.Abs(m.time - eventTime) > window) continue;
            float extra = System.Math.Max(0f, strength - m.strength);
            if (extra > 0f) { m.strength = strength; _moments[i] = m; }
            return extra;
        }

        _moments.Add(new Moment { time = eventTime, strength = strength });
        return strength;
    }

    public void Reset()
    {
        _moments.Clear();
        _seenIds.Clear();
    }
}
