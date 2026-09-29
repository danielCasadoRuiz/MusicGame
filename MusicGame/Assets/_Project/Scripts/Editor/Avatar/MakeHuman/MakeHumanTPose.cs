using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The canonical avatar's REST pose is a real Unity-Humanoid T-pose (not an A-pose plus an
/// "enforced" T-pose description). MakeHuman's own rest is an A-pose (upper arms ~50 deg down, elbows
/// ~40 deg bent forward, legs ~8 deg apart), so the bake re-poses every body state into the T-pose
/// with the rig's own linear-blend skinning:
///   - upper arms horizontal (rotated about the body's front axis: the palm/thumb orientation the
///     A-pose already has is preserved, so palms end up facing down, thumbs forward);
///   - elbows and wrists straightened with the minimal (hinge) rotation onto the arm line;
///   - thighs and shins vertical (knees straight, legs parallel);
///   - feet keep their rest world orientation (flat on the floor, toes forward);
///   - spine, neck, head, clavicles and fingers unchanged relative to their parents.
/// The same bone rotations re-pose MaleBase, FemaleBase and every morph state, so every body — and
/// every blend of them — shares ONE T-pose skeleton and ONE set of bindposes.
///
/// Bones are axis-aligned in the A-pose source (identity rotations, see MakeHumanRig); the T-pose
/// rotations computed here are what the FBX bones are finally oriented by (MakeHumanFbxExport).
/// </summary>
public class MakeHumanTPose
{
    public Vector3[] RestPosition;     // A-pose joint positions (input)
    public Vector3[] Position;         // T-pose joint positions
    public Quaternion[] Rotation;      // T-pose world rotation of each bone (A-pose rest = identity)
    public Matrix4x4[] Skin;           // A-pose model space -> T-pose model space, per bone
    public readonly List<string> Report = new List<string>();

    private enum Rule { None, AimLeft, AimRight, AimDown, KeepWorldRest }

    private static readonly Dictionary<string, (Rule rule, string child)> Rules = new Dictionary<string, (Rule, string)>
    {
        { "upperarm_l", (Rule.AimLeft,  "lowerarm_l") }, { "lowerarm_l", (Rule.AimLeft,  "hand_l") }, { "hand_l", (Rule.AimLeft,  "middle_01_l") },
        { "upperarm_r", (Rule.AimRight, "lowerarm_r") }, { "lowerarm_r", (Rule.AimRight, "hand_r") }, { "hand_r", (Rule.AimRight, "middle_01_r") },
        { "thigh_l", (Rule.AimDown, "calf_l") }, { "calf_l", (Rule.AimDown, "foot_l") },
        { "thigh_r", (Rule.AimDown, "calf_r") }, { "calf_r", (Rule.AimDown, "foot_r") },
        { "foot_l", (Rule.KeepWorldRest, null) }, { "foot_r", (Rule.KeepWorldRest, null) },
    };

    public static MakeHumanTPose Compute(MakeHumanRig rig, Vector3[] restJoints)
    {
        int n = rig.Bones.Count;
        var pose = new MakeHumanTPose
        {
            RestPosition = restJoints,
            Position = new Vector3[n],
            Rotation = new Quaternion[n],
            Skin = new Matrix4x4[n],
        };

        for (int i = 0; i < n; i++) // parents precede children (MakeHumanRig order)
        {
            var bone = rig.Bones[i];
            int parent = bone.Parent != null ? rig.BoneIndex[bone.Parent] : -1;
            Quaternion parentRot = parent >= 0 ? pose.Rotation[parent] : Quaternion.identity;
            pose.Position[i] = parent >= 0 ? pose.Position[parent] + parentRot * (restJoints[i] - restJoints[parent]) : restJoints[i];

            Quaternion world = parentRot; // local rotation identity unless a rule applies
            if (Rules.TryGetValue(bone.Name, out var r))
            {
                if (r.rule == Rule.KeepWorldRest)
                {
                    world = Quaternion.identity;
                }
                else
                {
                    int child = rig.BoneIndex[r.child];
                    var current = (world * (restJoints[child] - restJoints[i])).normalized;
                    var target = r.rule == Rule.AimLeft ? Vector3.left : r.rule == Rule.AimRight ? Vector3.right : Vector3.down;
                    var correction = Quaternion.FromToRotation(current, target);
                    world = correction * world;
                    pose.Report.Add($"{bone.Name}: {Vector3.Angle(current, target):0.0} deg onto {target}");
                }
            }
            pose.Rotation[i] = world;
            pose.Skin[i] = Matrix4x4.TRS(pose.Position[i], world, Vector3.one) * Matrix4x4.Translate(-restJoints[i]);
        }
        return pose;
    }

    /// <summary>Linear-blend-skins raw MakeHuman positions (A-pose model space) into the T-pose using
    /// the rig's own weights (the same top-4 normalized influences the mesh uses).</summary>
    public Vector3[] Repose(Vector3[] raw, MakeHumanRig rig)
    {
        var result = new Vector3[raw.Length];
        for (int v = 0; v < raw.Length; v++)
        {
            var influences = rig.RawTopInfluences(v);
            if (influences.Count == 0) { result[v] = Skin[0].MultiplyPoint3x4(raw[v]); continue; }
            Vector3 p = Vector3.zero;
            foreach (var (bone, weight) in influences) p += weight * Skin[bone].MultiplyPoint3x4(raw[v]);
            result[v] = p;
        }
        return result;
    }

    /// <summary>Uniform scale + ground shift applied after re-posing (straightening the legs changes the
    /// height by a few mm), so the T-pose avatar keeps the canonical height with its soles on y = 0.</summary>
    public void Normalize(float groundY, float scale)
    {
        for (int i = 0; i < Position.Length; i++)
        {
            var p = Position[i];
            Position[i] = new Vector3(p.x * scale, (p.y - groundY) * scale, p.z * scale);
        }
    }
}
