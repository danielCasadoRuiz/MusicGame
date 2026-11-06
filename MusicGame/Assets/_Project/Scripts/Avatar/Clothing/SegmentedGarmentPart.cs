using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Layering metadata carried by ONE quality variant of a garment prefab (each Low/Mid/High/… prefab has its own
/// mesh and possibly its own final material) — written by GarmentMaskImporter, read by AvatarFactory when the
/// variant is equipped. The variant is the rigged model as it is plus `occlusion` (occlusion masks + per-vertex
/// anchors, GarmentOcclusionData); AvatarAppearance hides its triangles per vertex under the garments above it.
///
/// A garment takes this path only if the component exists, its schemaVersion is supported and its layouts
/// validate against the renderers' meshes (TryValidate). Anything else goes through the legacy equipment path.
/// </summary>
public class SegmentedGarmentPart : MonoBehaviour
{
    /// <summary>4 = occlusion masks. (3 was the retired cuts format: one submesh per body region.)</summary>
    public const int SupportedSchemaVersion = 4;

    [Serializable]
    public class RendererLayout
    {
        public SkinnedMeshRenderer renderer;
        [Tooltip("Object name in the source model — diagnostics only.")]
        public string sourceObject;
        [Tooltip("Vertex count of the imported mesh, checked again at runtime (it must match the occlusion anchors).")]
        public int vertexCount;
    }

    [Tooltip("Format of this metadata (SupportedSchemaVersion).")]
    public int schemaVersion = SupportedSchemaVersion;

    [Tooltip("The garment's single rendered material.")]
    public Material finalMaterial;

    [Tooltip("Occlusion masks + per-vertex anchors of this variant (GarmentMaskImporter).")]
    public TextAsset occlusion;

    public List<RendererLayout> renderers = new();

    /// <summary>True when this variant can take the layered path: supported schema, occlusion data, a final
    /// material, and every layout matching its renderer's mesh.</summary>
    public bool TryValidate(out string reason)
    {
        if (schemaVersion != SupportedSchemaVersion) { reason = $"schemaVersion {schemaVersion} is not supported (expected {SupportedSchemaVersion}) - re-import it"; return false; }
        if (occlusion == null) { reason = "no occlusion data"; return false; }
        if (finalMaterial == null) { reason = "no final material assigned"; return false; }
        if (renderers == null || renderers.Count == 0) { reason = "no renderer layouts"; return false; }
        foreach (var layout in renderers)
        {
            if (layout?.renderer == null) { reason = "a renderer layout has no SkinnedMeshRenderer"; return false; }
            var mesh = layout.renderer.sharedMesh;
            if (mesh == null) { reason = $"'{layout.renderer.name}' has no mesh"; return false; }
            if (layout.vertexCount != mesh.vertexCount)
            { reason = $"'{layout.renderer.name}': mesh has {mesh.vertexCount} vertices, layout was imported with {layout.vertexCount}"; return false; }
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
