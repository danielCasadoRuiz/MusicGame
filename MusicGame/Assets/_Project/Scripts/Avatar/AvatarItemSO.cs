using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Which physical representation(s) an AvatarItemSO actually has — see AvatarItemVariant's own doc.
/// </summary>
public enum AvatarItemVariantMode
{
    /// <summary>One asset serves both genders — sneakers, glasses, a lot of hair. Uses sharedVariant.</summary>
    Shared,
    /// <summary>Independent Male/Female assets. Either maleVariant or femaleVariant may be left
    /// unassigned, meaning this item simply doesn't exist for that BodyBaseType (see
    /// AvatarItemSO.GetVariant's own doc) — never an error by itself.</summary>
    GenderSpecific,
}

/// <summary>
/// ONE physical representation of an AvatarItemSO — the actual prefab plus which MorphChannel(s) THIS
/// mesh responds to. This is the ONLY place supportedMorphChannels lives now (task's own explicit
/// correction, moved off the conceptual item): a Jacket's Male mesh and Female mesh are two entirely
/// different meshes with their own three morph channels each — the CONCEPTUAL item ("Jacket01") never
/// claims to support all six, only whichever variant actually gets loaded does.
/// </summary>
[System.Serializable]
public class AvatarItemVariant
{
    public AssetReferenceGameObject prefab;

    [Tooltip("Which MorphChannel(s) THIS variant's own mesh responds to (see MorphChannel's own doc " +
             "on the strict exact-name contract — no mapper). A GenderSpecific variant should only " +
             "ever declare its own gender's three channels (Male variant -> MaleSlim/Heavy/Muscle, " +
             "Female variant -> FemaleSlim/Heavy/Muscle) — the validator flags the opposite as an " +
             "authoring error. Empty is perfectly normal (hair, glasses, hats, most shoes/gloves, " +
             "rigid accessories) — AvatarFactory simply never attempts a morph on this variant, no warning.")]
    public MorphChannel[] supportedMorphChannels = System.Array.Empty<MorphChannel>();

    /// <summary>False for an unassigned/default variant instance (e.g. a GenderSpecific item's
    /// maleVariant left empty because the item simply doesn't exist for Male) — see GetVariant's own
    /// doc on why that's a normal, non-error outcome.</summary>
    public bool IsAssigned => prefab != null && prefab.RuntimeKeyIsValid();
}

/// <summary>
/// The common base for every equippable/loadable avatar visual — WearableItemSO (clothing/accessories)
/// and HairItemSO both derive from this; nothing is ever authored as a bare AvatarItemSO (abstract).
///
/// Represents a CONCEPTUAL piece — "Jacket01", "Sneakers01", "HairShort01" (task's own explicit
/// principle): a Recipe only ever names WHICH conceptual item, never which physical mesh. Physical
/// representation lives entirely in up to three AvatarItemVariant slots (sharedVariant OR
/// maleVariant/femaleVariant — see AvatarItemVariantMode's own doc), resolved by
/// GetVariant(BodyBaseType) — the ONE place this resolution logic lives, so AvatarFactory never
/// repeats "which variant applies" per item type.
///
/// Nothing garment-specific (slots/layer/masking/occlusion) lives here — see WearableItemSO's own doc
/// on why: Hair needs none of it, and duplicating that decision per subclass is exactly the
/// "contaminate the base with things not every subclass needs" this split avoids.
/// </summary>
public abstract class AvatarItemSO : ScriptableObject
{
    [Header("Identity")]
    public string stableId;
    public string displayName;

    [Header("Variants")]
    public AvatarItemVariantMode variantMode = AvatarItemVariantMode.Shared;

    [Tooltip("Used when variantMode == Shared — the one representation for both genders.")]
    public AvatarItemVariant sharedVariant = new();

    [Tooltip("Used when variantMode == GenderSpecific. Leave prefab unassigned if this item simply " +
             "doesn't exist for Male (see AvatarItemVariant.IsAssigned's own doc).")]
    public AvatarItemVariant maleVariant = new();

    [Tooltip("Used when variantMode == GenderSpecific. Leave prefab unassigned if this item simply " +
             "doesn't exist for Female.")]
    public AvatarItemVariant femaleVariant = new();

    [Header("Appearance override")]
    [Tooltip("Optional default recolor for this item, applied whenever it's equipped with no more " +
             "specific AvatarColorOverride in the recipe/style profile overriding it. Leave " +
             "shaderProperty empty to skip entirely (item keeps its authored material as-is). Common " +
             "to every variant — a garment/hair's color choice doesn't usually depend on which mesh " +
             "variant happens to render it.")]
    public AvatarColorOverride defaultColorOverride;

    /// <summary>
    /// Resolves WHICH physical representation applies for `bodyType` — Shared always resolves to
    /// sharedVariant; GenderSpecific resolves to maleVariant/femaleVariant. Returns null when the
    /// resolved variant isn't actually assigned (see AvatarItemVariant.IsAssigned's own doc) — task's
    /// own explicit "null = incompatible with this gender" requirement. Callers (AvatarFactory) must
    /// treat null as "this item simply doesn't exist for this Identity" — skip/warn, never crash, and
    /// crucially never load the OTHER gender's variant as a fallback (task's own explicit "no
    /// carreguis una variant incompatible" requirement).
    /// </summary>
    public AvatarItemVariant GetVariant(BodyBaseType bodyType)
    {
        AvatarItemVariant candidate = variantMode == AvatarItemVariantMode.Shared
            ? sharedVariant
            : (bodyType == BodyBaseType.Male ? maleVariant : femaleVariant);

        return candidate != null && candidate.IsAssigned ? candidate : null;
    }
}
