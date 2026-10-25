using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT the runner → fight conversion animation shows, computed from results that are ALREADY final (GameSession
/// built RunnerResults.CombatBuild and filled the run wallet when the run ended). Pure read-only presentation data:
/// building or playing it never grants, recomputes or re-applies anything.
///
/// Stat destinations: the five build stats go from 0 to the build's real points (MusicCombatBuild). Each stat's
/// points are split between the musical pickups that fed it in proportion to their real contribution to its quality
/// (weight × performance, MusicCombatBuildConverter) — so a pickup that feeds two stats sends one icon to each, and
/// the pieces of every bar add up exactly to its points. A stat whose sources the song did not have (fallback to
/// the overall performance) is split over all the musical pickups by performance, the same mean the fallback uses.
/// Resource destinations: Special / x3 / x4 counters (the run wallet) and the persistent extra-life counter.
/// Peak (score only) has no destination and never flies.
/// </summary>
public class BonusConversionPlan
{
    public struct Flight { public BonusKind Source; public FightStatId Stat; public float Points; }

    public struct ResourceFlight { public BonusKind Source; public int From; public int To; }

    public struct SourceChip { public BonusKind Kind; public int Count; public bool HasDestination; }

    public readonly List<SourceChip> Sources = new();
    public readonly List<Flight> StatFlights = new();
    public readonly List<ResourceFlight> ResourceFlights = new();
    /// <summary>The build's real final points per stat (the bars' end values).</summary>
    public readonly Dictionary<FightStatId, float> FinalPoints = new();
    public float Budget, MaxBudget;

    public bool IsEmpty => StatFlights.Count == 0 && ResourceFlights.Count == 0;

    public static BonusConversionPlan Build(RunnerResults results, FightStatsConfig config, RunSession run, int extraLivesNow)
    {
        var plan = new BonusConversionPlan();
        var build = results?.CombatBuild;
        var musical = results?.Musical;
        if (build == null || musical == null || config == null) return plan;
        plan.Budget = build.Budget;
        plan.MaxBudget = build.MaxBudget;
        foreach (var stat in BonusHelpCatalog.BuildStats) plan.FinalPoints[stat] = build.Get(stat);

        // Source chips: what was collected, in display order.
        foreach (var t in musical.Types)
        {
            var kind = BonusKinds.FromRing(t.Type);
            if (!kind.HasValue || t.Collected <= 0) continue;
            plan.Sources.Add(new SourceChip { Kind = kind.Value, Count = t.Collected, HasDestination = BonusHelpCatalog.Feeds(kind.Value, config).Count > 0 });
        }

        // Stat flights: split each stat's real points over its sources' real contributions.
        if (config.buildStats != null)
            foreach (var mapping in config.buildStats)
            {
                float points = build.Get(mapping.statId);
                if (points <= 1e-4f || mapping.sources == null) continue;
                var parts = new List<(BonusKind kind, float w)>();
                foreach (var src in mapping.sources)
                    if (src.weight > 0f && musical.TryGet(src.source, out var r) && r.IsApplicable && BonusKinds.FromRing(src.source).HasValue)
                        parts.Add((BonusKinds.FromRing(src.source).Value, src.weight * r.Performance));
                if (parts.Count == 0)   // fallback stat (none of its sources in the song): the overall performance's mean
                    foreach (var t in musical.Types)
                        if (t.IsApplicable && BonusKinds.FromRing(t.Type).HasValue) parts.Add((BonusKinds.FromRing(t.Type).Value, t.Performance));
                float sum = 0f;
                foreach (var p in parts) sum += p.w;
                foreach (var p in parts)
                {
                    float share = sum > 1e-6f ? p.w / sum : 1f / parts.Count;
                    if (share * points < 1e-3f) continue;
                    if (!HasChip(plan, p.kind)) continue;   // nothing collected of it: nothing to fly (its share stays in the bar)
                    plan.StatFlights.Add(new Flight { Source = p.kind, Stat = mapping.statId, Points = share * points });
                }
            }

        // Resources: the wallet / lives are already final; the counters animate from (final - this run's gain).
        if (run != null)
        {
            AddResource(plan, BonusKind.Special, run.SpecialsCollectedThisRun, run.Wallet.Specials);
            AddResource(plan, BonusKind.TripleCombo, results.Resources.TripleCombos, run.Wallet.TripleCombos);
            AddResource(plan, BonusKind.QuadCombo, results.Resources.QuadCombos, run.Wallet.QuadCombos);
            AddResource(plan, BonusKind.Life, run.LivesGrantedThisRun, extraLivesNow);
        }
        // A chip has a destination only if an icon really leaves it (a bonus whose share of every bar is ~0 stays).
        for (int i = 0; i < plan.Sources.Count; i++)
        {
            var c = plan.Sources[i];
            c.HasDestination = plan.StatFlights.Exists(f => f.Source == c.Kind) || plan.ResourceFlights.Exists(r => r.Source == c.Kind);
            plan.Sources[i] = c;
        }
        return plan;
    }

    private static bool HasChip(BonusConversionPlan plan, BonusKind kind)
    {
        foreach (var c in plan.Sources) if (c.Kind == kind) return true;
        return false;
    }

    private static void AddResource(BonusConversionPlan plan, BonusKind kind, int gained, int finalValue)
    {
        if (gained <= 0) return;
        plan.Sources.Add(new SourceChip { Kind = kind, Count = gained, HasDestination = true });
        plan.ResourceFlights.Add(new ResourceFlight { Source = kind, From = Mathf.Max(0, finalValue - gained), To = finalValue });
    }

    /// <summary>Stat points shown before any icon arrived (the part of a bar no collected pickup accounts for,
    /// e.g. a source the player never collected that still had a share). Bars start here, never jump past the end.</summary>
    public float StartPoints(FightStatId stat)
    {
        float flown = 0f;
        foreach (var f in StatFlights) if (f.Stat == stat) flown += f.Points;
        return Mathf.Max(0f, (FinalPoints.TryGetValue(stat, out var p) ? p : 0f) - flown);
    }
}
