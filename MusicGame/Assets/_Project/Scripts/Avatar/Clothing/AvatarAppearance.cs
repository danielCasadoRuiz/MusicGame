using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// The equipped appearance of ONE avatar for its segmented garments: which regions each garment shows
/// (garmentCoverage of the garments above it) and which skin the body shows (bodyCoverage of all of them).
/// Owned by AvatarInstance (AvatarInstance.Appearance); AvatarFactory adds/removes garments and calls
/// Rebuild after every equipment change — and only then: the derived meshes and the body mask stay as they
/// are until the equipment changes again.
///
/// Nothing here is shared between avatars except read-only data: the source-mesh topology cache and the
/// composed body masks (identical region sets reuse one texture). Each avatar has its own runtime meshes,
/// renderers, blendshape weights and MaterialPropertyBlock; the shared materials are never modified.
///
/// Unsegmented (legacy) garments never pass through here — see AvatarFactory's legacy section.
/// </summary>
public sealed class AvatarAppearance
{
    public static readonly int SkinVisibilityMaskId = Shader.PropertyToID("_SkinVisibilityMask");

    private sealed class Garment
    {
        public EquippedAvatarItem Item;
        public SegmentedGarmentSO Config;
        public readonly List<SegmentedGarmentRenderer> Renderers = new();
    }

    private readonly List<Garment> _garments = new();
    private readonly List<Renderer> _bodyRenderers = new();
    private readonly MaterialPropertyBlock _block = new();
    private GarmentRegionCatalogSO _catalog;
    private string _maskKey;      // key of the mask currently held (null when none)
    private string _requestedKey; // region set of the last rebuild, even when it produced no mask
    private Texture2D _mask;
    private bool _maskBound;

    // Rebuild scratch, reused so an equipment change allocates (almost) nothing once warmed up.
    private readonly List<SegmentedGarmentSO> _configs = new();
    private readonly List<HashSet<string>> _hidden = new();
    private readonly SortedSet<string> _bodyRegions = new(StringComparer.Ordinal);
    private readonly List<int> _indices = new();

    public GarmentRegionCatalogSO Catalog => _catalog;
    /// <summary>The composed skin mask currently bound to the body (null = whole body visible).</summary>
    public Texture2D BodyMask => _mask;
    public IReadOnlyCollection<string> CoveredBodyRegions => _bodyRegions;
    public int SegmentedGarmentCount => _garments.Count;

    /// <summary>Body renderers that receive the skin mask, and the region vocabulary/masks of that body.</summary>
    public void SetBody(IEnumerable<Renderer> bodyRenderers, GarmentRegionCatalogSO catalog)
    {
        _bodyRenderers.Clear();
        foreach (var r in bodyRenderers) if (r != null) _bodyRenderers.Add(r);
        _catalog = catalog;
    }

