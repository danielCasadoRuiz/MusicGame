using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Binds a clothing SkinnedMeshRenderer to the avatar's skeleton with the proven SkinnedMeshBinder
/// logic: every clothing bone is matched BY NAME to the avatar's real bone, and its bind pose is taken
/// from the avatar BODY mesh (indexed by bone name) instead of the garment's own — so a garment
/// exported against a slightly different rest pose still sits exactly on the body and follows its
/// animation/deformation. The mesh is cloned (never mutate a shared asset), rootBone/bounds/quality
/// come from the body, and the body's current blendshape weights are copied onto matching shapes.
/// Used by AvatarFactory for every wearable, and by the SkinnedMeshBinder test component.
/// </summary>
public static class AvatarClothingBinder
{
    /// <summary>Computes the avatar bones + body bind poses for `clothing`'s bone list. False (with the
    /// reason) if a bone is missing on the avatar or has no bind pose on the body.</summary>
    public static bool TryResolve(SkinnedMeshRenderer clothing, SkinnedMeshRenderer body, System.Func<string, Transform> findAvatarBone,
                                  out Transform[] bones, out Matrix4x4[] bindPoses, out string error)
    {
        bones = null; bindPoses = null; error = null;
        if (clothing == null || clothing.sharedMesh == null) { error = "no clothing mesh"; return false; }
        if (body == null || body.sharedMesh == null) { error = "no body mesh"; return false; }

        var sourceBones = clothing.bones;
        if (sourceBones == null || sourceBones.Length == 0) { error = "clothing has no bones"; return false; }

        // Body bind poses indexed by bone name.
        var bodyBones = body.bones;
        var bodyBindPoses = body.sharedMesh.bindposes;
        var bindPoseByName = new Dictionary<string, Matrix4x4>();
        for (int i = 0; i < bodyBones.Length && i < bodyBindPoses.Length; i++)
            if (bodyBones[i] != null && !bindPoseByName.ContainsKey(bodyBones[i].name))
                bindPoseByName.Add(bodyBones[i].name, bodyBindPoses[i]);

        bones = new Transform[sourceBones.Length];
        bindPoses = new Matrix4x4[sourceBones.Length];
        for (int i = 0; i < sourceBones.Length; i++)
        {
            var sourceBone = sourceBones[i];
            if (sourceBone == null) { error = $"null bone at index {i}"; return false; }
            var target = findAvatarBone(sourceBone.name);
            if (target == null) { error = $"bone '{sourceBone.name}' not found on the avatar"; return false; }
            if (!bindPoseByName.TryGetValue(sourceBone.name, out var bindPose)) { error = $"no body bind pose for bone '{sourceBone.name}'"; return false; }
            bones[i] = target;
            bindPoses[i] = bindPose;
        }
        return true;
    }

    /// <summary>Applies a resolved binding to `target` (which may be the clothing renderer itself):
    /// cloned mesh with the body bind poses, avatar bones, body rootBone/bounds/quality, synced
    /// blendshapes. Returns the cloned mesh (caller tracks/destroys it).</summary>
    public static Mesh Apply(SkinnedMeshRenderer target, Mesh sourceMesh, SkinnedMeshRenderer body, Transform[] bones, Matrix4x4[] bindPoses)
    {
        var runtimeMesh = Object.Instantiate(sourceMesh);
        runtimeMesh.name = sourceMesh.name + "_Runtime";
        runtimeMesh.bindposes = bindPoses; // weights/blendshapes kept, bind poses replaced

        target.sharedMesh = runtimeMesh;
        target.bones = bones;
        target.rootBone = body.rootBone;
        target.quality = body.quality;
        target.updateWhenOffscreen = body.updateWhenOffscreen;
        target.localBounds = body.localBounds;

        SyncBlendShapes(body, target);
        return runtimeMesh;
    }

    /// <summary>Copies the body's current blendshape weights onto same-named shapes of `clothes`.</summary>
    public static void SyncBlendShapes(SkinnedMeshRenderer body, SkinnedMeshRenderer clothes)
    {
        if (body == null || clothes == null || body.sharedMesh == null || clothes.sharedMesh == null) return;
        var bodyMesh = body.sharedMesh;
        var clothesMesh = clothes.sharedMesh;
        for (int i = 0; i < bodyMesh.blendShapeCount; i++)
        {
            int index = clothesMesh.GetBlendShapeIndex(bodyMesh.GetBlendShapeName(i));
            if (index >= 0) clothes.SetBlendShapeWeight(index, body.GetBlendShapeWeight(i));
        }
    }

    /// <summary>The avatar's main body renderer: the body renderer with the most bones (the full skin,
    /// not eyes/teeth/eyebrows).</summary>
    public static SkinnedMeshRenderer MainBody(IReadOnlyList<SkinnedMeshRenderer> bodyRenderers)
    {
        SkinnedMeshRenderer best = null;
        if (bodyRenderers == null) return null;
        foreach (var r in bodyRenderers)
            if (r != null && r.sharedMesh != null && (best == null || r.bones.Length > best.bones.Length ||
                (r.bones.Length == best.bones.Length && r.sharedMesh.vertexCount > best.sharedMesh.vertexCount)))
                best = r;
        return best;
    }
}
