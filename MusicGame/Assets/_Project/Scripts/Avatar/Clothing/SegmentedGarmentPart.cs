using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Segmentation metadata carried by ONE quality variant of a segmented garment prefab (each Low/Mid/High/…
/// prefab has its own: different mesh, different submesh order, possibly a different final material) —
/// written by GarmentCutsImporter, read by AvatarFactory when the variant is equipped.
///
/// The imported `_cuts` mesh keeps one TRANSPORT submesh per (region, source material) slot. Unity may reorder
/// material slots and suffix their names, so the importer resolves every submesh to its region from the
/// actual imported material names (never from manifest order) and stores the result here, per submesh index.
/// At runtime the transport materials are never rendered: every submesh maps to finalMaterial.
///
/// A garment counts as segmented only if this component exists, its schemaVersion is supported and its
/// layouts validate against the renderers' meshes (TryValidate) — the submesh count alone means nothing.
/// Anything else goes through the legacy (unsegmented) equipment path unchanged.
/// </summary>
public class SegmentedGarmentPart : MonoBehaviour
{
    public const int SupportedSchemaVersion = 3;

    [Serializable]
    public class RendererLayout
    {
        public SkinnedMeshRenderer renderer;
        [Tooltip("Manifest objects[].object / sourceObject — diagnostics only.")]
        public string cutsObject;
        public string sourceObject;
        [Tooltip("Region per submesh index of the imported mesh. Empty = unresolved: that geometry is always kept.")]
        public string[] submeshRegions = Array.Empty<string>();
        [Tooltip("Imported transport material name per submesh index — diagnostics only.")]
        public string[] transportMaterials = Array.Empty<string>();
        [Tooltip("Vertex count of the imported mesh, checked again at runtime.")]
        public int vertexCount;
    }

    [Tooltip("Cuts package schemaVersion this metadata was produced from.")]
    public int schemaVersion = SupportedSchemaVersion;

    [Tooltip("The garment's single rendered material — every transport slot maps to it.")]
    public Material finalMaterial;

    public List<RendererLayout> renderers = new();

    /// <summary>True when this variant can take the segmented path: recognised schema, a final material, and
    /// every layout matching its renderer's mesh (same submesh/vertex counts, at least one resolved region).</summary>
    public bool TryValidate(out string reason)
    {
        if (schemaVersion != SupportedSchemaVersion) { reason = $"schemaVersion {schemaVersion} is not supported (expected {SupportedSchemaVersion})"; return false; }
        if (finalMaterial == null) { reason = "no final material assigned"; return false; }
        if (renderers == null || renderers.Count == 0) { reason = "no renderer layouts"; return false; }
        foreach (var layout in renderers)
        {
            if (layout?.renderer == null) { reason = "a renderer layout has no SkinnedMeshRenderer"; return false; }
            var mesh = layout.renderer.sharedMesh;
            if (mesh == null) { reason = $"'{layout.renderer.name}' has no mesh"; return false; }
            if (layout.submeshRegions == null || layout.submeshRegions.Length != mesh.subMeshCount)
            { reason = $"'{layout.renderer.name}': layout has {layout.submeshRegions?.Length ?? 0} submesh regions but the mesh has {mesh.subMeshCount} submeshes"; return false; }
            if (layout.vertexCount != mesh.vertexCount)
            { reason = $"'{layout.renderer.name}': mesh has {mesh.vertexCount} vertices, layout was imported with {layout.vertexCount}"; return false; }
            bool anyResolved = false;
            foreach (var region in layout.submeshRegions) anyResolved |= !string.IsNullOrEmpty(region);
            if (!anyResolved) { reason = $"'{layout.renderer.name}': no submesh resolved to a region"; return false; }
        }
        reason = null;
        return true;
    }

    /// <summary>The layout of `renderer`, or null.</summary>
    public RendererLayout LayoutFor(SkinnedMeshRenderer renderer)
    {
        foreach (var layout in renderers) if (layout != null && layout.renderer == renderer) return layout;
        return null;
    }
}
