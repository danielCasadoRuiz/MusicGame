using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// The fighter BUILD a run produced — the Runner's CANONICAL combat-build output, stored once in
/// RunnerResults.CombatBuild (never copied into FighterStats or anywhere else). A fixed combat
/// budget (how WELL the player played) split across the five build stats (WHAT the song and the
/// collection looked like). See MusicCombatBuildConverter for the math.
/// Invariant: PunchPower + KickPower + Agility + Resistance + ImpactPower == Budget (float tolerance).
/// Immutable once built.
/// </summary>
public class MusicCombatBuild
{
    public readonly struct Stat
    {
        public readonly FightStatId StatId;
        /// <summary>0..1 — weighted performance of this stat's AVAILABLE sources (weights renormalized
        /// over the sources that exist), or the fallback when none exists.</summary>
        public readonly float Quality;
        /// <summary>0..1 — how present this stat's sources are in the song's actual gameplay.</summary>
        public readonly float Emphasis;
        /// <summary>Fraction of the budget this stat received (all shares sum to 1).</summary>
        public readonly float Share;
        /// <summary>Budget points (Share x Budget).</summary>
        public readonly float Points;
        /// <summary>True when none of this stat's sources existed in the song (Quality = fallback).</summary>
        public readonly bool UsedFallback;
        /// <summary>Human-readable provenance, e.g. "Snare 65% + Onset 35%" or "Onset 100% (HiHat N/A)".</summary>
        public readonly string Sources;

        public Stat(FightStatId statId, float quality, float emphasis, float share, float points, bool usedFallback, string sources)
        {
            StatId       = statId;
            Quality      = quality;
            Emphasis     = emphasis;
            Share        = share;
            Points       = points;
            UsedFallback = usedFallback;
            Sources      = sources;
        }
    }

    private readonly List<Stat> _stats = new();

    public IReadOnlyList<Stat> Stats => _stats;
    public float OverallPerformance { get; internal set; }
    public float MaxBudget { get; internal set; }
    /// <summary>Total combat power earned — depends ONLY on OverallPerformance, never on the song.</summary>
    public float Budget { get; internal set; }

    public float PunchPower  => Get(FightStatId.PunchPower);
    public float KickPower   => Get(FightStatId.KickPower);
    public float Agility     => Get(FightStatId.Agility);
    public float Resistance  => Get(FightStatId.Resistance);
    public float ImpactPower => Get(FightStatId.ImpactPower);

    /// <summary>True when the stat points add up to Budget (the fairness invariant).</summary>
    public bool BudgetInvariantHolds => Mathf.Abs(TotalPoints - Budget) <= 1e-3f * Mathf.Max(1f, Budget);

    public float Get(FightStatId statId)
    {
        foreach (var s in _stats) if (s.StatId == statId) return s.Points;
        return 0f;
    }

    public bool TryGet(FightStatId statId, out Stat stat)
    {
        foreach (var s in _stats) if (s.StatId == statId) { stat = s; return true; }
        stat = default;
        return false;
    }

    public float TotalPoints
    {
        get { float t = 0f; foreach (var s in _stats) t += s.Points; return t; }
    }

    internal void Add(Stat stat) => _stats.Add(stat);
}

