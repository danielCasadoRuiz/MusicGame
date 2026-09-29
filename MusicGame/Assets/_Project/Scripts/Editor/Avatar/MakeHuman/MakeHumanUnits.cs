using UnityEngine;

/// <summary>
/// MakeHuman source-space -> Unity-space conversion, shared by every parser that reads MakeHuman
/// data (base mesh positions, target deltas, rig joints) so they can never disagree.
///
/// Ported from AvatarLab's MakeHumanUnits (scale) + MakeHumanBodyMeshLoader (handedness):
///   - SCALE: 1 raw MakeHuman unit ≈ 0.1 m (base.obj spans ~16.9 raw units; MakeHuman's documented
///     average young adult is ~1.68 m — AvatarLab's own verified measurement).
///   - HANDEDNESS: MakeHuman is right-handed, Unity left-handed. AvatarLab negated Z, which leaves the
///     character facing -Z. MusicGame negates X instead (= AvatarLab's mapping rotated 180° about Y),
///     so the character faces +Z like every Unity Humanoid expects, with its anatomical left at -X.
///     It's still exactly one reflection, so triangle winding is reversed exactly as AvatarLab did.
/// </summary>
public static class MakeHumanUnits
{
    public const float MetersPerRawUnit = 0.1f;

    /// <summary>Converts a raw MakeHuman position OR delta (same space, same conversion).</summary>
    public static Vector3 ToUnity(float x, float y, float z) =>
        new Vector3(-x * MetersPerRawUnit, y * MetersPerRawUnit, z * MetersPerRawUnit);
}
