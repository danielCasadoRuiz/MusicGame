using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Approved final T-pose geometry, preserving the original rig and raw-index mapping.</summary>
public static class MakeHumanApprovedShapes
{
    [Serializable] private class Payload { public int version; public State[] states; }
    [Serializable] private class State { public string name; public Part[] parts; }
    [Serializable] private class Part { public string name; public float[] positions; }
    public static bool Apply(MakeHumanBakeResult bake, List<string> errors)
    {
        string path = Path.Combine(MakeHumanBodyBaker.SourceRoot, "Approved/canonical_shapes.json");
        if (!File.Exists(path)) return false;
        try
        {
            var payload = JsonUtility.FromJson<Payload>(File.ReadAllText(path));
            if (payload == null || payload.version != 1 || payload.states == null || payload.states.Length != 8)
                throw new InvalidDataException("Expected version 1 and all eight approved states.");
            var states = new Dictionary<string, State>();
            foreach (var state in payload.states) states.Add(state.name, state);
            foreach (var target in bake.AllStates())
            {
                if (!states.TryGetValue(target.Name, out var state)) throw new InvalidDataException("Missing " + target.Name);
                if (state.parts == null || state.parts.Length != bake.Source.Parts.Length) throw new InvalidDataException("Wrong part count: " + target.Name);
                for (int p = 0; p < bake.Source.Parts.Length; p++)
                {
                    var part = bake.Source.Parts[p];
                    if (state.parts[p].name != part.Name) throw new InvalidDataException("Wrong part order.");
                    var ids = new List<int>(new HashSet<int>(part.MeshToRawIndex)); ids.Sort();
                    var flat = state.parts[p].positions;
                    if (flat == null || flat.Length != ids.Count * 3) throw new InvalidDataException("Wrong vertex count: " + target.Name + "/" + part.Name);
                    for (int i = 0; i < ids.Count; i++)
                    {
                        var v = new Vector3(flat[i*3], flat[i*3+1], flat[i*3+2]);
                        if (!float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z)) throw new InvalidDataException("Non-finite approved position.");
                        target.Raw[ids[i]] = v;
                    }
                }
            }
            bake.Report.Add("Approved canonical endpoints loaded from Source/Approved/canonical_shapes.json (original skeleton preserved).");
            return true;
        }
        catch (Exception e) { errors.Add("Approved canonical shapes: " + e.Message); return false; }
    }
}
