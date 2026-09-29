using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// One renderable piece of the MakeHuman base mesh — becomes its own SkinnedMeshRenderer (same
/// skeleton, same presets/blendshapes contract). Body and Eyes are split so the body's skin-tone
/// MaterialPropertyBlock (applied renderer-wide by AvatarFactory) never tints the eyes.
/// </summary>
public class MakeHumanMeshPart
{
    public string Name;               // == the SkinnedMeshRenderer GameObject name == preset rendererName
    public string[] Groups;           // OBJ groups merged into this part
    public Vector2[] UVs;
    public int[] MeshToRawIndex;
    public int[] Triangles;

    public int VertexCount => MeshToRawIndex.Length;

    /// <summary>Raw-space positions -> this part's mesh-buffer positions (duplicating seam vertices),
    /// minus `origin` — the base's own ground joint, so every baked base stands with its feet at y = 0.</summary>
    public Vector3[] ToMeshPositions(Vector3[] rawPositions, Vector3 origin)
    {
        var result = new Vector3[MeshToRawIndex.Length];
        for (int m = 0; m < result.Length; m++) result[m] = rawPositions[MeshToRawIndex[m]] - origin;
        return result;
    }

    /// <summary>
    /// Smooth normals merged ACROSS UV seams (accumulated per RAW vertex, then copied to every Unity
    /// vertex sharing it) — Unity's own Mesh.RecalculateNormals treats seam duplicates as separate
    /// vertices and leaves visible lighting creases along every UV seam. Area-weighted face normals.
    /// </summary>
    public Vector3[] ComputeSeamlessNormals(Vector3[] meshPositions, int rawVertexCount)
    {
        var rawNormals = new Vector3[rawVertexCount];
        for (int t = 0; t < Triangles.Length; t += 3)
        {
            int a = Triangles[t], b = Triangles[t + 1], c = Triangles[t + 2];
            Vector3 n = Vector3.Cross(meshPositions[b] - meshPositions[a], meshPositions[c] - meshPositions[a]);
            rawNormals[MeshToRawIndex[a]] += n;
            rawNormals[MeshToRawIndex[b]] += n;
            rawNormals[MeshToRawIndex[c]] += n;
        }

        var result = new Vector3[meshPositions.Length];
        for (int m = 0; m < result.Length; m++)
        {
            Vector3 n = rawNormals[MeshToRawIndex[m]];
            result[m] = n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;
        }
        return result;
    }
}

/// <summary>
/// The parsed MakeHuman base mesh (hm08 base.obj, bundled as Source/Mesh/base_mesh.txt) — ported
/// from AvatarLab's MakeHumanBodyMesh + MakeHumanBodyMeshLoader, with two MusicGame additions:
///   - MORE THAN ONE rendered part (Body + Eyes), each its own MakeHumanMeshPart/renderer.
///   - Every OBJ group's raw vertex set is kept (GroupRawVertices), because the MakeHuman rig
///     resolves each joint as the mean of a "joint-*" helper cube's vertices (see MakeHumanRig).
///
/// Why not a Unity OBJ import: MakeHuman .target files (and rig weights) address vertices by their
/// RAW OBJ "v" index, but a Unity vertex is a (position, uv) pair — a raw vertex on a UV seam
/// becomes several Unity vertices. MeshToRawIndex keeps that mapping explicit (AvatarLab's own
/// design, unchanged).
///
/// Editor-only: runtime never parses MakeHuman data — it consumes the baked meshes/presets.
/// </summary>
public class MakeHumanSourceMesh
{
    /// <summary>MakeHuman topology this pipeline (targets + rig + weights) was authored for. Every
    /// target/rig file indexes into THIS exact vertex list, so a different base mesh is refused
    /// outright instead of being "mapped" — see Validate.</summary>
    public const string ExpectedBasemeshTag = "hm08";
    public const int ExpectedRawVertexCount = 19158;

    public static readonly (string part, string[] groups)[] PartLayout =
    {
        ("Body", new[] { "body" }),
        ("Eyes", new[] { "helper-l-eye", "helper-r-eye" }),
    };

    public string BasemeshTag;
    public Vector3[] RawPositions;
    public MakeHumanMeshPart[] Parts;
    public Dictionary<string, HashSet<int>> GroupRawVertices;

