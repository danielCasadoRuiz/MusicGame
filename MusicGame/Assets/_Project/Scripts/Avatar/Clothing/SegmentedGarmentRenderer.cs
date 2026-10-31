using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One segmented garment renderer on ONE avatar: owns nothing shared. Its runtime mesh is the avatar's own
/// clone of the `_cuts` mesh (made by AvatarClothingBinder — vertices, order, skin weights, bind poses, UVs,
/// normals, tangents, colors and every frame of every blendshape come with the clone untouched). Apply only
/// rewrites the INDEX data: the triangles of the visible regions, in source order, merged into a single
/// submesh drawn with the garment's final material. Vertex buffers are never modified, so hidden vertices
/// keep their weights and blendshape deltas (they cost memory, not draw time).
///
/// A rebuild with an unchanged visible set does nothing. Bounds are not recalculated (the renderer keeps the
/// body's localBounds), and blendshape weights are re-applied after the index change.
/// </summary>
public sealed class SegmentedGarmentRenderer
{
    public SkinnedMeshRenderer Renderer { get; }
    public Mesh RuntimeMesh { get; }
    public SegmentedGarmentTopology Topology { get; }
    public int VisibleTriangles { get; private set; }
    public int TotalTriangles { get; }

    private readonly string[] _regions;
    private readonly bool[] _visible;
    private readonly Material[] _finalMaterials;
    private readonly float[] _blendWeights;
    private bool _applied;
    private bool _runtimeHoldsSourceIndices = true; // the clone starts with the source's submeshes/indices

    public SegmentedGarmentRenderer(SkinnedMeshRenderer renderer, Mesh runtimeMesh, SegmentedGarmentTopology topology,
                                    string[] submeshRegions, Material finalMaterial)
    {
        Renderer = renderer;
        RuntimeMesh = runtimeMesh;
        Topology = topology;
        _regions = submeshRegions;
        _visible = new bool[topology.SubMeshCount];
        _finalMaterials = new[] { finalMaterial };
        _blendWeights = new float[runtimeMesh.blendShapeCount];
        for (int i = 0; i < topology.SubMeshCount; i++) TotalTriangles += topology.TriangleCount(i);

        // Vertex indices above 65535 need 32-bit index buffers; the clone already carries the source format,
        // this only guards a source that was imported with the wrong one.
        if (runtimeMesh.vertexCount > 65535 && runtimeMesh.indexFormat != IndexFormat.UInt32) runtimeMesh.indexFormat = IndexFormat.UInt32;
    }

    /// <summary>Region of submesh `index` ("" = unresolved, always kept).</summary>
    public string RegionOf(int submesh) => _regions[submesh] ?? "";

    /// <summary>Keeps every triangle whose region is not in `hidden` (unresolved and "unassigned" geometry is
    /// always kept), as ONE submesh with the final material. `scratch` is reused between calls.</summary>
    public void Apply(HashSet<string> hidden, List<int> scratch)
    {
        bool changed = !_applied;
        for (int i = 0; i < _visible.Length; i++)
        {
            string region = _regions[i];
            bool visible = string.IsNullOrEmpty(region) || region == GarmentRegionCatalogSO.UnassignedRegion || !hidden.Contains(region);
            changed |= _visible[i] != visible;
            _visible[i] = visible;
        }
        if (!changed) return;
        _applied = true;

        scratch.Clear();
        for (int i = 0; i < _visible.Length; i++)
            if (_visible[i]) Topology.AppendIndices(i, scratch);
        VisibleTriangles = scratch.Count / 3;

        for (int i = 0; i < _blendWeights.Length; i++) _blendWeights[i] = Renderer.GetBlendShapeWeight(i);

        // Same single-submesh grouping whether or not anything is hidden. A runtime mesh that still holds the
        // source indices and whose source already IS the final layout (one slot, visible) is reused as it is.
        bool sourceIsFinal = Topology.SubMeshCount == 1 && _visible[0];
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
