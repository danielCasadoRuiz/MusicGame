using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// The equipped appearance of ONE avatar for its layered garments: which triangles each garment shows (its
/// vertices lying under a higher-layer garment that hides its category are hidden — occlusion masks, see
/// GarmentOcclusionData) and which skin the body shows (maximum of the equipped garments' skin maps).
/// Owned by AvatarInstance (AvatarInstance.Appearance); AvatarFactory adds/removes garments and calls
/// Rebuild after every equipment change — and only then: the derived meshes and the body mask stay as they
/// are until the equipment changes again.
///
/// Nothing here is shared between avatars except read-only data: the source-mesh topology cache, the decoded
/// occlusion data and the composed body masks (identical garment sets reuse one texture). Each avatar has its
/// own runtime meshes, renderers, blendshape weights and MaterialPropertyBlock; shared materials are never modified.
///
/// Garments without layering metadata never pass through here — see AvatarFactory's legacy section.
/// </summary>
public sealed class AvatarAppearance
{
    public static readonly int SkinVisibilityMaskId = Shader.PropertyToID("_SkinVisibilityMask");

    private sealed class Garment
    {
        public EquippedAvatarItem Item;
        public SegmentedGarmentSO Config;
        public readonly List<SegmentedGarmentRenderer> Renderers = new();
        /// <summary>Occlusion masks + anchors (null = unusable: the garment hides nothing and is never hidden).</summary>
        public GarmentOcclusionData Occlusion;
        public bool[][] Hidden; // per renderer, per vertex (scratch, reused)
    }

    private readonly List<Garment> _garments = new();
    private readonly List<Renderer> _bodyRenderers = new();
    private readonly MaterialPropertyBlock _block = new();
    private string _maskKey;      // key of the mask currently held (null when none)
    private string _requestedKey; // garment set of the last rebuild, even when it produced no mask
    private Texture2D _mask;
    private bool _maskBound;

    // Rebuild scratch, reused so an equipment change allocates (almost) nothing once warmed up.
    private readonly List<int> _indices = new();
    private readonly List<GarmentOcclusionData> _skinMaps = new();
    private readonly List<Garment> _outers = new();
    private readonly System.Text.StringBuilder _key = new();

    /// <summary>The composed skin mask currently bound to the body (null = whole body visible).</summary>
    public Texture2D BodyMask => _mask;
    public int SegmentedGarmentCount => _garments.Count;

    /// <summary>Body renderers that receive the skin mask.</summary>
    public void SetBody(IEnumerable<Renderer> bodyRenderers)
    {
        _bodyRenderers.Clear();
        foreach (var r in bodyRenderers) if (r != null) _bodyRenderers.Add(r);
    }

    /// <summary>Registers one equipped garment. `runtimeMeshes[i]`/`sourceMeshes[i]` belong to
    /// part.renderers[i]: the avatar's own clone (already bound to the skeleton) and the imported asset.</summary>
    public void AddGarment(EquippedAvatarItem item, SegmentedGarmentSO config, SegmentedGarmentPart part,
                           IReadOnlyList<Mesh> sourceMeshes, IReadOnlyList<Mesh> runtimeMeshes)
    {
        var garment = new Garment { Item = item, Config = config };
        garment.Occlusion = GarmentOcclusionData.Acquire(part.occlusion);
        garment.Hidden = new bool[part.renderers.Count][];
        for (int i = 0; garment.Occlusion != null && i < part.renderers.Count; i++)
        {
            if (garment.Occlusion.VertexCount(i) == runtimeMeshes[i].vertexCount) continue;
            GarmentDiagnostics.Error($"'{config.garmentId}' occlusion data has {garment.Occlusion.VertexCount(i)} anchors for renderer " +
                                     $"'{part.renderers[i].renderer.name}' ({runtimeMeshes[i].vertexCount} vertices) - re-import it; it hides nothing.");
            garment.Occlusion.Release();
            garment.Occlusion = null;
        }
        for (int i = 0; i < part.renderers.Count; i++)
        {
            var topology = SegmentedGarmentTopology.Acquire(sourceMeshes[i]);
            garment.Renderers.Add(new SegmentedGarmentRenderer(part.renderers[i].renderer, runtimeMeshes[i], topology, part.finalMaterial));
        }
        _garments.Add(garment);
    }

    /// <summary>Forgets `item` (its renderers/meshes are destroyed by the caller). Call Rebuild afterwards.</summary>
    public bool RemoveGarment(EquippedAvatarItem item)
    {
        for (int i = 0; i < _garments.Count; i++)
        {
            if (_garments[i].Item != item) continue;
            foreach (var r in _garments[i].Renderers) r.Topology.Release();
            _garments[i].Occlusion?.Release();
            _garments.RemoveAt(i);
            return true;
        }
        return false;
    }

    public bool Contains(EquippedAvatarItem item)
    {
        foreach (var g in _garments) if (g.Item == item) return true;
        return false;
    }

    /// <summary>Recomputes every garment's visible triangles and the body mask from the CURRENT equipment.
    /// Garments whose hiding garments did not change keep their meshes untouched.</summary>
    public void Rebuild()
    {
        foreach (var g in _garments) ApplyOcclusion(g);
        _skinMaps.Clear();
        foreach (var g in _garments) if (g.Occlusion != null) _skinMaps.Add(g.Occlusion);
        UpdateBodyMask();
    }

