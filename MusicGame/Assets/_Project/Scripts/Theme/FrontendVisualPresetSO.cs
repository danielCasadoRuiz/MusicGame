using UnityEngine;

/// <summary>
/// A visual override layer with NO music/event meaning at all — purely "one of several looks the
/// app can randomly open with before any song has been analyzed" (see ThemeManager's Random
/// Frontend Visual behavior). Semantically: "we don't know the music style yet", never a fake
/// MusicStyleId. Shares VisualOverrideLayer's shape with MusicStyleVisualSO/EventThemeSO — no
/// extra identity field needed, ThemeManager just needs to pick ONE at random from
/// FrontendVisualRegistrySO.
///
/// Addressable content, same as MusicStyleVisualSO (see FrontendVisualRegistrySO/ThemeAssetLoader).
/// </summary>
[CreateAssetMenu(fileName = "FrontendVisualPreset", menuName = "MusicGame/Theme/Frontend Visual Preset")]
public class FrontendVisualPresetSO : VisualOverrideLayer
{
    [Tooltip("Debug/editor label only — never used for lookup (presets are chosen at random, not " +
             "by name/id).")]
    public string displayName;
}
