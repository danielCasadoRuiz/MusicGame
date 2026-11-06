using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The layering configuration of one garment — what it is (id, category), where it sits (layer) and which lower
/// garments it hides. Referenced from WearableItemSO.segmentedGarment; the per-quality geometry (prefab, final
/// material, occlusion data) lives on each quality prefab's SegmentedGarmentPart. Written by SegmentedOutfitSetup
/// (layering by garment kind). Garments on the SAME layer never hide each other.
/// </summary>
[CreateAssetMenu(fileName = "SegmentedGarment", menuName = "MusicGame/Avatar/Segmented Garment")]
public class SegmentedGarmentSO : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable garment id. Never renamed by re-imports.")]
    public string garmentId;
    [Tooltip("Category other garments' hidesCategories target (shirt, top, waistcoat, jacket, trousers…).")]
    public string category;
    [Tooltip("Equipment layer: higher = more outer. Only garments on a strictly higher layer hide this one.")]
    public int layer;

    [Header("Occlusion masks")]
    [Tooltip("Categories of lower-layer garments this one hides where it lies over them (its occlusion masks).")]
    public List<string> hidesCategories = new();
    [Tooltip("How far (m) this garment may sit above a lower garment's vertex and still hide it. Larger hides more " +
             "of what is under loose parts — which can then show when the body moves away from them.")]
    [Range(0f, 0.2f)] public float occlusionGap = 0.10f;

    [Header("Import record")]
    public string lastImportedPackage;
}
