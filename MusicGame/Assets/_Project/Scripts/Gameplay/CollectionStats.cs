using System.Collections.Generic;

public class CollectionStats
{
    private readonly Dictionary<RingType, int> _counts = new();

    public void Register(RingType type)
    {
        _counts.TryGetValue(type, out int c);
        _counts[type] = c + 1;
    }

    public int Get(RingType type) => _counts.TryGetValue(type, out int c) ? c : 0;

    public int Total
    {
        get { int t = 0; foreach (var v in _counts.Values) t += v; return t; }
    }
}
