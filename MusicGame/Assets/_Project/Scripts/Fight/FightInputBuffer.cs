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

    public FightInputBuffer(float windowSeconds) => _windowSeconds = Mathf.Max(0.1f, windowSeconds);

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
        if (removeCount > 0) _events.RemoveRange(0, removeCount);
    }
}
