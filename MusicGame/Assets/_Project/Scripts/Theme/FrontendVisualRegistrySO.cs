using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Same lightweight/local-with-AssetReference pattern as MusicStyleRegistrySO/EventThemeRegistrySO
/// — just a flat list, since ThemeManager picks ONE entry at random rather than looking one up by
/// key (see FrontendVisualPresetSO's own doc).
/// </summary>
[CreateAssetMenu(fileName = "FrontendVisualRegistry", menuName = "MusicGame/Theme/Frontend Visual Registry")]
public class FrontendVisualRegistrySO : ScriptableObject
{
    public AssetReferenceT<FrontendVisualPresetSO>[] presets = Array.Empty<AssetReferenceT<FrontendVisualPresetSO>>();

    public bool TryGetRandom(out AssetReferenceT<FrontendVisualPresetSO> reference)
    {
        if (presets == null || presets.Length == 0) { reference = null; return false; }
        reference = presets[UnityEngine.Random.Range(0, presets.Length)];
        return reference != null && reference.RuntimeKeyIsValid();
    }
}