    /// <summary>Registers one equipped segmented garment. `runtimeMeshes[i]`/`sourceMeshes[i]` belong to
    /// part.renderers[i]: the avatar's own clone (already bound to the skeleton) and the imported asset.</summary>
    public void AddGarment(EquippedAvatarItem item, SegmentedGarmentSO config, SegmentedGarmentPart part,
                           IReadOnlyList<Mesh> sourceMeshes, IReadOnlyList<Mesh> runtimeMeshes)
    {
        var garment = new Garment { Item = item, Config = config };
        ValidateConfig(config);
        for (int i = 0; i < part.renderers.Count; i++)
        {
            var layout = part.renderers[i];
            var regions = ResolveRegions(config, layout);
            var topology = SegmentedGarmentTopology.Acquire(sourceMeshes[i]);
            garment.Renderers.Add(new SegmentedGarmentRenderer(layout.renderer, runtimeMeshes[i], topology, regions, part.finalMaterial));
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

    /// <summary>Recomputes every garment's visible regions and the body mask from the CURRENT equipment.
    /// Garments whose visible set did not change keep their meshes untouched.</summary>
    public void Rebuild()
    {
        _configs.Clear();
        foreach (var g in _garments) _configs.Add(g.Config);
        while (_hidden.Count < _configs.Count) _hidden.Add(new HashSet<string>(StringComparer.Ordinal));

        GarmentCoverageResolver.ResolveGarmentHiddenRegions(_configs, _hidden);
        for (int g = 0; g < _garments.Count; g++)
            foreach (var renderer in _garments[g].Renderers) renderer.Apply(_hidden[g], _indices);

        GarmentCoverageResolver.CollectBodyCoverage(_configs, _bodyRegions);
        UpdateBodyMask();
    }

    /// <summary>Regions currently hidden on `item` by the garments above it (empty if not segmented).</summary>
    public void GetHiddenRegions(EquippedAvatarItem item, ICollection<string> result)
    {
        result.Clear();
        for (int g = 0; g < _garments.Count && g < _hidden.Count; g++)
            if (_garments[g].Item == item) foreach (var r in _hidden[g]) result.Add(r);
    }

    /// <summary>The per-renderer runtime state of `item` (empty if not segmented) — diagnostics/tests.</summary>
    public IReadOnlyList<SegmentedGarmentRenderer> RenderersOf(EquippedAvatarItem item)
    {
        foreach (var g in _garments) if (g.Item == item) return g.Renderers;
        return Array.Empty<SegmentedGarmentRenderer>();
    }

    private void UpdateBodyMask()
    {
        string key = _bodyRegions.Count == 0 ? null : BodyVisibilityMask.KeyFor(_catalog, _bodyRegions);
        if (key == _requestedKey && _maskBound) return;
        _requestedKey = key;

        if (_bodyRegions.Count > 0 && _catalog == null)
            GarmentDiagnostics.Warn("Segmented garments cover body regions but the base avatar has no garmentRegionCatalog — the body is not masked.");

        var previousKey = _maskKey;
        _mask = key != null ? BodyVisibilityMask.Acquire(_catalog, _bodyRegions, key) : null;
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

    private string[] ResolveRegions(SegmentedGarmentSO config, SegmentedGarmentPart.RendererLayout layout)
    {
        var regions = new string[layout.submeshRegions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            string region = layout.submeshRegions[i];
            if (string.IsNullOrEmpty(region)) continue; // unresolved at import: kept
            if (_catalog != null && !_catalog.IsKnownRegion(region))
            {
                GarmentDiagnostics.Warn($"'{config.garmentId}' renderer '{layout.renderer.name}' submesh {i} uses region '{region}', unknown to catalog '{_catalog.name}' — that geometry is always kept.");
                continue;
            }
            regions[i] = region;
        }
        return regions;
    }

    private void ValidateConfig(SegmentedGarmentSO config)
    {
        if (_catalog == null) return;
        if (!string.IsNullOrEmpty(config.category) && !_catalog.IsKnownCategory(config.category))
            GarmentDiagnostics.Warn($"'{config.garmentId}' has category '{config.category}', unknown to catalog '{_catalog.name}' (rules can still target it by name).");
        foreach (var region in config.bodyCoverage)
            if (!_catalog.IsKnownRegion(region))
                GarmentDiagnostics.Warn($"'{config.garmentId}' bodyCoverage lists unknown region '{region}' — ignored (skin stays visible).");
        foreach (var rule in config.garmentCoverage)
            if (rule?.zones != null)
                foreach (var zone in rule.zones)
                    if (!_catalog.IsKnownRegion(zone))
                        GarmentDiagnostics.Warn($"'{config.garmentId}' garmentCoverage lists unknown region '{zone}' — it can never match any geometry.");
    }

    /// <summary>Releases this avatar's share of the topology cache and the composed mask.</summary>
    public void Dispose()
    {
        foreach (var g in _garments)
            foreach (var r in g.Renderers) r.Topology.Release();
        _garments.Clear();
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
        /// <summary>Draw calls per rendered view for the segmented garments: one per enabled renderer submesh.</summary>
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
