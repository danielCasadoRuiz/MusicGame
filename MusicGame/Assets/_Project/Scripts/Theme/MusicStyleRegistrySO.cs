using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Lightweight, ALWAYS-LOCAL lookup table from MusicStyleId to its (Addressable) visual content —
/// deliberately holds an AssetReference per style, never a strong MusicStyleVisualSO reference.
/// A strong reference would force Unity to keep every style's entire content (World/Track/Player/
/// Collectibles/VFX assets) loaded just because this registry asset exists, defeating the whole
/// point of Addressables being load-on-demand.
/// </summary>
[CreateAssetMenu(fileName = "MusicStyleRegistry", menuName = "MusicGame/Theme/Music Style Registry")]
public class MusicStyleRegistrySO : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public MusicStyleId style;
        public string       displayName;
        public AssetReferenceT<MusicStyleVisualSO> visual;
    }

    public Entry[] entries = Array.Empty<Entry>();

    public bool TryGetVisualReference(MusicStyleId style, out AssetReferenceT<MusicStyleVisualSO> reference)
    {
        foreach (var e in entries)
        {
            if (e.style != style) continue;
            reference = e.visual;
            return reference != null && reference.RuntimeKeyIsValid();
        }
        reference = null;
        return false;
    }
}