    public static MakeHumanSourceMesh Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"MakeHuman base mesh not found at '{path}'.", path);
        return Parse(File.ReadAllText(path));
    }

    private class PartBuilder
    {
        public readonly List<Vector2> UVs = new List<Vector2>(16000);
        public readonly List<int> MeshToRaw = new List<int>(16000);
        public readonly List<int> Triangles = new List<int>(60000);
        public readonly Dictionary<long, int> KeyToMeshIndex = new Dictionary<long, int>(16000);
    }

    private static MakeHumanSourceMesh Parse(string text)
    {
        var rawPositions = new List<Vector3>(20000);
        var uvs = new List<Vector2>(22000);
        var builders = new PartBuilder[PartLayout.Length];
        for (int i = 0; i < builders.Length; i++) builders[i] = new PartBuilder();

        var groups = new Dictionary<string, HashSet<int>>();
        var face = new List<int>(4);
        var faceRaw = new List<(int pos, int uv)>(4);
        string basemeshTag = null;
        string currentGroup = null;
        HashSet<int> currentGroupSet = null;
        PartBuilder currentPart = null;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line[0] == '#')
            {
                // "# basemesh hm08" — MakeHuman's own topology tag.
                int idx = line.IndexOf("basemesh", System.StringComparison.Ordinal);
                if (idx >= 0) basemeshTag = line.Substring(idx + "basemesh".Length).Trim();
                continue;
            }

            if (line.StartsWith("v ", System.StringComparison.Ordinal))
            {
                string[] p = Split(line.Substring(2));
                rawPositions.Add(MakeHumanUnits.ToUnity(ParseFloat(p[0]), ParseFloat(p[1]), ParseFloat(p[2])));
            }
            else if (line.StartsWith("vt ", System.StringComparison.Ordinal))
            {
                string[] p = Split(line.Substring(3));
                uvs.Add(new Vector2(ParseFloat(p[0]), ParseFloat(p[1])));
            }
            else if (line.StartsWith("g ", System.StringComparison.Ordinal))
            {
                currentGroup = line.Substring(2).Trim();
                if (!groups.TryGetValue(currentGroup, out currentGroupSet))
                {
                    currentGroupSet = new HashSet<int>();
                    groups[currentGroup] = currentGroupSet;
                }
                currentPart = null;
                for (int i = 0; i < PartLayout.Length; i++)
                    if (System.Array.IndexOf(PartLayout[i].groups, currentGroup) >= 0) currentPart = builders[i];
            }
            else if (line.StartsWith("f ", System.StringComparison.Ordinal) && currentGroup != null)
            {
                faceRaw.Clear();
                foreach (string token in Split(line.Substring(2)))
                {
                    int slash = token.IndexOf('/');
                    int posIdx = int.Parse(slash < 0 ? token : token.Substring(0, slash), CultureInfo.InvariantCulture) - 1;
                    int uvIdx = slash < 0 ? -1 : int.Parse(token.Substring(slash + 1), CultureInfo.InvariantCulture) - 1;
                    currentGroupSet.Add(posIdx);
                    faceRaw.Add((posIdx, uvIdx));
                }
                if (currentPart == null) continue;

                face.Clear();
                foreach (var (posIdx, uvIdx) in faceRaw)
                {
                    long key = ((long)posIdx << 32) | (uint)(uvIdx + 1);
                    if (!currentPart.KeyToMeshIndex.TryGetValue(key, out int meshIndex))
                    {
                        meshIndex = currentPart.MeshToRaw.Count;
                        currentPart.KeyToMeshIndex[key] = meshIndex;
                        currentPart.MeshToRaw.Add(posIdx);
                        currentPart.UVs.Add(uvIdx >= 0 && uvIdx < uvs.Count ? uvs[uvIdx] : Vector2.zero);
                    }
                    face.Add(meshIndex);
                }

                // Fan-triangulate quads/tris, reversing winding to compensate for the single
                // handedness reflection in MakeHumanUnits.ToUnity (AvatarLab's own convention).
                for (int i = 1; i < face.Count - 1; i++)
                {
                    currentPart.Triangles.Add(face[0]);
                    currentPart.Triangles.Add(face[i + 1]);
                    currentPart.Triangles.Add(face[i]);
                }
            }
        }

        var parts = new MakeHumanMeshPart[PartLayout.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = new MakeHumanMeshPart
            {
                Name = PartLayout[i].part,
                Groups = PartLayout[i].groups,
                UVs = builders[i].UVs.ToArray(),
                MeshToRawIndex = builders[i].MeshToRaw.ToArray(),
                Triangles = builders[i].Triangles.ToArray(),
            };
        }

        return new MakeHumanSourceMesh
        {
            BasemeshTag = basemeshTag,
            RawPositions = rawPositions.ToArray(),
            Parts = parts,
            GroupRawVertices = groups,
        };
    }

    /// <summary>Hard topology gate — the rig, weights and every target index into THIS exact raw
    /// vertex list. Anything else fails loudly (never nearest-neighbour mapped).</summary>
    public void Validate(List<string> errors)
    {
        if (BasemeshTag != ExpectedBasemeshTag)
            errors.Add($"Base mesh topology tag is '{BasemeshTag ?? "<missing>"}' but the bundled targets/rig are authored for '{ExpectedBasemeshTag}'.");
        if (RawPositions.Length != ExpectedRawVertexCount)
            errors.Add($"Base mesh has {RawPositions.Length} raw vertices; MakeHuman {ExpectedBasemeshTag} has exactly {ExpectedRawVertexCount}.");
        foreach (var part in Parts)
        {
            foreach (string group in part.Groups)
                if (!GroupRawVertices.ContainsKey(group))
                    errors.Add($"Base mesh has no '{group}' group — part '{part.Name}' is missing geometry.");
            if (part.Triangles.Length == 0)
                errors.Add($"Base mesh part '{part.Name}' produced no triangles.");
        }
    }

    private static string[] Split(string s) => s.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);

    private static float ParseFloat(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
