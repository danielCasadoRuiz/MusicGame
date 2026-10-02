using System.Collections.Generic;
using UnityEngine;

/// <summary>Result of MusicStyleResolver: the game-facing style, its theme key, and every style's score (debug).</summary>
public struct MusicStyleResolution
{
    public GameMusicStyle Style;
    public MusicStyleId ThemeStyle;
    public float Score;
    public bool UsedFeatures;
    public Dictionary<GameMusicStyle, float> Scores;
}

/// <summary>
/// RAW detector output (SongProfile.musicTags + features) → controlled GameMusicStyle, through the
/// weights in MusicStyleRulesSO. Deterministic and lightweight; the raw tags are only read.
/// Works with a tags-only profile (early theme swap) and refines with the full profile (features).
/// </summary>
public static class MusicStyleResolver
{
    private static MusicStyleRulesSO s_defaults;

    public static MusicStyleResolution Resolve(SongProfile profile, MusicStyleRulesSO rules)
    {
        rules ??= s_defaults ??= ScriptableObject.CreateInstance<MusicStyleRulesSO>();
        var res = new MusicStyleResolution { Style = GameMusicStyle.Unknown, ThemeStyle = MusicStyleId.Unknown, Scores = new() };
        if (profile == null || !profile.HasMusicTags || rules.rules == null) return res;

        var tags = new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var t in profile.musicTags)
            if (!string.IsNullOrEmpty(t.tag)) tags[t.tag] = Mathf.Max(t.score, tags.TryGetValue(t.tag, out var v) ? v : 0f);

        // Features exist only once the full analysis ran (the early tags-only pass leaves them neutral = unused).
        bool full = profile.energyEnvelope != null && profile.energyEnvelope.Length > 0;
        res.UsedFeatures = full;
        float dance = full ? Mathf.Clamp01(profile.danceability) : 0f;
        float aggr  = full ? Mean(profile.aggressiveness) : 0f;
        float inten = full ? Mean(profile.intensity) : 0f;
        float minor = full && !profile.isMajorMode && profile.estimatedKey >= 0 ? Mathf.Clamp01(profile.modeConfidence) : 0f;
        float beat  = full ? Mathf.Clamp01(profile.beatConfidence) : 0f;

        float best = float.NegativeInfinity;
        foreach (var r in rules.rules)
        {
            if (r == null) continue;
            float s = 0f;
            if (r.tags != null) foreach (var tw in r.tags) if (tags.TryGetValue(tw.tag ?? "", out var p)) s += tw.weight * p;
            if (full)
                s += r.danceability * dance + r.lowDanceability * (1f - dance) + r.aggressiveness * aggr
                   + r.intensity * inten + r.calmness * (1f - inten) + r.minorMode * minor + r.beatConfidence * beat;
            res.Scores[r.style] = s;
            if (s > best) { best = s; res.Style = r.style; res.ThemeStyle = r.themeStyle; }
        }
        res.Score = best;
        if (best < rules.minScore) { res.Style = GameMusicStyle.Unknown; res.ThemeStyle = MusicStyleId.Unknown; }
        return res;
    }

    private static float Mean(float[] a)
    {
        if (a == null || a.Length == 0) return 0f;
        double sum = 0; foreach (var v in a) sum += v;
        return Mathf.Clamp01((float)(sum / a.Length));
    }
}