/// <summary>
/// MusicalPerformanceSummary -> MusicCombatBuild. Pure/stateless, every number from FightStatsConfig.
///
/// Fairness rule — two separate questions:
///   A) HOW WELL did the player play?  Budget = maxCombatBudget x performanceToBudget(OverallPerformance).
///      OverallPerformance only averages the musical types that existed, so the song's composition
///      (or density) can never make the fighter globally stronger or weaker.
///   B) WHAT did they collect?  The budget is split across build stats by
///        raw_s = Quality_s x Lerp(1, Emphasis_s / mean(Emphasis), compositionInfluence)
///        Share_s = raw_s / sum(raw)
///      Quality_s = sum(w_i x performance_i) / sum(w_i) over the stat's AVAILABLE sources only —
///      a missing source's weight is redistributed, never counted as 0 (no HiHat -> Agility =
///      100% Onset). No available source at all -> the configured fallback (overall performance).
///      Emphasis_s = sum(w_i x presence_i) / sum(w_i) over ALL configured sources, presence_i =
///      this type's share of the song's available musical pickups — so a snare-heavy song leans
///      the build towards punches, but only as a distribution: the total stays Budget.
/// </summary>
public static class MusicCombatBuildConverter
{
    public static MusicCombatBuild Build(MusicalPerformanceSummary musical, FightStatsConfig config)
    {
        var build = new MusicCombatBuild();
        if (musical == null || config == null || config.buildStats == null || config.buildStats.Length == 0) return build;

        float overall = musical.OverallPerformance;
        if (float.IsNaN(overall)) overall = 0f;
        build.OverallPerformance = overall;
        build.MaxBudget          = config.maxCombatBudget;
        // Total power depends ONLY on overall performance — composition never enters this line.
        float budget = config.maxCombatBudget * config.performanceToBudget.Evaluate(Mathf.Clamp01(overall));
        build.Budget = float.IsNaN(budget) ? 0f : Mathf.Max(0f, budget);

        // Presence of each musical type in this song's actual gameplay (sums to 1 over applicable types).
        int totalAvailable = 0;
        foreach (var t in musical.Types) if (t.IsApplicable) totalAvailable += t.Available;

        int count = config.buildStats.Length;
        var quality  = new float[count];
        var emphasis = new float[count];
        var fallback = new bool[count];
        var sources  = new string[count];

        for (int s = 0; s < count; s++)
        {
            var mapping = config.buildStats[s];
            float activeWeight = 0f, weighted = 0f, allWeight = 0f, presence = 0f;
            var text = new StringBuilder();
            var missing = new List<string>();

            if (mapping.sources != null)
                foreach (var src in mapping.sources)
                {
                    float w = Mathf.Max(0f, src.weight);
                    if (w <= 0f) continue;
                    allWeight += w;
                    if (musical.TryGet(src.source, out var r) && r.IsApplicable && RingTypes.IsMusical(src.source))
                    {
                        activeWeight += w;
                        weighted     += w * r.Performance;
                        presence     += w * (totalAvailable > 0 ? (float)r.Available / totalAvailable : 0f);
                    }
                    else missing.Add(src.source.ToString());
                }

            if (activeWeight > 0f)
            {
                quality[s] = weighted / activeWeight;
                foreach (var src in mapping.sources)
                    if (src.weight > 0f && musical.IsApplicable(src.source))
                        text.Append(text.Length > 0 ? " + " : "").Append($"{src.source} {src.weight / activeWeight * 100f:0}%");
            }
            else
            {
                fallback[s] = true;
                quality[s]  = config.missingSourceFallback == BuildStatFallback.OverallPerformance ? overall : config.neutralInput;
                text.Append(config.missingSourceFallback == BuildStatFallback.OverallPerformance ? "overall performance" : "neutral");
            }
            if (missing.Count > 0) text.Append($" ({string.Join(", ", missing)} N/A)");

            emphasis[s] = allWeight > 0f ? presence / allWeight : 0f;
            sources[s]  = text.ToString();
        }

        float meanEmphasis = 0f;
        foreach (float e in emphasis) meanEmphasis += e;
        meanEmphasis /= count;

        var raw = new float[count];
        float rawSum = 0f;
        for (int s = 0; s < count; s++)
        {
            float relative = meanEmphasis > 1e-6f ? emphasis[s] / meanEmphasis : 1f;
            float r = quality[s] * Mathf.Lerp(1f, relative, Mathf.Clamp01(config.compositionInfluence));
            raw[s] = float.IsNaN(r) ? 0f : Mathf.Max(0f, r);
            rawSum += raw[s];
        }

        // Shares sum to exactly 1 (equal split if every raw weight is 0), and the LAST stat takes
        // the budget remainder, so the points add up to Budget exactly — nothing after this point
        // may rescale a stat on its own.
        float assigned = 0f;
        for (int s = 0; s < count; s++)
        {
            float share  = rawSum > 1e-6f ? raw[s] / rawSum : 1f / count;
            float points = s < count - 1 ? share * build.Budget : Mathf.Max(0f, build.Budget - assigned);
            assigned += points;
            build.Add(new MusicCombatBuild.Stat(config.buildStats[s].statId, quality[s], emphasis[s], share,
                                                points, fallback[s], sources[s]));
        }
        Debug.Assert(build.BudgetInvariantHolds, "[MusicCombatBuildConverter] stat points do not sum to the budget");
        return build;
    }
}
