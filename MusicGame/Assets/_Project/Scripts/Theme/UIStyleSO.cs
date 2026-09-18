using UnityEngine;

/// <summary>
/// UI content for one Theme layer (see BaseThemeSO/MusicStyleVisualSO/EventThemeSO — this same
/// class is used as BOTH the always-present BaseTheme category AND an optional per-layer override,
/// since an override is just "another UIStyleSO, or null to fall through to Base").
///
/// `font` is a legacy UnityEngine.Font, matching the project's actual UI system (UIFactory builds
/// plain UnityEngine.UI.Text — TextMeshPro isn't installed here, so this deliberately doesn't
/// introduce that dependency just to match a hypothetical).
///
/// No wiring to actual menus/screens yet (see the "UI Theme" phase of the app-flow/Theme refactor
/// — that's later); this is purely the DATA a screen will eventually read.
/// </summary>
[CreateAssetMenu(fileName = "UIStyle", menuName = "MusicGame/Theme/UI Style")]
public class UIStyleSO : ScriptableObject
{
    [Header("Colors")]
    public Color primaryColor    = Color.white;
    public Color secondaryColor  = Color.gray;
    public Color accentColor     = Color.yellow;
    public Color backgroundColor = Color.black;

    [Header("Optional content")]
    public Sprite panelSprite;
    public Sprite buttonSprite;
    public Font   font;
}
