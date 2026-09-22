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
/// The ONLY externally-visible morphology knobs (task's own explicit "externament només vull
/// BodyBaseType/Weight/Muscle" scope note) — everything else (which actual blendshapes exist, how
/// many there are) is resolved internally via GetMorphWeights, never exposed here. A plain
/// [Serializable] struct so it can be authored directly in the Inspector (BodyMorphProfileSO) AND
/// copied freely at runtime (AvatarIdentity, AvatarRecipeSO's body override) without ever aliasing a
/// ScriptableObject's own serialized data (see AvatarIdentitySO.ToRuntime's own doc on why that
/// matters).
///
/// Weight/Muscle are BOTH continuous (0..1) — BodyMorphProfileSO only exists to give a few common
/// combinations a reusable, nameable preset; nothing in this struct or GetMorphWeights ever
/// quantizes/snaps a value to one of those presets (task's own explicit "no limita el sistema" note).
/// </summary>
[System.Serializable]
public struct BodyMorphValues
{
    public BodyBaseType BaseType;
    [Range(0f, 1f)] public float Weight;
    [Range(0f, 1f)] public float Muscle;

    public static BodyMorphValues Default(BodyBaseType baseType) => new BodyMorphValues
    {
        BaseType = baseType,
        Weight   = 0.5f,
        Muscle   = 0f,
    };

    /// <summary>
    /// Resolves Weight/Muscle into the actual MorphChannels for THIS instance's own BaseType — never
    /// Male channels for a Female body or vice versa (task's own explicit, repeated "mai aplicar
    /// morphs Male sobre Female ni al revés" requirement).
    ///
    /// WEIGHT MAPPING (task's own explicit spec):
    ///   Weight 0    -> Slim 100%
    ///   Weight 0.5  -> Slim 0%   / Heavy 0%   (both channels silent — the "Normal" midpoint has no
    ///                  blendshape of its own, it's simply the base mesh with neither applied)
    ///   Weight 1    -> Heavy 100%
    ///   Linear interpolation in between, on EITHER side of 0.5 independently (Slim only ever active
    ///   below 0.5, Heavy only ever active above 0.5 — they never overlap).
    ///
    /// Muscle applies unconditionally via its own separate channel/value — entirely independent of
    /// where Weight sits (task's own explicit "Muscle s'aplica després amb el canal corresponent").
    ///
    /// `results` is cleared and refilled rather than allocating a new List every call — callers
    /// (AvatarBodyMorphController.Apply) are expected to reuse one buffer.
    /// </summary>
    public void GetMorphWeights(List<MorphWeight> results)
    {
        results.Clear();

        MorphChannel slimChannel   = BaseType == BodyBaseType.Male ? MorphChannel.MaleSlim   : MorphChannel.FemaleSlim;
        MorphChannel heavyChannel  = BaseType == BodyBaseType.Male ? MorphChannel.MaleHeavy  : MorphChannel.FemaleHeavy;
        MorphChannel muscleChannel = BaseType == BodyBaseType.Male ? MorphChannel.MaleMuscle : MorphChannel.FemaleMuscle;

        float w = Mathf.Clamp01(Weight);
        float slimWeight  = w < 0.5f ? (0.5f - w) / 0.5f : 0f;
        float heavyWeight = w > 0.5f ? (w - 0.5f) / 0.5f : 0f;

        results.Add(new MorphWeight(slimChannel, slimWeight));
        results.Add(new MorphWeight(heavyChannel, heavyWeight));
        results.Add(new MorphWeight(muscleChannel, Mathf.Clamp01(Muscle)));
    }
}
