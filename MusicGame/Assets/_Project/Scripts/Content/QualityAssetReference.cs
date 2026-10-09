using System.Collections.Generic;
using UnityEngine.AddressableAssets;

/// <summary>
/// One quality variant of a quality-dependent 3D asset: a Unity quality level NAME (exactly as in
/// Project Settings → Quality; the Inspector shows a dropdown of the configured levels) + the
/// Addressable prefab for that level. Only the resolved entry is ever loaded — AssetReference fields
/// never load anything by themselves.
/// </summary>
[System.Serializable]
public class QualityAssetReference
{
    public string qualityName;
    public AssetReferenceGameObject prefab;

    public bool IsAssigned => prefab != null && prefab.RuntimeKeyIsValid();
}

/// <summary>
/// Up to one prefab per Unity quality level (Low / Mid / HighMid / High / Ultra). Resolved through
/// QualityAssetResolver against the ACTIVE QualitySettings level, with the fallback rule: highest
/// available level BELOW the requested one, else the lowest available ABOVE it. Reusable by any
/// quality-dependent 3D asset (wearables, hair, enemy props, instruments, environment pieces…).
/// The base MakeHuman avatar is the one deliberate exception (single shared mesh/skeleton).
/// </summary>
[System.Serializable]
public class QualityAssetCollection
{
    public List<QualityAssetReference> variants = new();

    public bool HasAny
    {
        get
        {
            if (variants == null) return false;
            foreach (var v in variants) if (v != null && v.IsAssigned) return true;
            return false;
        }
    }

    /// <summary>The variant for the active Unity quality level (with fallback), or null.</summary>
    public AssetReferenceGameObject Resolve() => QualityAssetResolver.Resolve(this, out _);
}
