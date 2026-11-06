using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One layered garment renderer on ONE avatar: owns nothing shared. Its runtime mesh is the avatar's own clone
/// of the imported mesh (made by AvatarClothingBinder — vertices, order, skin weights, bind poses, UVs, normals,
/// tangents, colors and every frame of every blendshape come with the clone untouched). ApplyHiddenVertices only
/// rewrites the INDEX data: the triangles with at least one visible vertex, in source order, merged into a
/// single submesh drawn with the garment's final material. Vertex buffers are never modified, so hidden vertices
/// keep their weights and blendshape deltas (they cost memory, not draw time).
///
/// A rebuild with an unchanged state key does nothing. Bounds are not recalculated (the renderer keeps the
/// body's localBounds), and blendshape weights are re-applied after the index change.
/// </summary>
public sealed class SegmentedGarmentRenderer
{
    public SkinnedMeshRenderer Renderer { get; }
    public Mesh RuntimeMesh { get; }
    public SegmentedGarmentTopology Topology { get; }
    public int VisibleTriangles { get; private set; }
    public int TotalTriangles { get; }

    private readonly Material[] _finalMaterials;
    private readonly float[] _blendWeights;
    private bool _applied;
    private string _stateKey;
    private bool _runtimeHoldsSourceIndices = true; // the clone starts with the source's submeshes/indices

    public SegmentedGarmentRenderer(SkinnedMeshRenderer renderer, Mesh runtimeMesh, SegmentedGarmentTopology topology, Material finalMaterial)
    {
        Renderer = renderer;
        RuntimeMesh = runtimeMesh;
        Topology = topology;
        _finalMaterials = new[] { finalMaterial };
        _blendWeights = new float[runtimeMesh.blendShapeCount];
        for (int i = 0; i < topology.SubMeshCount; i++) TotalTriangles += topology.TriangleCount(i);

        // Vertex indices above 65535 need 32-bit index buffers; the clone already carries the source format,
        // this only guards a source that was imported with the wrong one.
        if (runtimeMesh.vertexCount > 65535 && runtimeMesh.indexFormat != IndexFormat.UInt32) runtimeMesh.indexFormat = IndexFormat.UInt32;
    }

    /// <summary>Keeps every triangle with at least one visible vertex (`hiddenVertex` null = everything
    /// visible), as ONE submesh with the final material. `stateKey` identifies the hiding garments: an
    /// unchanged key does nothing. `scratch` is reused between calls.</summary>
    public void ApplyHiddenVertices(bool[] hiddenVertex, string stateKey, List<int> scratch)
    {
        if (_applied && _stateKey == stateKey) return;
        _applied = true;
        _stateKey = stateKey;

        scratch.Clear();
        for (int i = 0; i < Topology.SubMeshCount; i++)
        {
            int start = scratch.Count;
            Topology.AppendIndices(i, scratch);
            if (hiddenVertex == null) continue;
            int write = start;
            for (int t = start; t + 2 < scratch.Count; t += 3)
            {
                int a = scratch[t], b = scratch[t + 1], c = scratch[t + 2];
                if (hiddenVertex[a] && hiddenVertex[b] && hiddenVertex[c]) continue;
                scratch[write] = a; scratch[write + 1] = b; scratch[write + 2] = c;
                write += 3;
            }
            scratch.RemoveRange(write, scratch.Count - write);
        }
        VisibleTriangles = scratch.Count / 3;

        // A runtime mesh that still holds the source indices and whose source already IS the final layout
        // (one submesh, nothing hidden) is reused as it is.
        bool sourceIsFinal = Topology.SubMeshCount == 1 && hiddenVertex == null;
        for (int i = 0; i < _blendWeights.Length; i++) _blendWeights[i] = Renderer.GetBlendShapeWeight(i);
        if (!(sourceIsFinal && _runtimeHoldsSourceIndices))
        {
            RuntimeMesh.subMeshCount = 1;
            RuntimeMesh.SetIndices(scratch, MeshTopology.Triangles, 0, calculateBounds: false);
            _runtimeHoldsSourceIndices = sourceIsFinal;
        }
        Renderer.sharedMaterials = _finalMaterials;
        for (int i = 0; i < _blendWeights.Length; i++) Renderer.SetBlendShapeWeight(i, _blendWeights[i]);
        Renderer.enabled = VisibleTriangles > 0;
    }
}
