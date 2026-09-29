using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// The base geometry AvatarFactory builds everything else on top of — a SINGLE Addressable prefab
/// with ONE skeleton, ONE set of bindposes/skin weights and ONE Humanoid Avatar, shared by every
/// body: Male, Female and every continuous Gender/Weight/Muscle value in between.
///
/// The prefab's body mesh rests in the Gender-0 state (MaleBase) and carries the Gender blendshape
/// (FemaleBase - MaleBase) plus the six Slim/Heavy/Muscle channels (see
/// BodyMorphValues.GetMorphWeights for the exact formula). Both endpoints share the same canonical
/// height, so Gender never changes height, grounding or the skeleton.
///
/// maleBase/femaleBase are the baked REFERENCE geometry of the two Gender endpoints (Gender 0 / 1).
/// Runtime never writes them onto a mesh (body changes are blendshape weights only); they're the
/// ground truth the validator/tests check the mesh against, and what future garment fitting bakes
/// against.
///
/// baseAvatarPrefab is expected to carry an AvatarVisualPart (see its own doc) exposing: rootBone (the
/// skeleton AvatarSkeletonMapper builds its cache from and every equipped item remaps onto),
/// skinnedRenderers (morph-driven by AvatarBodyMorphController), skinToneRenderers (tinted from
/// FaceProfileSO.skinTone), an optional faceRenderer, and an optional regionMap (for
/// WearableItemSO.hiddenBodyRegions to act on).
/// </summary>
[CreateAssetMenu(fileName = "BaseAvatarDefinition", menuName = "MusicGame/Avatar/Base Avatar Definition")]
public class BaseAvatarDefinitionSO : ScriptableObject
{
    public string id;

    [Header("Base geometry (Addressable) — ONE prefab, shared by both genders")]
    public AssetReferenceGameObject baseAvatarPrefab;

    [Header("Gender endpoint reference geometry (Gender 0 / Gender 1) — same topology/skeleton/height")]
    public AvatarMeshBasePresetSO maleBase;
    public AvatarMeshBasePresetSO femaleBase;

    [Header("Shader properties (configurable — never hardcoded in code)")]
    [Tooltip("MaterialPropertyBlock color property FaceProfileSO.skinTone is written to on this base's " +
             "own AvatarVisualPart.skinToneRenderers.")]
    public string skinColorShaderProperty = "_BaseColor";

    [Tooltip("MaterialPropertyBlock texture property FaceProfileSO.faceTexture is written to on this " +
             "base's own AvatarVisualPart.faceRenderer, when both are assigned.")]
    public string faceTextureShaderProperty = "_BaseMap";

    public AvatarMeshBasePresetSO GetBasePreset(BodyBaseType baseType) =>
        baseType == BodyBaseType.Male ? maleBase : femaleBase;
}
