using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The anatomical region vocabulary shared by every segmented garment AND the body masks that hide skin per
/// region — one per base body (masks live on the body's UV layout), referenced from
/// BaseAvatarDefinitionSO.garmentRegionCatalog.
///
/// Regions are anatomical, not per garment category: a long shirt and a long jacket both hide the back of the
/// trousers with the same "buttocks" region. Region ids and garment categories are plain strings so new ones
/// are added here (or by the cuts-package importer) without code changes. "unassigned" always exists: it is
/// the geometry outside every authored volume, and it is never hidden.
///
/// Masks follow the cuts-package convention (manifest.maskConvention): white = hidden skin, black = visible,
/// combined with a per-pixel maximum. They are imported as linear single-channel R8 textures, no mipmaps,
/// Clamp, CPU-readable (BodyVisibilityMask combines them on the CPU) — see GarmentCutsImporter.
/// </summary>
[CreateAssetMenu(fileName = "GarmentRegionCatalog", menuName = "MusicGame/Avatar/Garment Region Catalog")]
public class GarmentRegionCatalogSO : ScriptableObject
{
    public const string UnassignedRegion = "unassigned";

    [Serializable]
    public class RegionMask
    {
        public string region;
        [Tooltip("White = covered skin, black = visible. Body UV0 layout, R8 linear, readable, no mipmaps, Clamp.")]
        public Texture2D mask;
        [Tooltip("White texels reported by the producer (manifest masks[].hiddenPixels) — diagnostics only.")]
        public int hiddenPixels;
    }

    [Tooltip("Every anatomical region id garments may reference. 'unassigned' is always kept.")]
    public List<string> regionIds = new() { UnassignedRegion };

    [Tooltip("Garment categories garmentCoverage rules may target (shirt, top, jacket, trousers…). Extensible.")]
    public List<string> categories = new() { "top", "shirt", "jacket", "coat", "trousers", "skirt", "dress", "shoes", "gloves", "hat" };

    [Tooltip("One skin mask per region (regions without a mask simply never hide skin).")]
    public List<RegionMask> bodyMasks = new();

    [Header("Body compatibility (from the cuts package manifest)")]
    public string bodyObject;
    public string bodyUv;
    [Tooltip("sha256 of the avatar file the producer cut against (manifest.avatarSha256).")]
    public string avatarSha256;
    public int maskResolution;
    [Tooltip("Texels the hidden areas were bled into the empty UV background at import (seam safety, never into skin).")]
    public int seamPaddingTexels;
    [Tooltip("Manifest this catalog was last imported from — diagnostics only.")]
    public string sourcePackage;

    // Lazily built lookup; rebuilt whenever the serialized lists change (OnValidate) or on first use.
    [NonSerialized] private Dictionary<string, Texture2D> _maskByRegion;
    [NonSerialized] private HashSet<string> _regions;
    [NonSerialized] private HashSet<string> _categories;

    public bool IsKnownRegion(string region)
    {
        EnsureLookups();
        return region != null && _regions.Contains(region);
    }

    public bool IsKnownCategory(string category)
    {
        EnsureLookups();
        return category != null && _categories.Contains(category);
    }

    /// <summary>The skin mask for `region`, or null when none was delivered (that skin then stays visible).</summary>
    public Texture2D GetMask(string region)
    {
        EnsureLookups();
        return region != null && _maskByRegion.TryGetValue(region, out var mask) ? mask : null;
    }

    private void EnsureLookups()
    {
        if (_maskByRegion != null) return;
        _maskByRegion = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        foreach (var entry in bodyMasks)
            if (entry != null && !string.IsNullOrEmpty(entry.region) && entry.mask != null) _maskByRegion[entry.region] = entry.mask;
        _regions = new HashSet<string>(regionIds ?? new List<string>(), StringComparer.Ordinal) { UnassignedRegion };
        _categories = new HashSet<string>(categories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
    }

    private void OnValidate()
    {
        regionIds ??= new List<string>();
        if (!regionIds.Contains(UnassignedRegion)) regionIds.Insert(0, UnassignedRegion);
        _maskByRegion = null;
    }

    private void OnEnable() => _maskByRegion = null;
}
