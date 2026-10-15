using System;
using System.Collections.Generic;

/// <summary>
/// Uniform random pick among the valid variants of one (composer, category), never repeating the
/// previous pick of that SAME pair back-to-back when an alternative exists. Each category has its own
/// history (an Intro never blocks a Victory). Session-only memory — nothing is persisted.
/// </summary>
public static class ComposerDialogueSelector
{
    private static readonly Dictionary<(string composerId, ComposerDialogueCategory category), string> s_Last = new();
    private static readonly Random s_Random = new();

    /// <param name="keys">Valid variant keys (any order, no duplicates).</param>
    /// <param name="randomRange">Optional (exclusive max → value) source for tests; defaults to System.Random.</param>
    public static string Pick(IReadOnlyList<string> keys, string composerId, ComposerDialogueCategory category,
                              Func<int, int> randomRange = null)
    {
        if (keys == null || keys.Count == 0) return null;
        randomRange ??= max => s_Random.Next(max);

        string chosen;
        if (keys.Count == 1)
        {
            chosen = keys[0];
        }
        else
        {
            s_Last.TryGetValue((composerId, category), out var last);
            int lastIndex = -1;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i] == last) { lastIndex = i; break; }

            if (lastIndex < 0)
            {
                chosen = keys[randomRange(keys.Count)];
            }
            else
            {
                // Uniform over the other Count-1 variants: draw in [0, Count-1) and skip the last one.
                int r = randomRange(keys.Count - 1);
                chosen = keys[r >= lastIndex ? r + 1 : r];
            }
        }

        s_Last[(composerId, category)] = chosen;
        return chosen;
    }

    public static void ResetHistory() => s_Last.Clear();
}
