using UnityEngine;

/// <summary>
/// THE shared asset contract every avatar visual prefab must implement — the base body AND every
/// garment/hair AvatarItemSO alike (task's own explicit "CONTRACTE FINAL DELS ASSETS" section: both
/// need "skeleton compatible", "morph mapping/compatible morphs", "regions configured/optional" —
/// this one component IS that contract, so there is exactly one place on any prefab AvatarFactory
/// needs to look, never several marker components with overlapping responsibility).
///
/// Everything here is OPTIONAL/tolerant except for an item that actually needs it: a rigid accessory
/// (glasses, a badge) legitimately has no skinnedRenderers/rootBone/regionMap at all, and AvatarFactory
/// treats that as "this item just can't be skeleton-remapped/morphed/partially masked", never an error
/// (see AvatarFactory's own doc on tolerant degradation).
/// </summary>
public class AvatarVisualPart : MonoBehaviour
{
    [Tooltip("This part's own root bone (matched BY NAME into the avatar's real skeleton — see " +
             "AvatarSkeletonMapper's own doc). Required only when skinnedRenderers is non-empty.")]
    public Transform rootBone;

    [Tooltip("Skinned meshes that ride the skeleton — remapped onto the avatar's real skeleton " +
             "(AvatarSkeletonMapper) and morph-driven (AvatarBodyMorphController) if this part " +
             "declares compatible morph channels (see AvatarItemVariant.supportedMorphChannels).")]
    public SkinnedMeshRenderer[] skinnedRenderers = System.Array.Empty<SkinnedMeshRenderer>();

    [Tooltip("Non-skinned renderers (rigid attachments — most glasses/hats/accessories). Never " +
             "skeleton-remapped or morph-driven, but still eligible for color overrides and region " +
             "toggling via regionMap below.")]
    public Renderer[] rigidRenderers = System.Array.Empty<Renderer>();

    [Tooltip("Skin-tone target — populated on the BODY prefab only (see FaceProfileSO.skinTone's own " +
             "doc); left empty on garments/hair, which never receive skin tone.")]
    public Renderer[] skinToneRenderers = System.Array.Empty<Renderer>();

    [Tooltip("Optional — the specific renderer FaceProfileSO.faceTexture is applied to via " +
             "MaterialPropertyBlock (BaseAvatarDefinitionSO.faceTextureShaderProperty). Null is " +
             "tolerated (face texture override simply skipped).")]
    public Renderer faceRenderer;

    [Tooltip("Optional — declares which AvatarBodyRegion(s) this part's OWN geometry covers, for body " +
             "masking (on the body) or garment-vs-garment occlusion (on a garment) — see " +
             "AvatarRegionMap's own doc. Null means this part can't be partially masked/occluded.")]
    public AvatarRegionMap regionMap;

    /// <summary>Every renderer this part owns, skinned + rigid + skin-tone + face — the set
    /// AvatarFactory iterates for color overrides (a color override doesn't care whether the target
    /// renderer happens to be skinned or rigid).</summary>
    public System.Collections.Generic.IEnumerable<Renderer> AllRenderers()
    {
        foreach (var r in skinnedRenderers) if (r != null) yield return r;
        foreach (var r in rigidRenderers)   if (r != null) yield return r;
    }
}
