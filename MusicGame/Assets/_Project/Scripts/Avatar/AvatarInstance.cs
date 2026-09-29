using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>One equipped item's runtime footprint — kept on AvatarInstance so callers (a future
/// inventory/preview UI) can inspect what's actually on an avatar without re-deriving it from
/// Recipe.Items (which only says what was ASKED for, not what actually got equipped after conflict
/// resolution — see AvatarFactory's own doc).</summary>
public class EquippedAvatarItem
{
    public WearableItemSO Definition;
    public GameObject Instance;
    public AvatarVisualPart VisualPart;
}

/// <summary>
/// The runtime HANDLE for one fully-assembled avatar — what AvatarFactory.CreateAsync returns and the
/// ONLY thing Runner/Fight ever touch (task's own explicit "Runner i Fight només han de rebre un
/// avatar ja muntat" requirement: neither reads Recipe/Identity/morphs directly, they read Root/
/// VisualRoot/Animator like any other visual).
///
/// Owns every runtime resource this avatar allocated — cloned meshes (never a shared sharedMesh, see
/// AvatarFactory's own doc) and every Addressables handle (base body + hair + every equipped item) —
/// and Dispose() is the ONE place that releases all of it, so a caller never needs to remember which
/// parts were Addressable vs locally instantiated.
/// </summary>
public class AvatarInstance
{
    public Transform Root;
    public Transform VisualRoot;
    public Transform SkeletonRoot;
    public Animator Animator;

    public AvatarIdentity FinalIdentity;
    public BodyMorphValues BodyMorphValues;
    public AvatarBodyMorphController MorphController;
    public AvatarSkeletonMapper SkeletonMapper;
    public readonly List<EquippedAvatarItem> EquippedItems = new();
    public AvatarRecipe Recipe;

    private readonly List<AsyncOperationHandle> _addressableHandles = new();
    private readonly List<Mesh> _clonedMeshes = new();
    private bool _disposed;

    // Body context captured by AvatarFactory. Empty for an instance whose build failed before the
    // body existed.
    private readonly List<SkinnedMeshRenderer> _bodyRenderers = new();
    private (Transform bone, Vector3 position, Quaternion rotation)[] _restPose = System.Array.Empty<(Transform, Vector3, Quaternion)>();

    /// <summary>AvatarFactory calls this once the base body exists — records which renderers are the
    /// BODY and snapshots the skeleton's rest pose (see ResetToRestPose).</summary>
    public void SetBodyContext(IEnumerable<SkinnedMeshRenderer> bodyRenderers)
    {
        _bodyRenderers.Clear();
        foreach (var smr in bodyRenderers) if (smr != null) _bodyRenderers.Add(smr);

        var bones = SkeletonRoot != null ? SkeletonRoot.GetComponentsInChildren<Transform>(true) : System.Array.Empty<Transform>();
        _restPose = new (Transform, Vector3, Quaternion)[bones.Length];
        for (int i = 0; i < bones.Length; i++) _restPose[i] = (bones[i], bones[i].localPosition, bones[i].localRotation);
    }

    /// <summary>
    /// Runtime body edit — Gender / Weight / Muscle, all continuous — the SAME path AvatarFactory uses at
    /// build time, the debug UI drives, and AvatarBodyTransition animates.
    ///
    /// Only blendshape WEIGHTS change (absolute values for every channel, see
    /// BodyMorphValues.GetMorphWeights). Mesh vertices, the skeleton, bindposes and the Animator/its
    /// Humanoid Avatar are never touched — so a body change can run every frame mid-animation without
    /// resetting the animation state, and no sequence of changes can ever accumulate error.
    ///
    /// Equipped gender-specific Wearable variants are NOT re-resolved when the dominant gender flips
    /// (see BodyMorphValues.BaseType) — rebuild via AvatarFactory for that; warns if it happens.
    /// </summary>
    public void ApplyBody(BodyMorphValues values)
    {
        if (_disposed || Root == null) return;

        if (values.BaseType != BodyMorphValues.BaseType && EquippedItems.Count > 0)
            Debug.LogWarning("[AvatarInstance] Dominant gender changed with wearables equipped — gender-specific variants are not " +
                              "re-resolved; rebuild via AvatarFactory to swap them.");

        BodyMorphValues = values;
        if (FinalIdentity != null) FinalIdentity.Body = values;
        MorphController?.Apply(values);
    }

    /// <summary>Puts every skeleton bone back to the prefab's rest pose (e.g. after stopping an
    /// animation). Purely a pose reset — never part of a body change.</summary>
    public void ResetToRestPose()
    {
        foreach (var (bone, position, rotation) in _restPose)
        {
            if (bone == null) continue;
            bone.localPosition = position;
            bone.localRotation = rotation;
        }
    }

    /// <summary>Read-only view of the body renderers ApplyBody drives — for diagnostics/tests
    /// (e.g. comparing two instances' meshes), never for mutating them directly.</summary>
    public IReadOnlyList<SkinnedMeshRenderer> BodyRenderers => _bodyRenderers;

    /// <summary>AvatarFactory calls this for every Addressables handle it opens while building this
    /// instance (base body prefab, hair prefab, each equipped item prefab) — tracked here so Dispose
    /// releases every single one, never leaking a handle.</summary>
    public void TrackHandle(AsyncOperationHandle handle) => _addressableHandles.Add(handle);

    /// <summary>AvatarFactory calls this for every Mesh it clones off a sharedMesh (see
    /// AvatarFactory.CloneMeshForMutation) — a
    /// cloned Mesh is a plain C# object Unity never garbage-collects on its own, so it must be
    /// explicitly Destroy()'d here.</summary>
    public void TrackClonedMesh(Mesh mesh) => _clonedMeshes.Add(mesh);

    /// <summary>Releases every Addressables handle, destroys every cloned Mesh, then destroys Root —
    /// safe to call more than once (a second call is a no-op). Runner/Fight call this whenever the
    /// avatar is no longer needed (a fighter/scene unloading, a debug preview Release) — never rely on
    /// scene teardown alone to reclaim Addressables memory.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var handle in _addressableHandles)
            if (handle.IsValid()) Addressables.Release(handle);
        _addressableHandles.Clear();

        foreach (var mesh in _clonedMeshes)
            if (mesh != null) DestroyObject(mesh);
        _clonedMeshes.Clear();

        EquippedItems.Clear();
        _bodyRenderers.Clear();
        _restPose = System.Array.Empty<(Transform, Vector3, Quaternion)>();

        if (Root != null) DestroyObject(Root.gameObject);
        Root = null;
        VisualRoot = null;
        SkeletonRoot = null;
        Animator = null;
    }

    /// <summary>Destroy in Play Mode, DestroyImmediate in Edit Mode (editor tooling/tests build avatars
    /// outside Play via AvatarFactory.AssembleBody — Object.Destroy is illegal there).</summary>
    private static void DestroyObject(Object target)
    {
        if (Application.isPlaying) Object.Destroy(target);
        else Object.DestroyImmediate(target);
    }
}
