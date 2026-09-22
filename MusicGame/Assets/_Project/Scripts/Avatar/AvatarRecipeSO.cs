using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional partial override of an AvatarIdentity's Body — bool flags stand in for a
/// nullable float (Unity doesn't serialize float? natively), same idiom used elsewhere in this
/// project for "optional override" fields. Leaving both flags false means "use the Identity's own
/// Weight/Muscle unchanged, only BaseType/Face/Hair overrides (if any) apply".</summary>
[System.Serializable]
public class BodyMorphOverride
{
    public bool overrideWeight;
    [Range(0f, 1f)] public float weight = 0.5f;

    public bool overrideMuscle;
    [Range(0f, 1f)] public float muscle;
}

/// <summary>
/// An AUTHORED outfit for an Identity — "Avatar_Rex_Level8" (task's own example): Identity_Rex +
/// jacket/t-shirt/pants/boots + a Body Override so Rex can visibly bulk up across levels while
/// staying recognizably Rex (same Face/Hair, same base Identity asset).
///
/// ToRuntime() ALWAYS returns a fresh AvatarRecipe with a fresh, already-flattened AvatarIdentity copy
/// — see AvatarIdentitySO.ToRuntime's own doc on why overrides never touch the source SOs.
/// </summary>
[CreateAssetMenu(fileName = "AvatarRecipe", menuName = "MusicGame/Avatar/Avatar Recipe")]
public class AvatarRecipeSO : ScriptableObject
{
    [Header("Who")]
    public AvatarIdentitySO identity;

    [Header("Base geometry")]
    public BaseAvatarDefinitionSO baseAvatar;

    [Header("What they're wearing")]
    public WearableItemSO[] items = System.Array.Empty<WearableItemSO>();

    [Header("Optional overrides — leave unassigned to just use the Identity as authored")]
    public BodyMorphOverride bodyOverride;
    public HairItemSO hairOverride;
    public FaceProfileSO faceOverride;

    [Header("Color overrides")]
    public AvatarColorOverride[] colorOverrides = System.Array.Empty<AvatarColorOverride>();

    public AvatarRecipe ToRuntime()
    {
        AvatarIdentity runtimeIdentity;
        if (identity != null)
        {
            runtimeIdentity = identity.ToRuntime();
        }
        else
        {
            Debug.LogWarning($"[AvatarRecipeSO] '{name}' has no identity assigned — falling back to a default Male body with no Face/Hair.");
            runtimeIdentity = new AvatarIdentity { Body = BodyMorphValues.Default(BodyBaseType.Male) };
        }

        if (bodyOverride != null)
        {
            if (bodyOverride.overrideWeight) runtimeIdentity.Body.Weight = bodyOverride.weight;
            if (bodyOverride.overrideMuscle) runtimeIdentity.Body.Muscle = bodyOverride.muscle;
        }
        if (hairOverride != null) runtimeIdentity.Hair = hairOverride;
        if (faceOverride != null) runtimeIdentity.Face = faceOverride;

        if (baseAvatar == null)
            Debug.LogWarning($"[AvatarRecipeSO] '{name}' has no baseAvatar assigned — AvatarFactory will have nothing to instantiate.");

        return new AvatarRecipe
        {
            Identity       = runtimeIdentity,
            BaseAvatar     = baseAvatar,
            Items          = new List<WearableItemSO>(items ?? System.Array.Empty<WearableItemSO>()),
            ColorOverrides = new List<AvatarColorOverride>(colorOverrides ?? System.Array.Empty<AvatarColorOverride>()),
        };
    }
}
