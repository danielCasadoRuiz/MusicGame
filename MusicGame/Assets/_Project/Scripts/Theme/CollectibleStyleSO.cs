using System;
using UnityEngine;

/// <summary>
/// Collectible VISUAL content for one Theme layer — never scoring/collision/event logic (that
/// stays entirely in GameplayManager/MusicRunnerCollectiblesConfig). Deliberately a SEPARATE data
/// model from MusicRunnerCollectiblesConfig (which mixes visual + scoring/activation tunables)
/// rather than referencing it directly. GameplayManager's visual resolution (GetOrCreateMaterial/
/// BuildPoolObject) checks HERE FIRST for a matching RingType entry, falling back to whatever
/// MusicRunnerCollectiblesConfig already resolves when a type has no entry (or BaseTheme's own
/// Collectibles category is empty) — same Theme-first-with-fallback pattern as World/Track.
/// </summary>
[CreateAssetMenu(fileName = "CollectibleStyle", menuName = "MusicGame/Theme/Collectible Style")]
public class CollectibleStyleSO : ScriptableObject
{
    [Serializable]
    public struct RingVisual
    {
        public RingType   type;
        [Tooltip("Null = keep whatever MusicRunnerCollectiblesConfig.ResolvePrefab already gives " +
                 "this type (its own prefab, or the built-in cube fallback).")]
        public GameObject prefab;
        public Color      color;
        [Tooltip("Separate from intensity on purpose (same reasoning as " +
                 "MusicRunnerCollectiblesConfig.ringEmissionEnabled) — a 0-intensity override " +
                 "should still mean 'explicitly enabled, just dim', not 'disabled'.")]
        public bool       emissionEnabled;
        public float      emissionIntensity;
    }

    public RingVisual[] rings = Array.Empty<RingVisual>();
}
