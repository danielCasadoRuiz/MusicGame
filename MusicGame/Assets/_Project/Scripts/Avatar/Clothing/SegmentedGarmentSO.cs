using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One garmentCoverage rule: on every equipped garment of a STRICTLY LOWER layer whose category is in
/// targetCategories, the listed regions are hidden. Categories compare case-insensitively.</summary>
[Serializable]
public class GarmentCoverageRule
{
    public List<string> targetCategories = new();
    public List<string> zones = new();
}

/// <summary>
/// The layering configuration of one segmented garment — what it is (id, category), where it sits (layer) and
/// what it covers. Referenced from WearableItemSO.segmentedGarment; the per-quality geometry (prefab, final
/// material, region→submesh layout) lives on each quality prefab's SegmentedGarmentPart.
///
/// Coverage is explicit and conservative: a region is listed only when this garment covers it safely through
/// every animation. Missing coverage means empty lists (the garment hides nothing). bodyCoverage (skin) and
/// garmentCoverage (lower garments) are independent: a jacket can hide the arm skin and still leave the shirt
/// cuffs visible. Garments on the SAME layer never hide each other.
///
/// Written by GarmentCutsImporter from the cuts package (coverage / coverageConfigured). Coverage edited by
/// hand here is detected on re-import (importedCoverageHash) and preserved.
/// </summary>
[CreateAssetMenu(fileName = "SegmentedGarment", menuName = "MusicGame/Avatar/Segmented Garment")]
public class SegmentedGarmentSO : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable garment id (coverage.id in the cuts package). Never renamed by re-imports.")]
    public string garmentId;
    [Tooltip("Category other garments' garmentCoverage rules target (see GarmentRegionCatalogSO.categories).")]
    public string category;
    [Tooltip("Equipment layer: higher = more outer. Only garments on a strictly higher layer hide this one.")]
    public int layer;

    [Header("Coverage (regions safely covered through every animation)")]
    [Tooltip("Skin regions this garment hides on the body.")]
    public List<string> bodyCoverage = new();
    [Tooltip("Regions this garment hides on lower-layer garments, filtered by their category.")]
    public List<GarmentCoverageRule> garmentCoverage = new();

    [Header("Import record (GarmentCutsImporter)")]
    [Tooltip("False when the package had no <model>.coverage.json for this garment: coverage stays empty until configured.")]
    public bool coverageConfigured;
    [Tooltip("Hash of the coverage as last written by the importer — a different current hash means it was edited by hand and is preserved.")]
    public string importedCoverageHash;
    public List<string> importedSources = new();
    public string lastImportedPackage;

    /// <summary>Order-independent hash of the current coverage (ids trimmed, ordinal), used to detect manual edits.</summary>
    public string ComputeCoverageHash()
    {
        var parts = new List<string> { "L" + layer, "C" + (category ?? "") };
        var body = new List<string>(bodyCoverage ?? new List<string>());
        body.Sort(StringComparer.Ordinal);
        parts.Add("B" + string.Join(",", body));
        var rules = new List<string>();
        foreach (var rule in garmentCoverage ?? new List<GarmentCoverageRule>())
        {
            if (rule == null) continue;
            var targets = new List<string>(rule.targetCategories ?? new List<string>());
            var zones = new List<string>(rule.zones ?? new List<string>());
            targets.Sort(StringComparer.OrdinalIgnoreCase);
            zones.Sort(StringComparer.Ordinal);
            rules.Add(string.Join(",", targets).ToLowerInvariant() + ":" + string.Join(",", zones));
        }
        rules.Sort(StringComparer.Ordinal);
        parts.Add("G" + string.Join("|", rules));
        return Hash128.Compute(string.Join(";", parts)).ToString();
    }
}
