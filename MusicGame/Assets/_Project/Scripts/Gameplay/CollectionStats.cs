using System.Collections.Generic;
using UnityEngine;

public class CollectionStats
{
    private readonly Dictionary<RingType, int> _counts = new();

    public void Register(RingType type)
    {
        _counts.TryGetValue(type, out int c);
        _counts[type] = c + 1;
    }

    public int Get(RingType type) => _counts.TryGetValue(type, out int c) ? c : 0;

    /// <summary>Removes `amount` previously-registered rings of this type from the visible
    /// count (used by the fall penalty — losing a bonus should visibly drop its counter, not
    /// just the abstract score number). Never drops below 0.</summary>
    public void Unregister(RingType type, int amount)
    {
        if (amount <= 0) return;
        _counts.TryGetValue(type, out int c);
        _counts[type] = Mathf.Max(0, c - amount);
    }

    public int Total
    {
        get { int t = 0; foreach (var v in _counts.Values) t += v; return t; }
    }

    public int Score { get; private set; }

    public void AddScore(int points) => Score += points;

    /// <summary>Defensive floor at 0 — callers (e.g. the fall penalty) only ever subtract the
    /// sum of points actually earned by real, currently-registered pickups, so this should
    /// never actually engage.</summary>
    public void SubtractScore(int points) => Score = Mathf.Max(0, Score - points);

    public void MultiplyScore(float factor) => Score = Mathf.Max(0, Mathf.RoundToInt(Score * factor));

    /// <summary>Full reset for a new run (Restart Song) — counts AND score, not just score.</summary>
    public void Reset()
    {
        _counts.Clear();
        Score = 0;
    }
}
