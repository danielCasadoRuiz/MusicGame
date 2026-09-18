using System;
using UnityEngine;

/// <summary>
/// Local, always-available list of songs shipped WITH the project (as opposed to a locally-picked
/// or streamed file) — see Section "Song Selection" of the app-flow/Theme refactor plan, option A.
/// Plain AudioClip references here are fine (unlike Theme content) — predefined songs are core,
/// always-needed content, not swappable/updatable art, so there's no Addressables case for them
/// yet; that can change later without affecting PredefinedSongSource's own shape.
/// </summary>
[CreateAssetMenu(fileName = "PredefinedSongLibrary", menuName = "MusicGame/Song/Predefined Song Library")]
public class PredefinedSongLibrarySO : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string    displayName;
        public AudioClip clip;
    }

    public Entry[] songs = Array.Empty<Entry>();
}
