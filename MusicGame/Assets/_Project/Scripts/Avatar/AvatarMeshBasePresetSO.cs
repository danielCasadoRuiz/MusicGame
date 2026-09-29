using UnityEngine;

/// <summary>
/// One named renderer's vertex data for ONE AvatarMeshBasePresetSO — matched by renderer GameObject
/// NAME on the shared baseAvatarPrefab (same "exact name, no alternative mapping" philosophy as
/// MorphChannel). normals/tangents are optional.
/// </summary>
[System.Serializable]
public class AvatarMeshBaseVertexData
{
    [Tooltip("Must exactly match the SkinnedMeshRenderer GameObject's own name on baseAvatarPrefab.")]
    public string rendererName;

    public Vector3[] vertices = System.Array.Empty<Vector3>();
    public Vector3[] normals = System.Array.Empty<Vector3>();
    public Vector4[] tangents = System.Array.Empty<Vector4>();
}

/// <summary>
/// The baked reference geometry of one Gender ENDPOINT — MaleBase (Gender 0) or FemaleBase (Gender 1)
/// — for the shared baseAvatarPrefab: same topology, same skeleton, same bindposes, same canonical
/// height; only vertex positions (and their normals/tangents) differ.
///
/// Geometry only — there is deliberately NO skeleton data here any more: the avatar has one canonical
/// skeleton for every body (see BaseAvatarDefinitionSO's own doc). Runtime never writes a preset onto a
/// mesh; the body mesh's rest vertices equal MaleBase and its Gender blendshape reaches FemaleBase.
/// These presets are the ground truth the validator/tests compare against, and what future garment
/// fitting bakes against.
///
/// version is bumped on every rebake, purely for debugging "did this change".
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
