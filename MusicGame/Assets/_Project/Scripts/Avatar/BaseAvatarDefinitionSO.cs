using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// The base geometry AvatarFactory builds everything else on top of — a SINGLE Addressable prefab
/// (task's own explicit correction: Male/Female are NOT two different prefabs, they're two vertex-
/// position PRESETS applied to the exact same shared skeleton/topology/bone-weights/UVs — see
/// AvatarMeshBasePresetSO's own doc). Every avatar, regardless of BodyBaseType, instantiates this same
/// baseAvatarPrefab; maleBase/femaleBase only ever change WHERE its vertices sit, applied by
/// AvatarFactory right after cloning the mesh and before any Slim/Heavy/Muscle blendshape.
///
/// baseAvatarPrefab is expected to carry an AvatarVisualPart (see its own doc) exposing: rootBone (the
/// skeleton AvatarSkeletonMapper builds its cache from and every equipped item remaps onto),
/// skinnedRenderers (base-shape-swapped by maleBase/femaleBase, then morph-driven by
/// AvatarBodyMorphController using this base's own gender-matched MorphChannels — resolved
/// automatically from BodyMorphValues.BaseType), skinToneRenderers (tinted from FaceProfileSO.skinTone),
/// an optional faceRenderer, and an optional regionMap (for WearableItemSO.hiddenBodyRegions to act on).
/// </summary>
[CreateAssetMenu(fileName = "BaseAvatarDefinition", menuName = "MusicGame/Avatar/Base Avatar Definition")]
public class BaseAvatarDefinitionSO : ScriptableObject
{
    public string id;

    [Header("Base geometry (Addressable) — ONE prefab, shared by both genders")]
    public AssetReferenceGameObject baseAvatarPrefab;

    [Header("Base shape presets — vertex positions only, same topology/skeleton")]
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
