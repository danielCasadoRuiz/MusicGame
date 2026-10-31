using System;
using System.Collections.Generic;

/// <summary>
/// Pure layering rules for segmented garments — no Unity objects, so the rules are unit-testable on their own:
///
///   * garmentCoverage: garment O hides region r on garment G iff O.layer > G.layer (STRICTLY — same layer
///     leaves both visible) and one of O's rules lists G.category (case-insensitive) and r.
///   * bodyCoverage: the union (deduplicated) of every equipped garment's bodyCoverage.
///
/// "unassigned" is never hidden, whatever a configuration says. Unknown region ids are reported (the geometry
/// or skin they would have hidden is kept) — see GarmentDiagnostics.
/// </summary>
public static class GarmentCoverageResolver
{
    /// <summary>Fills hiddenPerGarment[i] with the regions hidden on garments[i] by the garments above it.
    /// `hiddenPerGarment` must have garments.Count sets; each is cleared first (no allocation when reused).</summary>
    public static void ResolveGarmentHiddenRegions(IReadOnlyList<SegmentedGarmentSO> garments, IReadOnlyList<HashSet<string>> hiddenPerGarment)
    {
        for (int g = 0; g < garments.Count; g++)
        {
            var hidden = hiddenPerGarment[g];
            hidden.Clear();
            var lower = garments[g];
            if (lower == null) continue;

            for (int o = 0; o < garments.Count; o++)
            {
                var outer = garments[o];
                if (o == g || outer == null || outer.layer <= lower.layer || outer.garmentCoverage == null) continue;
                foreach (var rule in outer.garmentCoverage)
                {
                    if (rule?.zones == null || !Targets(rule, lower.category)) continue;
                    foreach (var zone in rule.zones)
                        if (!string.IsNullOrEmpty(zone) && zone != GarmentRegionCatalogSO.UnassignedRegion) hidden.Add(zone);
                }
            }
        }
    }

    /// <summary>Union of every garment's bodyCoverage into `result` (cleared first), sorted and deduplicated.</summary>
    public static void CollectBodyCoverage(IReadOnlyList<SegmentedGarmentSO> garments, SortedSet<string> result)
    {
        result.Clear();
        foreach (var garment in garments)
        {
            if (garment?.bodyCoverage == null) continue;
            foreach (var region in garment.bodyCoverage)
                if (!string.IsNullOrEmpty(region) && region != GarmentRegionCatalogSO.UnassignedRegion) result.Add(region);
        }
    }

    private static bool Targets(GarmentCoverageRule rule, string category)
    {
        if (rule.targetCategories == null || string.IsNullOrEmpty(category)) return false;
        foreach (var target in rule.targetCategories)
            if (string.Equals(target, category, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
