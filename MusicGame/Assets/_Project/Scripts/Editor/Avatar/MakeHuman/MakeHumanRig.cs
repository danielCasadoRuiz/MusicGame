using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// The MakeHuman "game_engine" rig (CC0, from the same makehumancommunity/mpfb2 data tree AvatarLab
/// sourced the base mesh/targets from — Source/Rig/rig.game_engine.json + weights.game_engine.json).
/// AvatarLab never rigged its mesh; this is MusicGame's addition, but it uses MakeHuman's OWN joint
/// definitions rather than inventing positions:
///   - every bone head uses strategy "CUBE": the joint sits at the MEAN of the named "joint-*"
///     helper cube's vertices in base.obj. Those helper vertices are moved by the macro targets like
///     any other vertex, so evaluating joints on MaleBase vs FemaleBase gives each base its own
///     correctly-fitted skeleton — same hierarchy/names, different rest positions.
///   - weights are keyed by RAW base-mesh vertex index (same space as targets).
///
/// Bone orientation: bones are world-axis-aligned at rest (identity rotations). Blender rolls are
/// irrelevant to Unity Humanoid (Mecanim retargets in its own muscle space), and axis-aligned rest
/// bones make bindposes pure translations — simple and exact.
/// </summary>
public class MakeHumanRig
{
    public class Bone
    {
        public string Name;
        public string Parent;       // null for the root
        public string HeadCube;     // "joint-*" group whose vertex mean is this bone's head
        public int[] HeadVertices;  // resolved raw vertex indices of HeadCube
        public int[] TailVertices;  // same for the bone's tail (defines the bone's axis in the FBX)
        public float Roll;          // MakeHuman/Blender bone roll (radians) — part of the rig's own bone orientation
    }

    /// <summary>Parents-before-children order (the order transforms/bindposes/bones[] use).</summary>
    public readonly List<Bone> Bones = new List<Bone>();
    public readonly Dictionary<string, int> BoneIndex = new Dictionary<string, int>();

    /// <summary>Per raw vertex: (boneIndex, weight) influences, unnormalized, as authored.</summary>
    public List<(int bone, float weight)>[] RawWeights;

    public string RootBoneName => Bones[0].Name;

    public static MakeHumanRig Load(string rigPath, string weightsPath, MakeHumanSourceMesh mesh, List<string> errors)
    {
        var rig = new MakeHumanRig();
        var rigJson = JObject.Parse(File.ReadAllText(rigPath));

        // Collect, then order parents-first (the JSON is alphabetical).
        var byName = new Dictionary<string, Bone>();
        foreach (var property in rigJson.Properties())
        {
            var b = (JObject)property.Value;
            var head = (JObject)b["head"];
            string strategy = (string)head["strategy"];
            var bone = new Bone
            {
                Name = property.Name,
                Parent = string.IsNullOrEmpty((string)b["parent"]) ? null : (string)b["parent"],
            };

            if (strategy == "CUBE")
            {
                bone.HeadCube = (string)head["cube_name"];
                if (mesh.GroupRawVertices.TryGetValue(bone.HeadCube, out var set) && set.Count > 0)
                {
                    bone.HeadVertices = new int[set.Count];
                    set.CopyTo(bone.HeadVertices);
                    System.Array.Sort(bone.HeadVertices);
                }
                else
                {
                    errors.Add($"Rig bone '{bone.Name}' uses joint cube '{bone.HeadCube}', which the base mesh doesn't have — rig/mesh topology mismatch.");
                }
            }
            else if (strategy == "MEAN" || strategy == "VERTEX")
            {
                var indices = new List<int>();
                foreach (var v in (JArray)head["vertex_indices"]) indices.Add((int)v);
                bone.HeadVertices = indices.ToArray();
            }
            else
            {
                errors.Add($"Rig bone '{bone.Name}' uses unsupported head strategy '{strategy}'.");
            }
            bone.TailVertices = ResolveVertices((JObject)b["tail"], mesh, bone.Name + " tail", errors);
            bone.Roll = b["roll"] != null ? (float)b["roll"] : 0f;
            byName[bone.Name] = bone;
        }

        var visited = new HashSet<string>();
        void Visit(Bone bone)
        {
            if (visited.Contains(bone.Name)) return;
            if (bone.Parent != null)
            {
                if (byName.TryGetValue(bone.Parent, out var parent)) Visit(parent);
                else errors.Add($"Rig bone '{bone.Name}' has unknown parent '{bone.Parent}'.");
            }
            visited.Add(bone.Name);
            rig.BoneIndex[bone.Name] = rig.Bones.Count;
            rig.Bones.Add(bone);
        }
        foreach (var bone in byName.Values) if (bone.Parent == null) Visit(bone);
        foreach (var bone in byName.Values) Visit(bone);

        int roots = 0;
        foreach (var bone in rig.Bones) if (bone.Parent == null) roots++;
        if (roots != 1) errors.Add($"Rig must have exactly one root bone, found {roots}.");

        foreach (var bone in rig.Bones)
            if (bone.HeadVertices != null)
                foreach (int v in bone.HeadVertices)
                    if (v < 0 || v >= mesh.RawPositions.Length)
                    { errors.Add($"Rig bone '{bone.Name}' head references raw vertex {v}, outside the base mesh."); break; }

        // Weights: { "weights": { "<bone>": [[rawIndex, weight], ...] } }
        rig.RawWeights = new List<(int, float)>[mesh.RawPositions.Length];
        var weightsJson = (JObject)JObject.Parse(File.ReadAllText(weightsPath))["weights"];
        foreach (var property in weightsJson.Properties())
        {
            if (!rig.BoneIndex.TryGetValue(property.Name, out int boneIndex))
            {
                errors.Add($"Weights reference bone '{property.Name}', which the rig doesn't define.");
                continue;
            }
            foreach (JArray pair in (JArray)property.Value)
            {
                int raw = (int)pair[0];
                float w = (float)pair[1];
                if (raw < 0 || raw >= mesh.RawPositions.Length)
                {
                    errors.Add($"Weights for bone '{property.Name}' reference raw vertex {raw}, outside the base mesh — incompatible topology.");
                    break;
                }
                if (w <= 0f) continue;
                (rig.RawWeights[raw] ??= new List<(int, float)>(4)).Add((boneIndex, w));
            }
        }

        return rig;
    }

