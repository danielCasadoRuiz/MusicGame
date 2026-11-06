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

    [Header("Shader properties (configurable — never hardcoded in code)")]
    [Tooltip("MaterialPropertyBlock color property FaceProfileSO.skinTone is written to on this base's " +
             "own AvatarVisualPart.skinToneRenderers.")]
    public string skinColorShaderProperty = "_BaseColor";

    [Tooltip("MaterialPropertyBlock texture property FaceProfileSO.faceTexture is written to on this " +
             "base's own AvatarVisualPart.faceRenderer, when both are assigned.")]
    public string faceTextureShaderProperty = "_BaseMap";

    [Header("Body")]
    [Tooltip("Every avatar built on this base (player, runner, fighter, enemies) uses the plain base body — male, " +
             "Weight 0.5, Muscle 0: no body blendshape — ignoring the per-character body profiles / recipe overrides, " +
             "which are kept for later. Heights (OpponentDefinition.heightMeters) are not affected.")]
    public bool neutralBodyForAll = true;

}
