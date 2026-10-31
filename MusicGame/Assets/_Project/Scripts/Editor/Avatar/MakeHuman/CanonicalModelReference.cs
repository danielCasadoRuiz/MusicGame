using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Read-only editor snapshot of the imported AvatarLab model; never generates geometry.</summary>
public sealed class CanonicalModelReference
{
    public sealed class Part { public string Name; public Vector2[] UVs; }
    public sealed class SourceData { public Part[] Parts; }
    public sealed class Geometry { public Vector3[] Vertices; public Vector3[] Normals; }
    public sealed class State
    {
        public string Name;
        public Geometry[] Parts;
        public float Height, GroundY;
    }
    public sealed class Bone { public string Name, Parent; }
    public sealed class RigData { public string RootBoneName = "Root"; public List<Bone> Bones = new(); }
    public sealed class Pose { public Vector3[] Position; }
    public SourceData Source;
    public RigData Rig = new();
    public Pose TPose = new();
    public State Male, Female;
    public Dictionary<MorphChannel, State> Morphs = new();
    public Bounds RendererBounds;
    public List<string> Report = new();
    public static readonly MorphChannel[] BodyChannels = { MorphChannel.MaleSlim, MorphChannel.MaleHeavy, MorphChannel.MaleMuscle, MorphChannel.FemaleSlim, MorphChannel.FemaleHeavy, MorphChannel.FemaleMuscle };
    public static BodyBaseType BaseOf(MorphChannel channel) => channel.ToString().StartsWith("Female", StringComparison.Ordinal) ? BodyBaseType.Female : BodyBaseType.Male;
    public State GetBase(BodyBaseType type) => type == BodyBaseType.Male ? Male : Female;
    public IEnumerable<State> AllStates() { yield return Male; yield return Female; foreach (var channel in BodyChannels) yield return Morphs[channel]; }

    public static CanonicalModelReference Read(List<string> errors)
    {
        try
        {
            var model = CanonicalAvatarModel.LoadModel();
            if (model == null) throw new InvalidOperationException("Import the canonical FBX exported by AvatarLab first.");
            var result = new CanonicalModelReference();
            string[] names = { "Body", "Eyes" };
            result.Source = new SourceData { Parts = new Part[names.Length] };
            var meshes = new Mesh[names.Length];
            for (int p = 0; p < names.Length; p++)
            {
                meshes[p] = CanonicalAvatarModel.LoadMesh(names[p]);
                if (meshes[p] == null) throw new InvalidOperationException("Missing canonical mesh: " + names[p]);
                result.Source.Parts[p] = new Part { Name = names[p], UVs = meshes[p].uv };
            }
            State ReadState(string name, MorphChannel? channel = null)
            {
                var state = new State { Name = name, Parts = new Geometry[names.Length] };
                for (int p = 0; p < names.Length; p++)
                {
                    var vertices = meshes[p].vertices;
                    if (channel.HasValue)
                    {
                        AddChannel(meshes[p], vertices, channel.Value);
                        if (BaseOf(channel.Value) == BodyBaseType.Female) AddChannel(meshes[p], vertices, MorphChannel.Gender);
                    }
                    else if (name == "FemaleBase") AddChannel(meshes[p], vertices, MorphChannel.Gender);
                    state.Parts[p] = new Geometry { Vertices = vertices, Normals = meshes[p].normals };
                }
                float low = float.MaxValue, high = float.MinValue;
                foreach (var v in state.Parts[0].Vertices) { low = Mathf.Min(low, v.y); high = Mathf.Max(high, v.y); }
                state.GroundY = low; state.Height = high - low;
                return state;
            }
            result.Male = ReadState("MaleBase"); result.Female = ReadState("FemaleBase");
            foreach (var channel in BodyChannels) result.Morphs[channel] = ReadState(channel.ToString(), channel);
            var body = Array.Find(model.GetComponentsInChildren<SkinnedMeshRenderer>(true), r => r.name == "Body");
            var positions = new List<Vector3>();
            foreach (var bone in body.bones)
            {
                result.Rig.Bones.Add(new Bone { Name = bone.name, Parent = bone.parent != null && Array.IndexOf(body.bones, bone.parent) >= 0 ? bone.parent.name : null });
                positions.Add(model.transform.InverseTransformPoint(bone.position));
            }
            result.TPose.Position = positions.ToArray();
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var state in result.AllStates()) foreach (var part in state.Parts) foreach (var vertex in part.Vertices) bounds.Encapsulate(vertex);
            bounds.Expand(.3f); result.RendererBounds = bounds;
            result.Report.Add("Consumed existing AvatarLab canonical FBX; no source evaluation or geometry generation.");
            return result;
        }
        catch (Exception exception) { errors.Add(exception.Message); return null; }
    }

    private static void AddChannel(Mesh mesh, Vector3[] vertices, MorphChannel channel)
    {
        int index = mesh.GetBlendShapeIndex(channel.ToString());
        if (index < 0 || mesh.GetBlendShapeFrameCount(index) != 1 || Mathf.Abs(mesh.GetBlendShapeFrameWeight(index, 0) - 100f) > .001f)
            throw new InvalidOperationException("Invalid imported channel: " + channel);
        var delta = new Vector3[mesh.vertexCount];
        mesh.GetBlendShapeFrameVertices(index, 0, delta, null, null);
        for (int i = 0; i < vertices.Length; i++) vertices[i] += delta[i];
    }
}
