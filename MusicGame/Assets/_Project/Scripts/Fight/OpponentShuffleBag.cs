using System.Collections.Generic;

/// <summary>
/// Opponent selection: a shuffle bag with a recent-opponent cooldown. The bag holds every roster
/// opponent once, in shuffled order; opponents are drawn from it one by one and it is refilled +
/// reshuffled when empty — so within one bag every opponent appears exactly once. The cooldown
/// additionally forbids picking an opponent until `cooldown` OTHER opponents have appeared since
/// its last pick (this is what protects bag boundaries: the last of one bag can never open the
/// next). If the cooldown can't be honoured (roster smaller than cooldown + 1) it relaxes to "not
/// the same as the previous pick", then to "anything".
///
/// State is plain id lists ([Serializable]) so it survives scene loads inside GameSession and can
/// later be written to a save game unchanged. Opponent identity = OpponentDefinition.id.
/// </summary>
[System.Serializable]
public class OpponentShuffleBag
{
    /// <summary>Ids still to be drawn in the current bag, in draw order.</summary>
    public List<string> bag = new();
    /// <summary>Most recent picks, oldest first.</summary>
    public List<string> recent = new();

    public IReadOnlyList<string> Remaining => bag;
    public IReadOnlyList<string> Recent => recent;

    public OpponentDefinition Next(IReadOnlyList<OpponentDefinition> roster, int cooldown, System.Random rng)
    {
        if (roster == null || roster.Count == 0) return null;
        var byId = new Dictionary<string, OpponentDefinition>();
        foreach (var o in roster)
            if (o != null && !string.IsNullOrEmpty(o.id) && !byId.ContainsKey(o.id)) byId[o.id] = o;
        if (byId.Count == 0) return null;

        bag.RemoveAll(id => !byId.ContainsKey(id)); // roster changed since the bag was filled
        if (bag.Count == 0) Refill(byId.Keys, rng);

        cooldown = System.Math.Max(0, System.Math.Min(cooldown, byId.Count - 1));
        int index = FindIndex(id => !InRecent(id, cooldown));
        if (index < 0) index = FindIndex(id => recent.Count == 0 || recent[recent.Count - 1] != id);
        if (index < 0) index = 0;

        string pick = bag[index];
        bag.RemoveAt(index);
        recent.Add(pick);
        int keep = System.Math.Max(1, cooldown);
        if (recent.Count > keep) recent.RemoveRange(0, recent.Count - keep);
        return byId[pick];
    }

    public void Clear()
    {
        bag.Clear();
        recent.Clear();
    }

    private bool InRecent(string id, int cooldown)
    {
        for (int i = recent.Count - 1; i >= 0 && i >= recent.Count - cooldown; i--)
            if (recent[i] == id) return true;
        return false;
    }

    private int FindIndex(System.Predicate<string> match)
    {
        for (int i = 0; i < bag.Count; i++) if (match(bag[i])) return i;
        return -1;
    }

    private void Refill(IEnumerable<string> ids, System.Random rng)
    {
        bag.Clear();
        bag.AddRange(ids);
        for (int i = bag.Count - 1; i > 0; i--) // Fisher–Yates
        {
            int j = rng.Next(i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }
    }
}
