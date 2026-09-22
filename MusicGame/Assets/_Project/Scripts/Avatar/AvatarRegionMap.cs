using UnityEngine;

/// <summary>One AvatarBodyRegion's worth of geometry -> the Renderer(s) that draw it. Several bindings
/// can share the same region flag (e.g. two separate meshes both count as Torso) — AvatarRegionMap
/// toggles every binding whose region overlaps a given mask.</summary>
[System.Serializable]
public struct AvatarRegionBinding
{
    public AvatarBodyRegion region;
    public Renderer renderer;
}

/// <summary>
/// Declares which Renderer(s) draw which AvatarBodyRegion(s) for ONE avatar visual asset (the body, or
/// a single garment) — the ONLY masking mechanism in this system (task's own explicit "no utilitzis
/// transparència... el prefab del body ha d'exposar les regions renderitzables" requirement): toggling
/// `renderer.enabled`, never material transparency, never runtime triangle/submesh edits.
///
/// Placed on the SAME GameObject as (or a child of) an AvatarVisualPart — see its own doc — so
/// AvatarFactory finds both together. Optional: an item with no AvatarRegionMap simply can't be
/// partially masked, on itself or by an outer garment (see AvatarFactory's own doc on the resulting
/// "no partial masking support" warning-not-crash behavior).
///
/// Used TWICE by AvatarFactory, for the exact same mechanism applied to two different sources:
///   - Body masking: an equipped item's WearableItemSO.hiddenBodyRegions is applied to the BODY's own
///     AvatarRegionMap (Pants hide the body's Pelvis/Legs).
///   - Garment-vs-garment occlusion: an outer item's WearableItemSO.occludedLowerGarmentRegions is
///     applied to a LOWER-layer garment's OWN AvatarRegionMap (a Jacket hides a T-Shirt's Torso).
/// </summary>
public class AvatarRegionMap : MonoBehaviour
{
    public AvatarRegionBinding[] bindings = System.Array.Empty<AvatarRegionBinding>();

    /// <summary>Every region this map actually has geometry for — what an outer garment's
    /// occludedLowerGarmentRegions is checked against to decide "does this even apply here".</summary>
    public AvatarBodyRegion ExposedRegions
    {
        get
        {
            AvatarBodyRegion result = AvatarBodyRegion.None;
            foreach (var binding in bindings) result |= binding.region;
            return result;
        }
    }

    /// <summary>Disables every binding whose region overlaps `regionsToHide` — idempotent, safe to
    /// call repeatedly (e.g. a debug rebuild) since it always re-derives from the full bindings list
    /// rather than accumulating state.</summary>
    public void ApplyHiddenRegions(AvatarBodyRegion regionsToHide)
    {
        foreach (var binding in bindings)
        {
            if (binding.renderer == null) continue;
            bool hidden = (binding.region & regionsToHide) != 0;
            binding.renderer.enabled = !hidden;
        }
    }
}