    private static int[] ResolveVertices(JObject end, MakeHumanSourceMesh mesh, string label, List<string> errors)
    {
        if (end == null) { errors.Add($"Rig {label}: missing definition."); return null; }
        string strategy = (string)end["strategy"];
        if (strategy == "CUBE")
        {
            string cube = (string)end["cube_name"];
            if (mesh.GroupRawVertices.TryGetValue(cube, out var set) && set.Count > 0)
            {
                var result = new int[set.Count];
                set.CopyTo(result);
                System.Array.Sort(result);
                return result;
            }
            errors.Add($"Rig {label} uses joint cube '{cube}', which the base mesh doesn't have.");
            return null;
        }
        if (strategy == "MEAN" || strategy == "VERTEX")
        {
            var indices = new List<int>();
            foreach (var v in (JArray)end["vertex_indices"]) indices.Add((int)v);
            return indices.ToArray();
        }
        errors.Add($"Rig {label} uses unsupported strategy '{strategy}'.");
        return null;
    }

    /// <summary>Tail positions (mean of each bone's tail vertex set) for one evaluated body shape.</summary>
    public Vector3[] ResolveTails(Vector3[] rawPositions)
    {
        var tails = new Vector3[Bones.Count];
        for (int i = 0; i < Bones.Count; i++)
        {
            var verts = Bones[i].TailVertices;
            if (verts == null || verts.Length == 0) continue;
            Vector3 sum = Vector3.zero;
            foreach (int v in verts) sum += rawPositions[v];
            tails[i] = sum / verts.Length;
        }
        return tails;
    }

    /// <summary>World-space (model-space) joint positions for one evaluated body shape.</summary>
    public Vector3[] ResolveJoints(Vector3[] rawPositions)
    {
        var joints = new Vector3[Bones.Count];
        for (int i = 0; i < Bones.Count; i++)
        {
            var verts = Bones[i].HeadVertices;
            if (verts == null || verts.Length == 0) continue;
            Vector3 sum = Vector3.zero;
            foreach (int v in verts) sum += rawPositions[v];
            joints[i] = sum / verts.Length;
        }
        return joints;
    }

    /// <summary>The 4 strongest authored influences of one RAW vertex, renormalized — the single
    /// skin-weight rule used everywhere (T-pose re-posing, the FBX vertex groups, validation). Unity's
    /// default skin quality is 4 bones; MakeHuman authors up to 7 on ~1k vertices, all small tails. A
    /// vertex with no influences is bound 100% to the root bone. Unweighted = true in that case.</summary>
    public List<(int bone, float weight)> RawTopInfluences(int raw) => RawTopInfluences(raw, out _);

    public List<(int bone, float weight)> RawTopInfluences(int raw, out bool unweighted)
    {
        var list = new List<(int bone, float weight)>(4);
        var influences = RawWeights[raw];
        unweighted = influences == null || influences.Count == 0;
        if (unweighted) { list.Add((0, 1f)); return list; }

        var sorted = new List<(int bone, float weight)>(influences);
        sorted.Sort((a, b) => b.weight.CompareTo(a.weight));
        int count = Mathf.Min(4, sorted.Count);
        float total = 0f;
        for (int i = 0; i < count; i++) total += sorted[i].weight;
        for (int i = 0; i < count; i++) list.Add((sorted[i].bone, sorted[i].weight / total));
        return list;
    }

    /// <summary>Number of rendered raw vertices of `part` with no authored weights (bound to root).</summary>
    public int CountUnweighted(MakeHumanMeshPart part)
    {
        var seen = new HashSet<int>(part.MeshToRawIndex);
        int count = 0;
        foreach (int raw in seen) if (RawWeights[raw] == null || RawWeights[raw].Count == 0) count++;
        return count;
    }
}
