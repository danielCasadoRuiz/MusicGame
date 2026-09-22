using UnityEngine;

/// <summary>
/// Hair specialization of AvatarItemSO — shares stableId/variants (Shared/Male/Female)/Addressables/
/// morphs/defaultColorOverride with every other item (see AvatarItemSO's own doc), but carries NO
/// garment-specific data (slots/layer/masking/occlusion — that's WearableItemSO's own concern, see its
/// doc on why Hair doesn't need it).
///
/// Hair is part of AvatarIdentity by default (task's own "Hair forma part de Identity per defecte") —
/// AvatarRecipeSO can override it per-recipe, but the base identity always carries one; it is NEVER
/// listed among a Recipe's WearableItemSO[] items.
///
/// Deliberately minimal this phase (task's own explicit "no implementis hair physics / advanced
/// hairstyle systems" scope note) — attachmentBoneName/canCoexistWithHeadwear are the only two
/// hair-specific fields with a real consumer today (AvatarFactory's rigid bone-attach fallback,
/// WearableItemSO.hidesHair's own interaction). Kept as its OWN subclass (rather than folded into
/// WearableItemSO) specifically so future hair-only data (scalp masking, a dedicated hair-color
/// channel, physics/attachment metadata) has an obvious home without contaminating WearableItemSO's
/// own contract.
/// </summary>
[CreateAssetMenu(fileName = "HairItem", menuName = "MusicGame/Avatar/Hair Item")]
public class HairItemSO : AvatarItemSO
{
    [Header("Attachment (rigid fallback only — ignored when the variant IS skinned)")]
    [Tooltip("Bone name (matched into the avatar's real skeleton by AvatarSkeletonMapper — see its " +
             "own doc) this hair prefab attaches to when its variant's AvatarVisualPart has no " +
             "skinnedRenderers of its own (a simple rigid hair prop parented to the head bone).")]
    public string attachmentBoneName = "Head";

    [Tooltip("Any equipped WearableItemSO whose slot includes Headwear and sets hidesHair (see its " +
             "own doc) makes AvatarFactory skip/hide this hair entirely for that recipe — a helmet " +
             "and a full head of hair are mutually exclusive by default.")]
    public bool canCoexistWithHeadwear = false;

    // Placeholder for future hair-only data (scalp/body region masking, a dedicated hair-color
    // channel, physics/attachment metadata) — intentionally not built yet, see class doc.
}
