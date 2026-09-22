using UnityEngine;

/// <summary>
/// One named renderer's base vertex positions for ONE AvatarMeshBasePresetSO — MaleBase and FemaleBase
/// each carry one of these per skinned renderer on the shared baseAvatarPrefab, matched by renderer
/// GameObject NAME (the artist's own naming contract, same "exact name, no alternative mapping"
/// philosophy as MorphChannel — see AvatarBodyMorphController's own doc). normals/tangents are
/// optional: leave them empty when the mesh's own imported normals already read correctly after the
/// vertex swap (task's own explicit "només si realment són necessaris" scope note) — AvatarFactory
/// calls Mesh.RecalculateNormals/RecalculateTangents itself whenever they're omitted.
/// </summary>
[System.Serializable]
public class AvatarMeshBaseVertexData
{
    [Tooltip("Must exactly match the SkinnedMeshRenderer GameObject's own name on baseAvatarPrefab.")]
    public string rendererName;

    public Vector3[] vertices = System.Array.Empty<Vector3>();

    [Tooltip("Optional — leave empty to let AvatarFactory call Mesh.RecalculateNormals() instead.")]
    public Vector3[] normals = System.Array.Empty<Vector3>();

    [Tooltip("Optional — leave empty to let AvatarFactory call Mesh.RecalculateTangents() instead.")]
    public Vector4[] tangents = System.Array.Empty<Vector4>();
}

/// <summary>
/// A base body SHAPE — MaleBase or FemaleBase (task's own explicit "recuperar la filosofia... vertex
/// positions" request): both share the EXACT same baseAvatarPrefab (one skeleton, one topology, one
/// set of bone weights/UVs — see BaseAvatarDefinitionSO's own doc), differing ONLY in where their
/// vertices sit. AvatarFactory applies one of these to the freshly-cloned meshes BEFORE any
/// Slim/Heavy/Muscle blendshape weight is set, since Unity blendshapes are deltas evaluated on top of
/// whatever Mesh.vertices currently holds — swap the base first, then layer blendshapes on top.
///
/// version/metadata exists purely so a future re-export of the same preset (e.g. a topology fix) has
/// somewhere to record "this changed" for debugging — nothing reads it yet.
/// </summary>
[CreateAssetMenu(fileName = "AvatarMeshBasePreset", menuName = "MusicGame/Avatar/Avatar Mesh Base Preset")]
public class AvatarMeshBasePresetSO : ScriptableObject
{
    public string id;
    public int version;

    public AvatarMeshBaseVertexData[] renderers = System.Array.Empty<AvatarMeshBaseVertexData>();

    public AvatarMeshBaseVertexData FindRenderer(string rendererName)
    {
        foreach (var entry in renderers)
            if (entry != null && entry.rendererName == rendererName) return entry;
        return null;
    }
}
