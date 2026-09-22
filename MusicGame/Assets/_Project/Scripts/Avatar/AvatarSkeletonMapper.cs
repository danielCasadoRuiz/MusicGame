using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ONE skeleton contract for the whole avatar (task's own explicit "tot utilitza el mateix skeleton
/// contract... no mantinguis skeletons independents animant cada peça" requirement): built once per
/// AvatarInstance from the base body's own AvatarVisualPart.rootBone, then reused to remap every
/// equipped item's SkinnedMeshRenderer.bones[] onto the SAME hierarchy — so the whole avatar animates
/// as one skeleton, never several independently-animating ones.
///
/// Matching is BY BONE NAME, not by hierarchy position/index — the standard "shared humanoid rig"
/// convention: an item's own authoring skeleton can have a different parent chain/order as long as
/// each bone Transform it actually uses shares its name with the real skeleton's own bone. This is a
/// name-cache lookup, never a Physics/scene search, so remapping many items stays cheap.
/// </summary>
public class AvatarSkeletonMapper
{
    private readonly Dictionary<string, Transform> _bonesByName = new();
    private readonly Transform _root;

    public AvatarSkeletonMapper(Transform skeletonRoot)
    {
        _root = skeletonRoot;
        if (skeletonRoot == null) return;

        foreach (var t in skeletonRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!_bonesByName.ContainsKey(t.name))
            {
                _bonesByName[t.name] = t;
            }
            else
            {
                Debug.LogWarning($"[AvatarSkeletonMapper] Duplicate bone name '{t.name}' under skeleton root " +
                                  $"'{skeletonRoot.name}' — the FIRST one found wins; every equipped item's own " +
                                  "matching bone will remap to that one. Rename one of them if they're meant to be distinct.");
            }
        }
    }

    /// <summary>Looks up one bone by name — used for hair/rigid-attachment sockets (see
    /// HairItemSO.attachmentBoneName's own doc), not just full SkinnedMeshRenderer remaps.</summary>
    public Transform FindBone(string boneName)
    {
        if (string.IsNullOrEmpty(boneName)) return null;
        return _bonesByName.TryGetValue(boneName, out var bone) ? bone : null;
    }

    /// <summary>Rewrites `renderer.bones` (and `renderer.rootBone`) to point into THIS skeleton,
    /// matching each original bone/rootBone by name. A bone the real skeleton doesn't have logs a
    /// clear warning naming both the item and the missing bone (task's own explicit "warning/error
    /// clar amb item i bone" requirement) and is left pointing at its ORIGINAL (item-authored) bone —
    /// tolerant degradation over a crash, at the cost of that one bone not actually following the
    /// shared skeleton.</summary>
    public void Remap(SkinnedMeshRenderer renderer, string itemLabel)
    {
        if (renderer == null || _root == null) return;

        var originalBones = renderer.bones;
        var remapped = new Transform[originalBones.Length];
        int missing = 0;

        for (int i = 0; i < originalBones.Length; i++)
        {
            var original = originalBones[i];
            if (original == null) { remapped[i] = null; continue; }

            if (_bonesByName.TryGetValue(original.name, out var match))
            {
                remapped[i] = match;
            }
            else
            {
                remapped[i] = original;
                missing++;
                Debug.LogWarning($"[AvatarSkeletonMapper] '{itemLabel}' references bone '{original.name}' " +
                                  $"which the avatar's real skeleton (root '{_root.name}') doesn't have — " +
                                  "that bone will not follow the shared skeleton for this item.");
            }
        }

        renderer.bones = remapped;

        if (renderer.rootBone != null && _bonesByName.TryGetValue(renderer.rootBone.name, out var rootMatch))
            renderer.rootBone = rootMatch;
        else if (renderer.rootBone != null)
            Debug.LogWarning($"[AvatarSkeletonMapper] '{itemLabel}' rootBone '{renderer.rootBone.name}' " +
                              $"not found on the avatar's real skeleton (root '{_root.name}').");

        if (missing > 0)
            Debug.LogWarning($"[AvatarSkeletonMapper] '{itemLabel}' had {missing} unmatched bone(s) out of {originalBones.Length}.");
    }
}
