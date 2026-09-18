using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Same lightweight/local-with-AssetReference pattern as MusicStyleRegistrySO, keyed by a plain
/// string eventId instead of an enum — events (Christmas, a launch promo...) aren't a fixed,
/// versioned vocabulary the way MusicStyleId is, so a central enum would need editing for every
/// new event. No scheduling/"currently active" logic here yet (see EventThemeSO's own doc) — this
/// is just "given an eventId, where's its content".
/// </summary>
[CreateAssetMenu(fileName = "EventThemeRegistry", menuName = "MusicGame/Theme/Event Theme Registry")]
public class EventThemeRegistrySO : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string eventId;
        public string displayName;
        public AssetReferenceT<EventThemeSO> theme;
    }

    public Entry[] entries = Array.Empty<Entry>();

    public bool TryGetThemeReference(string eventId, out AssetReferenceT<EventThemeSO> reference)
    {
        foreach (var e in entries)
        {
            if (e.eventId != eventId) continue;
            reference = e.theme;
            return reference != null && reference.RuntimeKeyIsValid();
        }
        reference = null;
        return false;
    }
}
