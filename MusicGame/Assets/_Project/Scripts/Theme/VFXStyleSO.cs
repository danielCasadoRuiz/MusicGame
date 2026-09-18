using UnityEngine;

/// <summary>
/// VFX content for one Theme layer — extension points only for now (see Section "VFX Theme" of
/// the app-flow/Theme refactor: collection effects, trails, beat pulses, combo effects, impacts,
/// screen effects, style transitions, fight effects, etc. are NOT all implemented yet). Two
/// concrete slots are exposed as a starting example — add more only when a real system actually
/// needs one, not speculatively.
/// </summary>
[CreateAssetMenu(fileName = "VFXStyle", menuName = "MusicGame/Theme/VFX Style")]
public class VFXStyleSO : ScriptableObject
{
    public GameObject collectionEffectPrefab;
    public GameObject beatPulseEffectPrefab;
}
