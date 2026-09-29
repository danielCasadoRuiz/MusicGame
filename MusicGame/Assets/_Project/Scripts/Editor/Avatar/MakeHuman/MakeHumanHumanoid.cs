/// <summary>
/// Unity Humanoid bone mapping for the MakeHuman game_engine skeleton — applied explicitly (never
/// auto-guessed) to MakeHuman_Canonical.fbx's ModelImporter (see MakeHumanFbxPipeline.ConfigureImporter).
/// The Avatar itself is created by Unity's importer FROM THE MODEL, whose rest pose is the real T-pose
/// (see MakeHumanTPose), so no separate T-pose description exists anywhere.
/// </summary>
public static class MakeHumanHumanoid
{
    /// <summary>Unity human bone -> game_engine bone name. Shoulder = clavicle, UpperArm = upperarm,
    /// Hand = hand (not a wrist helper), Toes = ball; fingers mapped 3 joints each.</summary>
    public static readonly (string human, string bone)[] BoneMap =
    {
        ("Hips", "pelvis"),
        ("Spine", "spine_01"), ("Chest", "spine_02"), ("UpperChest", "spine_03"),
        ("Neck", "neck_01"), ("Head", "head"),

        ("LeftShoulder", "clavicle_l"), ("LeftUpperArm", "upperarm_l"), ("LeftLowerArm", "lowerarm_l"), ("LeftHand", "hand_l"),
        ("RightShoulder", "clavicle_r"), ("RightUpperArm", "upperarm_r"), ("RightLowerArm", "lowerarm_r"), ("RightHand", "hand_r"),

        ("LeftUpperLeg", "thigh_l"), ("LeftLowerLeg", "calf_l"), ("LeftFoot", "foot_l"), ("LeftToes", "ball_l"),
        ("RightUpperLeg", "thigh_r"), ("RightLowerLeg", "calf_r"), ("RightFoot", "foot_r"), ("RightToes", "ball_r"),

        ("Left Thumb Proximal", "thumb_01_l"), ("Left Thumb Intermediate", "thumb_02_l"), ("Left Thumb Distal", "thumb_03_l"),
        ("Left Index Proximal", "index_01_l"), ("Left Index Intermediate", "index_02_l"), ("Left Index Distal", "index_03_l"),
        ("Left Middle Proximal", "middle_01_l"), ("Left Middle Intermediate", "middle_02_l"), ("Left Middle Distal", "middle_03_l"),
        ("Left Ring Proximal", "ring_01_l"), ("Left Ring Intermediate", "ring_02_l"), ("Left Ring Distal", "ring_03_l"),
        ("Left Little Proximal", "pinky_01_l"), ("Left Little Intermediate", "pinky_02_l"), ("Left Little Distal", "pinky_03_l"),

        ("Right Thumb Proximal", "thumb_01_r"), ("Right Thumb Intermediate", "thumb_02_r"), ("Right Thumb Distal", "thumb_03_r"),
        ("Right Index Proximal", "index_01_r"), ("Right Index Intermediate", "index_02_r"), ("Right Index Distal", "index_03_r"),
        ("Right Middle Proximal", "middle_01_r"), ("Right Middle Intermediate", "middle_02_r"), ("Right Middle Distal", "middle_03_r"),
        ("Right Ring Proximal", "ring_01_r"), ("Right Ring Intermediate", "ring_02_r"), ("Right Ring Distal", "ring_03_r"),
        ("Right Little Proximal", "pinky_01_r"), ("Right Little Intermediate", "pinky_02_r"), ("Right Little Distal", "pinky_03_r"),
    };
}
