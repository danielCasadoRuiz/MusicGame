using UnityEngine;

/// <summary>
/// A PARTIAL visual override tied to one MusicStyleId (Funk, Rock, House...) — see
/// VisualOverrideLayer for the shared optional-fields shape (null = fall through to BaseTheme).
/// This is the whole point: a new style can be introduced gradually (e.g. only UI + World colors
/// at first) without duplicating every asset BaseTheme already provides. See ThemeResolver for the
/// actual Base/Style/Event merge.
///
/// Addressable content (see ThemeAssetLoader/MusicStyleRegistrySO) — heavy enough to have its own
/// load/unload lifecycle, unlike the small system configs (AppConfigSO, FlowConfigSO) that stay
/// local.
/// </summary>
[CreateAssetMenu(fileName = "MusicStyleVisual", menuName = "MusicGame/Theme/Music Style Visual")]
public class MusicStyleVisualSO : VisualOverrideLayer
{
    public MusicStyleId style;
}
