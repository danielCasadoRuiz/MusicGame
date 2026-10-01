using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A rolling window of recent FightInputEvents — old entries are dropped as soon as they fall
/// outside windowSeconds, so this never grows unbounded. FightComboRecognizer reads Events after
/// every Add() to check for a match; nothing else needs to touch this directly.
/// </summary>
public class FightInputBuffer
{
    private readonly List<FightInputEvent> _events = new();
    private readonly float _windowSeconds;
    private readonly int _maxEntries;

    public FightInputBuffer(float windowSeconds, int maxEntries = 24)
    {
        _windowSeconds = Mathf.Max(0.1f, windowSeconds);
        _maxEntries = Mathf.Max(4, maxEntries);
    }

    public IReadOnlyList<FightInputEvent> Events => _events;

    public void Add(FightInputEvent inputEvent)
    {
        _events.Add(inputEvent);
        Prune();
    }

    private void Prune()
    {
        float cutoff = Time.time - _windowSeconds;
        int removeCount = 0;
        while (removeCount < _events.Count && _events[removeCount].Time < cutoff)
            removeCount++;
        removeCount = Mathf.Max(removeCount, _events.Count - _maxEntries); // bounded by count too
        if (removeCount > 0) _events.RemoveRange(0, removeCount);
    }
}
