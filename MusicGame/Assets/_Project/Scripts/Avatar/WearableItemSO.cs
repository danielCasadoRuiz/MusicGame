using UnityEngine;

/// <summary>
/// Clothing/equipment specialization of AvatarItemSO — shirts, pants, jackets, shoes, gloves, hats,
/// glasses, necklaces, accessories all live here as ONE type (task's own explicit "no creïs
/// ShoesItemSO/GlovesItemSO..." requirement — AvatarSlot already tells them apart). Everything
/// garment-specific (slots/layering/masking/occlusion) lives HERE rather than on the shared
/// AvatarItemSO base, since Hair needs none of it (see AvatarItemSO's own doc).
///
/// This is the type AvatarRecipe/AvatarRecipeSO's item list is actually built from — Hair is NEVER in
/// that list, it comes from AvatarIdentity(.SO)/its optional Recipe override instead (unchanged from
/// before this refactor).
/// </summary>
[CreateAssetMenu(fileName = "WearableItem", menuName = "MusicGame/Avatar/Wearable Item")]
public class WearableItemSO : AvatarItemSO
{
    [Header("Slots")]
    [Tooltip("Every slot this item occupies at once — a Dress sets UpperBody | LowerBody. " +
             "AvatarFactory rejects a recipe where two equipped items' occupiedSlots overlap (see its " +
             "own doc) rather than silently letting one clobber the other.")]
    public AvatarSlot occupiedSlots;

    [Header("Layering")]
    public GarmentLayer layer = GarmentLayer.Shirt;

    [Header("Masking")]
    [Tooltip("Body regions this item's own geometry covers and should therefore hide on the BODY " +
             "underneath it — e.g. Pants -> Pelvis | UpperLegs | LowerLegs.")]
    public AvatarBodyRegion hiddenBodyRegions;

    [Tooltip("Regions of a LOWER-layer garment (see GarmentLayer's own doc on 'lower') this item " +
             "occludes — e.g. a Jacket (Outerwear) sets Torso | UpperArms to hide a T-Shirt " +
             "(Shirt) underneath it. Only actually applies to a lower item that itself exposes a " +
             "matching region via its own AvatarRegionMap — see AvatarFactory's own doc on the " +
             "warning-not-crash fallback when it doesn't.")]
    public AvatarBodyRegion occludedLowerGarmentRegions;

    [Header("Hair interaction")]
    [Tooltip("Only meaningful when occupiedSlots includes Headwear. When true, equipping this item " +
             "hides the avatar's Hair entirely (see HairItemSO.canCoexistWithHeadwear's own doc — this " +
             "is the item-side half of that same rule).")]
    public bool hidesHair;
}