    /// <summary>Hides the vertices of `g` lying under the garments above it that hide its category.</summary>
    private void ApplyOcclusion(Garment g)
    {
        _outers.Clear();
        _key.Clear();
        if (g.Occlusion != null)
            foreach (var o in _garments)
            {
                if (o == g || o.Occlusion == null || o.Config == null || g.Config == null) continue;
                if (o.Config.layer <= g.Config.layer || !Hides(o.Config, g.Config.category)) continue;
                if (o.Occlusion.Resolution != g.Occlusion.Resolution)
                {
                    GarmentDiagnostics.Warn($"'{o.Config.garmentId}' masks are {o.Occlusion.Resolution}, '{g.Config.garmentId}' anchors {g.Occlusion.Resolution} - it does not hide it.");
                    continue;
                }
                _outers.Add(o);
                _key.Append(o.Occlusion.GetHashCode()).Append(':').Append(o.Config.occlusionGap).Append('|');
            }
        string key = _key.ToString();
        for (int r = 0; r < g.Renderers.Count; r++)
        {
            if (_outers.Count == 0) { g.Renderers[r].ApplyHiddenVertices(null, key, _indices); continue; }
            var texels = g.Occlusion.AnchorTexel[r];
            var codes = g.Occlusion.AnchorCode[r];
            var hidden = g.Hidden[r];
            if (hidden == null || hidden.Length != codes.Length) hidden = g.Hidden[r] = new bool[codes.Length];
            Array.Clear(hidden, 0, hidden.Length);
            foreach (var o in _outers)
            {
                var cover = o.Occlusion.Cover;
                int gap = Mathf.RoundToInt(o.Config.occlusionGap * 1000f);
                for (int v = 0; v < codes.Length; v++)
                {
                    if (hidden[v]) continue;
                    int code = codes[v];
                    if (code == GarmentOcclusionData.FarCode) continue;
                    int c = cover[texels[v]];
                    if (c != 0 && c <= code + gap) hidden[v] = true;
                }
            }
            g.Renderers[r].ApplyHiddenVertices(hidden, key, _indices);
        }
    }

    private static bool Hides(SegmentedGarmentSO outer, string category)
    {
        if (outer.hidesCategories == null || string.IsNullOrEmpty(category)) return false;
        foreach (var c in outer.hidesCategories)
            if (string.Equals(c, category, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The per-renderer runtime state of `item` (empty if not layered) — diagnostics/tests.</summary>
    public IReadOnlyList<SegmentedGarmentRenderer> RenderersOf(EquippedAvatarItem item)
    {
        foreach (var g in _garments) if (g.Item == item) return g.Renderers;
        return Array.Empty<SegmentedGarmentRenderer>();
    }

    private void UpdateBodyMask()
    {
        string key = BodyVisibilityMask.KeyFor(_skinMaps);
        if (key == _requestedKey && _maskBound) return;
        _requestedKey = key;

        var previousKey = _maskKey;
        _mask = BodyVisibilityMask.Acquire(_skinMaps, key);
        _maskKey = _mask != null ? key : null;
        BodyVisibilityMask.Release(previousKey);

        // Read-modify-write: the body's block also carries skin tone / face texture (AvatarFactory).
        var texture = _mask != null ? _mask : Texture2D.blackTexture;
        foreach (var renderer in _bodyRenderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(_block);
            _block.SetTexture(SkinVisibilityMaskId, texture);
            renderer.SetPropertyBlock(_block);
        }
        _maskBound = true;
    }

    /// <summary>Releases this avatar's share of the topology cache, the occlusion data and the composed mask.</summary>
    public void Dispose()
    {
        foreach (var g in _garments)
        {
            foreach (var r in g.Renderers) r.Topology.Release();
            g.Occlusion?.Release();
        }
        _garments.Clear();
        _skinMaps.Clear();
        BodyVisibilityMask.Release(_maskKey);
        _maskKey = null;
        _requestedKey = null;
        _mask = null;
        _maskBound = false;
        _bodyRenderers.Clear();
    }

    // ── Measurements ─────────────────────────────────────────────────────────────

    public struct Stats
    {
        public int Garments, Renderers, EnabledRenderers, Submeshes, VisibleTriangles, SourceTriangles, Vertices;
        public long RuntimeMeshBytes, SourceMeshBytes, TopologyBytes, MaskBytes;
        /// <summary>Draw calls per rendered view for the layered garments: one per enabled renderer submesh.</summary>
        public int DrawCalls => Submeshes;
    }

    /// <summary>Counts and memory of the current appearance. Memory figures use Profiler.GetRuntimeMemorySizeLong
    /// (Editor / development builds; 0 in release players). Source mesh bytes count each asset once.</summary>
    public Stats GetStats()
    {
        var stats = new Stats { Garments = _garments.Count };
        var sources = new HashSet<Mesh>();
        foreach (var g in _garments)
        {
            foreach (var r in g.Renderers)
            {
                stats.Renderers++;
                stats.Vertices += r.RuntimeMesh.vertexCount;
                stats.VisibleTriangles += r.VisibleTriangles;
                stats.SourceTriangles += r.TotalTriangles;
                if (r.Renderer.enabled) { stats.EnabledRenderers++; stats.Submeshes += r.RuntimeMesh.subMeshCount; }
                stats.RuntimeMeshBytes += Profiler.GetRuntimeMemorySizeLong(r.RuntimeMesh);
                if (sources.Add(r.Topology.Source))
                {
                    stats.SourceMeshBytes += Profiler.GetRuntimeMemorySizeLong(r.Topology.Source);
                    stats.TopologyBytes += r.Topology.IndexMemoryBytes;
                }
            }
        }
        if (_mask != null) stats.MaskBytes = Profiler.GetRuntimeMemorySizeLong(_mask);
        return stats;
    }
}
