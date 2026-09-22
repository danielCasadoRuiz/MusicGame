using UnityEngine;

/// <summary>
/// Which base geometry an avatar is built from — a CLOSED set (never "more genders later" — see
/// BaseAvatarDefinitionSO's own doc on why Male/Female are two separate authored prefabs, not one
/// prefab with a blend). Every morph channel in MorphChannel below is gender-specific and must never
/// be applied across this boundary (see BodyMorphValues.GetMorphWeights's own doc).
/// </summary>
public enum BodyBaseType
{
    Male,
    Female,
}

/// <summary>
/// Semantic morph identity — NEVER a blendshape index (see AvatarBodyMorphController's own doc on why
/// indices are resolved by name and cached, exactly once, instead of being hardcoded here). The
/// canonical blendshape name for a channel is simply its own enum name (MorphChannel.MaleSlim ->
/// blendshape "MaleSlim") — a deliberate 1:1 convention so there is exactly one place (this enum) an
/// artist needs to match when naming blendshapes on a mesh, with no separate mapping table to keep in
/// sync. A renderer that lacks a given blendshape name simply never gets that channel applied — see
/// AvatarBodyMorphController's own tolerant-lookup doc.
/// </summary>
public enum MorphChannel
{
    MaleSlim,
    MaleHeavy,
    MaleMuscle,
    FemaleSlim,
    FemaleHeavy,
    FemaleMuscle,
}

/// <summary>
/// Where a WearableItemSO attaches — a [Flags] set because ONE item can occupy several at once (a
/// Dress occupies UpperBody + LowerBody; see WearableItemSO.occupiedSlots' own doc). AvatarFactory
/// detects a conflict whenever two equipped items' occupiedSlots overlap (bitwise AND != 0) — see its
/// own doc on why that simple check is exactly what "a Dress and a separate Top both claim
/// UpperBody" needs, no special-casing per slot.
/// </summary>
[System.Flags]
public enum AvatarSlot
{
    None      = 0,
    UpperBody = 1 << 0,
    LowerBody = 1 << 1,
    FullBody  = 1 << 2,
    Feet      = 1 << 3,
    Hands     = 1 << 4,
    Headwear  = 1 << 5,
    Accessory = 1 << 6,
}

/// <summary>
/// Draw/occlusion order for garments — a plain ordered enum (never [Flags]: one item has exactly one
/// layer), used ONLY to decide which of two overlapping garments is "outer" for
/// WearableItemSO.occludedLowerGarmentRegions purposes (see AvatarFactory's own doc on garment-vs-
/// garment occlusion) — a strictly higher numeric value is strictly more "outer".
/// </summary>
public enum GarmentLayer
{
    Base       = 0,
    Inner      = 1,
    Shirt      = 2,
    Pants      = 3,
    Outerwear  = 4,
    Accessory  = 5,
}

/// <summary>
/// Coarse body geometry regions — a [Flags] set so an item can hide/occlude several at once (Pants ->
/// Pelvis | UpperLegs | LowerLegs). Deliberately NOT split left/right (task's own explicit "no ho
/// facis excessivament granular" scope note) — nothing so far needs a one-armed sleeve to hide only
/// the left UpperArm, and this stays a simple bitmask as long as that remains true. Consumed by
/// AvatarRegionMap, which is the ONE place a region flag actually turns into "this Renderer is
/// enabled/disabled" — this enum itself carries no rendering behavior.
/// </summary>
[System.Flags]
public enum AvatarBodyRegion
{
    None       = 0,
    Head       = 1 << 0,
    Neck       = 1 << 1,
    Torso      = 1 << 2,
    Pelvis     = 1 << 3,
    UpperArms  = 1 << 4,
    LowerArms  = 1 << 5,
    Hands      = 1 << 6,
    UpperLegs  = 1 << 7,
    LowerLegs  = 1 << 8,
    Feet       = 1 << 9,
}

/// <summary>
/// A single "recolor this equipped item" instruction — shared by MusicStyleAvatarProfileSO (style-
/// driven outfit colors) and AvatarRecipeSO (per-recipe overrides), applied by AvatarFactory via
/// MaterialPropertyBlock (never a shared-material edit — see AvatarFactory's own doc on why: two
/// avatars wearing the same AvatarItemSO must never recolor each other). shaderProperty is deliberately
/// a plain string, never hardcoded to "_Color"/"_BaseColor" — different render pipelines/shaders name
/// their color property differently, and this project's own convention (see FightDebugHUD/
/// FightFlowConfig docs elsewhere) is "no hardcoded numbers/strings in a controller", the same
/// principle applied here to shader property names.
/// </summary>
[System.Serializable]
public struct AvatarColorOverride
{
    public AvatarItemSO item;
    public Color color;
    [Tooltip("Shader color property this override writes via MaterialPropertyBlock — e.g. _BaseColor " +
             "or _Color, whatever the item's actual shader uses. Left empty is tolerated (skipped, " +
             "with a warning) rather than guessing a default.")]
    public string shaderProperty;
}
