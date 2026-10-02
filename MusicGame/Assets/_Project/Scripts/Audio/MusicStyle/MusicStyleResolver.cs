using System.Collections.Generic;
using UnityEngine;

/// <summary>Result of MusicStyleResolver: the game-facing style, its theme key, and every style's score (debug).</summary>
public struct MusicStyleResolution
{
    public GameMusicStyle Style;
    public MusicStyleId ThemeStyle;
    public float Score;
    /// <summary>Lead over the second-best style (score units).</summary>
    public float Margin;
    public GameMusicStyle RunnerUp;
    public bool UsedFeatures;
    /// <summary>Tags-only pass: true when the result is conclusive enough to be final (see ResolveEarly).</summary>
    public bool Conclusive;
    public Dictionary<GameMusicStyle, float> Scores;
}

/// <summary>
/// RAW detector output (SongProfile.musicTags + features) → ONE final GameMusicStyle, through the
/// weights in MusicStyleRulesSO. Deterministic and lightweight; the raw tags are only read.
///   ResolveEarly(tags)  — tags only; Conclusive only for a dominant recognized genre (confidence + margin).
///   Resolve(profile)    — tags + full SongProfile features; always final.
/// </summary>
public static class MusicStyleResolver
{
    private static MusicStyleRulesSO s_defaults;

    private static MusicStyleRulesSO Rules(MusicStyleRulesSO rules) =>
        rules != null ? rules : (s_defaults ??= ScriptableObject.CreateInstance<MusicStyleRulesSO>());

    /// <summary>Tags-only pass. Conclusive = the winner is a recognizedGenre rule with score ≥
    /// earlyMinConfidence and a lead ≥ earlyMinMargin over the runner-up; otherwise the caller must
    /// keep the style pending and wait for the full analysis.</summary>
    public static MusicStyleResolution ResolveEarly(MusicTagScore[] tags, MusicStyleRulesSO rules)
    {
        rules = Rules(rules);
        var res = Resolve(new SongProfile { musicTags = tags }, rules);
        bool genre = false;
        if (rules.rules != null)
            foreach (var r in rules.rules) if (r != null && r.style == res.Style) { genre = r.recognizedGenre; break; }
        res.Conclusive = genre && res.Style != GameMusicStyle.Unknown
                      && res.Score >= rules.earlyMinConfidence && res.Margin >= rules.earlyMinMargin;
        return res;
    }

    public static MusicStyleResolution Resolve(SongProfile profile, MusicStyleRulesSO rules)
    {
        rules = Rules(rules);
        var res = new MusicStyleResolution { Style = GameMusicStyle.Unknown, ThemeStyle = MusicStyleId.Unknown, Scores = new() };
        if (profile == null || !profile.HasMusicTags || rules.rules == null) return res;

        var tags = new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var t in profile.musicTags)
            if (!string.IsNullOrEmpty(t.tag)) tags[t.tag] = Mathf.Max(t.score, tags.TryGetValue(t.tag, out var v) ? v : 0f);

        // Features exist only once the full analysis ran (a tags-only profile leaves them unused).
        bool full = profile.energyEnvelope != null && profile.energyEnvelope.Length > 0;
        res.UsedFeatures = full;
        float minutes = Mathf.Max(profile.duration, 1f) / 60f;
        float dance   = full ? Mathf.Clamp01(profile.danceability) : 0f;
        float aggr    = full ? Mean(profile.aggressiveness) : 0f;
        float inten   = full ? Mean(profile.intensity) : 0f;
        bool  keyed   = full && profile.estimatedKey >= 0;
        float minor   = keyed && !profile.isMajorMode ? Mathf.Clamp01(profile.modeConfidence) : 0f;
        float major   = keyed &&  profile.isMajorMode ? Mathf.Clamp01(profile.modeConfidence) : 0f;
        float beat    = full ? Mathf.Clamp01(profile.beatConfidence) : 0f;
        float dens    = full ? Mean(profile.density) : 0f;
        float tempo   = full ? Mathf.Clamp01(profile.tempoStability) : 0f;
        float voice   = full ? Mean(profile.voiceProbability) : 0f;
        float timbre  = full ? Mean(profile.timbralChange) : 0f;
        float dyn     = full ? Mathf.Clamp01(StdDev(profile.intensity) / Mathf.Max(0.01f, rules.intensitySpreadForMax)) : 0f;
        float hits    = full ? Mathf.Clamp01((profile.impactTimes?.Length ?? 0) / minutes / Mathf.Max(0.01f, rules.impactsPerMinuteForMax)) : 0f;
        float builds  = full ? Mathf.Clamp01((profile.buildupStartTimes?.Length ?? 0) / minutes / Mathf.Max(0.01f, rules.buildupsPerMinuteForMax)) : 0f;
        float cplx    = full ? Mathf.Clamp01(profile.complexity) : 0f;

        float best = float.NegativeInfinity, second = float.NegativeInfinity;
        foreach (var r in rules.rules)
        {
            if (r == null) continue;
            float s = 0f;
            if (r.tags != null) foreach (var tw in r.tags) if (tags.TryGetValue(tw.tag ?? "", out var p)) s += tw.weight * p;
            if (full)
                s += r.danceability * dance + r.lowDanceability * (1f - dance) + r.aggressiveness * aggr
                   + r.intensity * inten + r.calmness * (1f - inten) + r.minorMode * minor + r.majorMode * major
                   + r.beatConfidence * beat + r.density * dens + r.lowDensity * (1f - dens)
                   + r.tempoStability * tempo + r.lowTempoStability * (1f - tempo)
                   + r.voice * voice + r.instrumental * (1f - voice) + r.timbral * timbre
                   + r.dynamics * dyn + r.impacts * hits + r.buildups * builds + r.complexity * cplx;
            // Two rules may share a style: keep that style's best rule.
            if (res.Scores.TryGetValue(r.style, out var prev) && prev >= s) continue;
            res.Scores[r.style] = s;
            if (s > best)
            {
                if (res.Style != r.style) { second = best; res.RunnerUp = res.Style; }
                best = s; res.Style = r.style; res.ThemeStyle = r.themeStyle;
            }
            else if (s > second && r.style != res.Style) { second = s; res.RunnerUp = r.style; }
        }
        res.Score  = best;
        res.Margin = float.IsNegativeInfinity(second) ? best : best - second;
        if (best < rules.minScore) { res.Style = GameMusicStyle.Unknown; res.ThemeStyle = MusicStyleId.Unknown; }
        return res;
    }

    private static float Mean(float[] a)
    {
        if (a == null || a.Length == 0) return 0f;
        double sum = 0; foreach (var v in a) sum += v;
        return Mathf.Clamp01((float)(sum / a.Length));
    }

    private static float StdDev(float[] a)
    {
        if (a == null || a.Length < 2) return 0f;
        double sum = 0, sq = 0; foreach (var v in a) { sum += v; sq += v * v; }
        double m = sum / a.Length;
        return (float)System.Math.Sqrt(System.Math.Max(0, sq / a.Length - m * m));
    }
}
