using UnityEngine;

/// <summary>
/// Decides, per equipped variant, whether a garment takes the segmented path or the existing (legacy) one.
/// Segmented requires BOTH recognised metadata and valid mappings: the item references a SegmentedGarmentSO,
/// the instantiated variant carries a SegmentedGarmentPart that validates against its meshes, and every mesh
/// is CPU-readable. Everything else — today's unsegmented models — keeps the legacy path exactly as before.
///
/// MIGRATION NOTE: this check, and AvatarFactory's "Legacy (unsegmented) garments" section, are the whole
/// compatibility layer. Once every model is segmented they can be deleted together.
/// </summary>
public static class SegmentedGarmentSupport
{
    /// <summary>True when `itemGO` (an instantiated variant of `item`) can be equipped as a segmented garment.
    /// `reason` explains a fallback when the item or prefab claims segmentation but cannot be used.</summary>
    public static bool TryGet(WearableItemSO item, GameObject itemGO, out SegmentedGarmentPart part, out string reason)
    {
        part = itemGO != null ? itemGO.GetComponentInChildren<SegmentedGarmentPart>(true) : null;
        reason = null;
        if (item == null || (item.segmentedGarment == null && part == null)) return false; // plain legacy model

        if (item.segmentedGarment == null) { reason = "the prefab has segmentation metadata but the item has no SegmentedGarmentSO"; part = null; return false; }
        if (part == null) { reason = "the item has a SegmentedGarmentSO but this variant prefab has no SegmentedGarmentPart"; return false; }
        if (!part.TryValidate(out reason)) { part = null; return false; }
        foreach (var layout in part.renderers)
        {
            if (!layout.renderer.sharedMesh.isReadable)
            {
                reason = $"mesh '{layout.renderer.sharedMesh.name}' is not CPU-readable (enable Read/Write on its model import)";
                part = null;
                return false;
            }
        }
        return true;
    }
}
