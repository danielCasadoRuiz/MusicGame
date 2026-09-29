using System.Collections.Generic;
using UnityEngine;

/// <summary>One resolved morph channel + how strongly it should be applied (0..1) — the output of
/// BodyMorphValues.GetMorphWeights, consumed directly by AvatarBodyMorphController.Apply. Never
/// carries a blendshape index itself (see MorphChannel's own doc).</summary>
public readonly struct MorphWeight
{
    public readonly MorphChannel Channel;
    public readonly float Weight01;

    public MorphWeight(MorphChannel channel, float weight01)
    {
        Channel  = channel;
        Weight01 = weight01;
    }
}

/// <summary>
/// The ONLY externally-visible morphology knobs: Gender / Weight / Muscle — everything else (which
/// actual blendshapes exist, how many there are) is resolved internally via GetMorphWeights, never
/// exposed here. A plain [Serializable] struct copied freely at runtime (AvatarIdentity, recipe body
/// overrides) without ever aliasing a ScriptableObject's own serialized data.
///
/// ALL THREE axes are continuous:
///   Gender 0 = exact MaleBase .. 1 = exact FemaleBase (same height, same skeleton — see
///          BaseAvatarDefinitionSO). Recipes/profiles still serialize the BodyBaseType endpoint;
///          FromBaseType maps Male -> 0, Female -> 1.
///   Weight 0 = thin, 0.5 = base, 1 = heavy.
///   Muscle 0 = base, 1 = strong.
/// A future Height axis would be a fourth, independent field — Gender deliberately carries no height.
/// </summary>
[System.Serializable]
public struct BodyMorphValues
{
    [Range(0f, 1f)] public float Gender;
    [Range(0f, 1f)] public float Weight;
    [Range(0f, 1f)] public float Muscle;

    /// <summary>The DOMINANT endpoint (Gender &lt; 0.5 = Male) — only for discrete decisions such as
    /// which gender-specific Wearable variant to load (AvatarItemSO.GetVariant). Never used for
    /// geometry.</summary>
    public BodyBaseType BaseType => Gender < 0.5f ? BodyBaseType.Male : BodyBaseType.Female;

    public static float GenderOf(BodyBaseType baseType) => baseType == BodyBaseType.Female ? 1f : 0f;

    public static BodyMorphValues FromBaseType(BodyBaseType baseType, float weight, float muscle) => new BodyMorphValues
    {
        Gender = GenderOf(baseType),
        Weight = weight,
        Muscle = muscle,
    };

    public static BodyMorphValues Default(BodyBaseType baseType) => FromBaseType(baseType, 0.5f, 0f);

    /// <summary>
    /// Resolves Gender/Weight/Muscle into blendshape weights for a mesh whose REST vertices are
    /// MaleBase, carrying:
    ///   Gender       = FemaleBase - MaleBase
    ///   MaleX        = MaleX   - MaleBase      (X = Slim / Heavy / Muscle)
    ///   FemaleX      = FemaleX - FemaleBase
    ///
    /// With g = Gender, s/h/m = the Slim/Heavy/Muscle amounts below:
    ///   V = MaleBase + g·(FemaleBase - MaleBase)
    ///       + (1-g)·(s·dMaleSlim   + h·dMaleHeavy   + m·dMaleMuscle)
    ///       +    g·(s·dFemaleSlim + h·dFemaleHeavy + m·dFemaleMuscle)
    ///     = lerp(MaleBase + maleMorphs, FemaleBase + femaleMorphs, g)            — exactly.
    /// So a Gender transition moves MaleHeavy straight toward FemaleHeavy (etc.) with Weight/Muscle held
    /// constant — never through a neutralized body — and g = 0 / g = 1 reproduce each family exactly.
    ///
    /// WEIGHT MAPPING: Weight 0 -> Slim 100%; 0.5 -> neither (the base itself); 1 -> Heavy 100%;
    /// linear on either side, Slim and Heavy never overlap. Muscle is its own independent amount.
    ///
    /// Every channel is emitted as an ABSOLUTE weight on every call (inactive ones explicitly 0), so
    /// the result never depends on what was applied before. `results` is cleared and refilled —
    /// callers reuse one buffer.
    /// </summary>
    public void GetMorphWeights(List<MorphWeight> results)
    {
        results.Clear();

        float g = Mathf.Clamp01(Gender);
        float maleFactor = 1f - g;
        float femaleFactor = g;

        float w = Mathf.Clamp01(Weight);
        float slim   = w < 0.5f ? (0.5f - w) / 0.5f : 0f;
        float heavy  = w > 0.5f ? (w - 0.5f) / 0.5f : 0f;
        float muscle = Mathf.Clamp01(Muscle);

        results.Add(new MorphWeight(MorphChannel.Gender,       g));
        results.Add(new MorphWeight(MorphChannel.MaleSlim,     slim   * maleFactor));
        results.Add(new MorphWeight(MorphChannel.MaleHeavy,    heavy  * maleFactor));
        results.Add(new MorphWeight(MorphChannel.MaleMuscle,   muscle * maleFactor));
        results.Add(new MorphWeight(MorphChannel.FemaleSlim,   slim   * femaleFactor));
        results.Add(new MorphWeight(MorphChannel.FemaleHeavy,  heavy  * femaleFactor));
        results.Add(new MorphWeight(MorphChannel.FemaleMuscle, muscle * femaleFactor));
    }
}
