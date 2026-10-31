using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The per-submesh triangle index lists of one imported `_cuts` mesh ASSET — read once and shared by every
/// avatar wearing that mesh (reference-counted; released with the last avatar). Every rebuild of a garment
/// takes its visible regions' lists from here, never from the avatar's own (already merged) runtime mesh.
///
/// Reading indices needs a CPU-readable mesh: GarmentCutsImporter imports `_cuts` models with
/// Read/Write enabled (the existing garment imports already do), and SegmentedGarmentSupport rejects a
/// non-readable mesh up front (legacy path) instead of failing here.
/// </summary>
public sealed class SegmentedGarmentTopology
{
    private static readonly Dictionary<Mesh, SegmentedGarmentTopology> s_cache = new();
    private static readonly List<int> s_read = new();

    public Mesh Source { get; }
    public int SubMeshCount => _submeshIndices.Length;
    public IndexFormat IndexFormat { get; }
    public int VertexCount { get; }

    private readonly int[][] _submeshIndices;
    private int _references;

    /// <summary>Live cached topologies — diagnostics/tests (0 once every avatar was disposed).</summary>
    public static int CachedCount => s_cache.Count;

    private SegmentedGarmentTopology(Mesh source)
    {
        Source = source;
        IndexFormat = source.indexFormat;
        VertexCount = source.vertexCount;
        _submeshIndices = new int[source.subMeshCount][];
        for (int i = 0; i < _submeshIndices.Length; i++)
        {
            if (source.GetTopology(i) != MeshTopology.Triangles)
            {
                GarmentDiagnostics.Warn($"Mesh '{source.name}' submesh {i} is {source.GetTopology(i)}, not triangles — it is kept as-is in the visible geometry.");
            }
            s_read.Clear();
            source.GetIndices(s_read, i, applyBaseVertex: true);
            _submeshIndices[i] = s_read.ToArray();
        }
        s_read.Clear();
    }

    public static SegmentedGarmentTopology Acquire(Mesh source)
    {
        if (!s_cache.TryGetValue(source, out var topology))
        {
            topology = new SegmentedGarmentTopology(source);
            s_cache.Add(source, topology);
        }
        topology._references++;
        return topology;
    }

    public void Release()
    {
        if (_references <= 0) return;
        if (--_references == 0) s_cache.Remove(Source);
    }

    public int TriangleCount(int submesh) => _submeshIndices[submesh].Length / 3;

    /// <summary>Appends submesh `submesh`'s indices to `target` (no allocation once `target` has grown).</summary>
    public void AppendIndices(int submesh, List<int> target) => target.AddRange(_submeshIndices[submesh]);

    /// <summary>Approximate managed memory held by the cached index lists (bytes).</summary>
    public long IndexMemoryBytes
    {
        get
        {
            long total = 0;
            foreach (var list in _submeshIndices) total += list.Length * sizeof(int);
            return total;
        }
    }
}
