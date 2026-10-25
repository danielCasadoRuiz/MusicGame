using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The fighter BUILD a run produced — the Runner's CANONICAL combat-build output, stored once in
/// RunnerResults.CombatBuild (never copied into FighterStats or anywhere else). FOUR stats (PunchPower, KickPower,
/// Resistance, Agility), each = base + the points the collected bonuses that feed it added (MusicCombatBuildConverter).
/// Contributions lists, per (bonus, stat), the points that really reached the stat (after the cap), so the
/// conversion animation can show exactly where every point came from. Immutable once built.
/// </summary>
public class MusicCombatBuild
{
    public readonly struct Stat
    {
        public readonly FightStatId StatId;
        /// <summary>Final points: base + bonus, capped (what combat reads).</summary>
        public readonly float Points;
        public readonly float BasePoints;
        /// <summary>What the collected bonuses added before the cap (≥ Points − BasePoints).</summary>
        public readonly float RawBonus;

        public Stat(FightStatId statId, float points, float basePoints, float rawBonus)
        {
            StatId = statId; Points = points; BasePoints = basePoints; RawBonus = rawBonus;
        }

        public bool Capped => BasePoints + RawBonus > Points + 1e-4f;
    }

    /// <summary>The points one bonus type really gave one stat (after the cap). Sum over a stat = Points − BasePoints.</summary>
    public readonly struct Contribution
    {
        public readonly RingType Source;
        public readonly FightStatId Stat;
        public readonly int Collected;
        public readonly float Points;

        public Contribution(RingType source, FightStatId stat, int collected, float points)
        {
            Source = source; Stat = stat; Collected = collected; Points = points;
        }
    }

    private readonly List<Stat> _stats = new();
    private readonly List<Contribution> _contributions = new();

    public IReadOnlyList<Stat> Stats => _stats;
    public IReadOnlyList<Contribution> Contributions => _contributions;
    public float MaxStatPoints { get; internal set; }

    public float PunchPower => Get(FightStatId.PunchPower);
    public float KickPower  => Get(FightStatId.KickPower);
    public float Resistance => Get(FightStatId.Resistance);
    public float Agility    => Get(FightStatId.Agility);

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

    /// <summary>Summary indicator only (never redistributes anything): the four stats added up.</summary>
    public float TotalPoints
    {
        get { float t = 0f; foreach (var s in _stats) t += s.Points; return t; }
    }

    internal void Add(Stat stat) => _stats.Add(stat);
    internal void Add(Contribution c) => _contributions.Add(c);
}

/// <summary>
/// MusicalPerformanceSummary -> MusicCombatBuild. Pure/stateless, every number from FightStatsConfig.
///
///   stat = min(maxStatPoints, baseStatPoints + Σ_bonus collected(bonus) × contribution(bonus, stat))
///
///   PunchPower = base + Snare + Onset + Impact      KickPower = base + Kick + Onset + Impact
///   Resistance = base + Beat + Onset                 Agility   = base + HiHat + Onset
///
/// Direct and cumulative: one collected bonus only ever ADDS to the stats it feeds — nothing is normalised against
/// a budget, shared out by weights or taken from another stat, and the cap is a plain min() (monotonic). A bonus the
/// song never offered simply adds nothing (no fallback borrowing another type's or the overall performance). The
/// runner score is untouched: this only reads the collected counts.
/// </summary>
public static class MusicCombatBuildConverter
{
    /// <summary>The four build stats, in the HUD's order.</summary>
    public static readonly FightStatId[] Stats =
        { FightStatId.PunchPower, FightStatId.KickPower, FightStatId.Resistance, FightStatId.Agility };

    public static MusicCombatBuild Build(MusicalPerformanceSummary musical, FightStatsConfig config)
    {
        var build = new MusicCombatBuild();
        if (config == null) return build;
        build.MaxStatPoints = config.maxStatPoints;
        float basePoints = Mathf.Min(config.baseStatPoints, config.maxStatPoints);

        foreach (var stat in Stats)
        {
            var parts = new List<(RingType source, int collected, float points)>();
            float raw = 0f;
            if (musical != null)
                foreach (var t in musical.Types)
                {
                    // What the player still HOLDS (falls take bonuses away — the live counter), never more.
                    if (t.Kept <= 0 || !RingTypes.IsMusical(t.Type)) continue;
                    var row = config.Contribution(t.Type);
                    float per = row != null ? row.Get(stat) : 0f;
                    if (per <= 0f) continue;
                    float pts = t.Kept * per;
                    raw += pts;
                    parts.Add((t.Type, t.Kept, pts));
                }
            float points = Mathf.Min(config.maxStatPoints, basePoints + raw);
            // The cap scales every contribution of this stat alike, so they still add up to exactly what it received.
            float k = raw > 1e-6f ? (points - basePoints) / raw : 0f;
            foreach (var p in parts) build.Add(new MusicCombatBuild.Contribution(p.source, stat, p.collected, p.points * k));
            build.Add(new MusicCombatBuild.Stat(stat, points, basePoints, raw));
        }
        return build;
    }
}
