using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Combines the Player's own AvatarIdentitySO (WHO they are — unaffected by music style) with a
/// MusicStyleAvatarProfileSO (WHAT they're wearing for the currently detected style) into one runtime
/// AvatarRecipe — task's own explicit "Player Identity + MusicStyleAvatarProfile -> AvatarRecipe"
/// pipeline. A plain static builder, deliberately NOT a MonoBehaviour/singleton (task's own "no creïs
/// managers globals innecessaris" scope note) — callers (Runner, eventually) invoke Build() directly
/// whenever they need a fresh recipe, there is no persistent state to own here.
/// </summary>
public static class PlayerAvatarRecipeBuilder
{
    public static AvatarRecipe Build(AvatarIdentitySO playerIdentity, BaseAvatarDefinitionSO baseAvatar, MusicStyleAvatarProfileSO styleProfile)
    {
        AvatarIdentity runtimeIdentity;
        if (playerIdentity != null)
        {
            runtimeIdentity = playerIdentity.ToRuntime();
        }
        else
        {
            Debug.LogWarning("[PlayerAvatarRecipeBuilder] No Player AvatarIdentitySO provided — falling back to a default Male body with no Face/Hair.");
            runtimeIdentity = new AvatarIdentity { Body = BodyMorphValues.Default(BodyBaseType.Male) };
        }

        var recipe = new AvatarRecipe
        {
            Identity   = runtimeIdentity,
            BaseAvatar = baseAvatar,
            Items      = new List<WearableItemSO>(),
            ColorOverrides = new List<AvatarColorOverride>(),
        };

        if (styleProfile != null)
        {
            if (styleProfile.items != null) recipe.Items.AddRange(styleProfile.items);
            if (styleProfile.colorOverrides != null) recipe.ColorOverrides.AddRange(styleProfile.colorOverrides);
        }
        else
        {
            Debug.LogWarning("[PlayerAvatarRecipeBuilder] No MusicStyleAvatarProfileSO provided for the current style — Player will wear no items this recipe.");
        }

        return recipe;
    }
}
