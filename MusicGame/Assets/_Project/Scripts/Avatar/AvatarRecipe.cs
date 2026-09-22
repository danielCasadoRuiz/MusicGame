using System.Collections.Generic;

/// <summary>
/// HOW an identity goes dressed RIGHT NOW — the runtime data AvatarFactory.CreateAsync actually
/// consumes. Deliberately separate from AvatarIdentity (see its own doc): the same Identity can back
/// many Recipes (task's own example: Identity_Rex -> Avatar_Rex_Level1/5/10).
///
/// Built exclusively by AvatarRecipeSO.ToRuntime() or PlayerAvatarRecipeBuilder.Build() — never
/// constructed ad hoc elsewhere, so every optional override (body/hair/face) is always already
/// resolved into Identity by the time AvatarFactory sees it; the factory itself never reasons about
/// "override vs not", only about the final, flattened values.
/// </summary>
public class AvatarRecipe
{
    public AvatarIdentity Identity;
    public BaseAvatarDefinitionSO BaseAvatar;

    /// <summary>Wearables only — Hair is NEVER listed here, it lives on Identity (with an optional
    /// Recipe-level override) instead, see AvatarIdentity/AvatarRecipeSO's own doc.</summary>
    public List<WearableItemSO> Items = new();
    public List<AvatarColorOverride> ColorOverrides = new();
}
