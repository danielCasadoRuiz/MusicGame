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
    public readonly List<EquippedAvatarItem> EquippedItems = new();
    public AvatarRecipe Recipe;

    private readonly List<AsyncOperationHandle> _addressableHandles = new();
    private readonly List<Mesh> _clonedMeshes = new();
    private bool _disposed;

    /// <summary>AvatarFactory calls this for every Addressables handle it opens while building this
    /// instance (base body prefab, hair prefab, each equipped item prefab) — tracked here so Dispose
    /// releases every single one, never leaking a handle.</summary>
    public void TrackHandle(AsyncOperationHandle handle) => _addressableHandles.Add(handle);

    /// <summary>AvatarFactory calls this for every Mesh it clones off a sharedMesh before mutating
    /// blendshape weights (see AvatarBodyMorphController's own doc on why cloning is mandatory) — a
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
            if (mesh != null) Object.Destroy(mesh);
        _clonedMeshes.Clear();

        EquippedItems.Clear();

        if (Root != null) Object.Destroy(Root.gameObject);
        Root = null;
        VisualRoot = null;
        SkeletonRoot = null;
        Animator = null;
    }
}
