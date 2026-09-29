using UnityEngine;

/// <summary>Procedural Humanoid rig-test poses (no animation clip involved) — see AvatarPoseTests.</summary>
public enum AvatarTestPose
{
    TPose,
    ArmsDown,
    ArmsForward,
    ArmSwing,
    KneeLift,
    ElbowsBent,
}

/// <summary>
/// Drives an avatar through Unity Humanoid MUSCLES (HumanPoseHandler), starting from its own rest pose —
/// which is the canonical T-pose — so the Humanoid Avatar calibration can be judged independently of
/// any external animation: if a normal muscle moves a limb the wrong way here, the Avatar is wrong.
///
/// Unity muscle conventions used (verified against an independent reference Humanoid):
///   Arm Down-Up      -1 = arm lowered to its limit, +1 = raised
///   Arm Front-Back   negative = forward (flexion), positive = backward
///   Upper Leg Front-Back negative = forward (hip flexion), positive = backward
///   Stretch muscles  1 = straight, negative = bent
/// </summary>
public static class AvatarPoseTests
{
    private static readonly (string muscle, float value)[] ArmsDown =
        { ("Left Arm Down-Up", -1f), ("Right Arm Down-Up", -1f) };

    public static (string muscle, float value)[] Muscles(AvatarTestPose pose) => pose switch
    {
        AvatarTestPose.ArmsDown => ArmsDown,
        AvatarTestPose.ArmsForward => new[] { ("Left Arm Front-Back", -0.7f), ("Right Arm Front-Back", -0.7f), ("Left Arm Down-Up", -0.4f), ("Right Arm Down-Up", -0.4f) },
        AvatarTestPose.ArmSwing => new[] { ("Left Arm Down-Up", -0.8f), ("Right Arm Down-Up", -0.8f), ("Left Arm Front-Back", -0.3f), ("Right Arm Front-Back", 0.4f),
                                           ("Left Forearm Stretch", 0.6f), ("Right Forearm Stretch", 0.8f) },
        AvatarTestPose.KneeLift => new[] { ("Left Upper Leg Front-Back", -1f), ("Left Lower Leg Stretch", -1f), ("Left Arm Down-Up", -1f), ("Right Arm Down-Up", -1f) },
        AvatarTestPose.ElbowsBent => new[] { ("Left Arm Down-Up", -1f), ("Right Arm Down-Up", -1f), ("Left Forearm Stretch", -1f), ("Right Forearm Stretch", -1f) },
        _ => System.Array.Empty<(string, float)>(),
    };

    /// <summary>Resets `instance` to its rest (T-pose) and applies `pose` on top via Humanoid muscles.
    /// The Animator must not be playing a controller (it would overwrite the pose next frame).</summary>
    public static void Apply(AvatarInstance instance, AvatarTestPose pose)
    {
        if (instance?.Animator == null || instance.Animator.avatar == null) return;
        instance.ResetToRestPose();
        ApplyFromRest(instance.Animator, instance.Root, pose);
    }

    /// <summary>Same, for an avatar already in its rest pose (e.g. a freshly instantiated prefab).</summary>
    public static void ApplyFromRest(Animator animator, Transform root, AvatarTestPose pose)
    {
        if (pose == AvatarTestPose.TPose || animator == null || animator.avatar == null) return;
        using var handler = new HumanPoseHandler(animator.avatar, root);
        var human = new HumanPose();
        handler.GetHumanPose(ref human); // the rest (T-pose) muscles and body placement
        foreach (var (muscle, value) in Muscles(pose))
        {
            int index = System.Array.IndexOf(HumanTrait.MuscleName, muscle);
            if (index >= 0) human.muscles[index] = value;
        }
        handler.SetHumanPose(ref human);
    }
}
