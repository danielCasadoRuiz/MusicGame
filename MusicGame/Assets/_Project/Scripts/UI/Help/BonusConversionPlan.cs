using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT the runner → fight conversion animation shows, computed from results that are ALREADY final (GameSession
/// built RunnerResults.CombatBuild and filled the run wallet when the run ended). Pure read-only presentation data:
/// building or playing it never grants, recomputes or re-applies anything.
///
/// Stat destinations: the four build stats start at their base and grow by exactly the points every collected bonus
/// gave them (MusicCombatBuild.Contributions — direct, after the cap): Snare → Punch, Kick → Kick, HiHat → Agility,
/// Beat → Resistance, Onset → all four, Impact → Punch + Kick. A bonus that feeds several stats sends one smaller icon
/// to each (one bonus, several destinations — never shown as several pickups); the pieces of a bar add up exactly to
/// its final points, whichever bonuses fed it.
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
    /// <summary>Summary indicator: the four final stats added up (never used to redistribute anything).</summary>
    public float Budget, MaxBudget;
    /// <summary>Each stat's starting value (its base): what a bar shows before any icon arrives.</summary>
    public readonly Dictionary<FightStatId, float> BasePoints = new();

    public bool IsEmpty => StatFlights.Count == 0 && ResourceFlights.Count == 0;

    public static BonusConversionPlan Build(RunnerResults results, FightStatsConfig config, RunSession run, int extraLivesNow)
    {
        var plan = new BonusConversionPlan();
        var build = results?.CombatBuild;
        var musical = results?.Musical;
        if (build == null || musical == null || config == null) return plan;
        plan.Budget = build.TotalPoints;
        plan.MaxBudget = build.MaxStatPoints * BonusHelpCatalog.BuildStats.Length;
        foreach (var stat in BonusHelpCatalog.BuildStats)
        {
            plan.FinalPoints[stat] = build.Get(stat);
            plan.BasePoints[stat] = build.TryGet(stat, out var st) ? st.BasePoints : 0f;
        }

        // Source chips: what was collected, in display order.
        foreach (var t in musical.Types)
        {
            var kind = BonusKinds.FromRing(t.Type);
            if (!kind.HasValue || t.Kept <= 0) continue;   // the counts the player saw at the end (after falls)
            plan.Sources.Add(new SourceChip { Kind = kind.Value, Count = t.Kept, HasDestination = false });
        }

        // Stat flights: the build's own per-(bonus, stat) points — nothing recomputed or re-split here.
        foreach (var c in build.Contributions)
        {
            var kind = BonusKinds.FromRing(c.Source);
            if (!kind.HasValue || c.Points < 1e-3f || !HasChip(plan, kind.Value)) continue;
            plan.StatFlights.Add(new Flight { Source = kind.Value, Stat = c.Stat, Points = c.Points });
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

    /// <summary>Stat points shown before any icon arrived: final − everything that flies in (= the base).
    /// Bars start here and end exactly on the final value.</summary>
    public float StartPoints(FightStatId stat)
    {
        float flown = 0f;
        foreach (var f in StatFlights) if (f.Stat == stat) flown += f.Points;
        return Mathf.Max(0f, (FinalPoints.TryGetValue(stat, out var p) ? p : 0f) - flown);
    }

    /// <summary>How many stats one bonus of `kind` flies to (Onset 4, Impact 2, specialists 1).</summary>
    public int Destinations(BonusKind kind)
    {
        int n = 0;
        foreach (var f in StatFlights) if (f.Source == kind) n++;
        return n;
    }
}
