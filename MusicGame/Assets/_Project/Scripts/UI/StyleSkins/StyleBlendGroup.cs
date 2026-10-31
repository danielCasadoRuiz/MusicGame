using UnityEngine;

/// <summary>
/// Sits on a menu root (the Runner pause overlay, the Fight pause panel, the main menu) or on the Runner HUD's
/// pause button and pushes one style + weight to every StyleBlendButton under it. PauseController /
/// FightController call Apply when the menu opens (the Runner from the played fraction of the song, the Fight
/// from who is winning — StyleSkinLibrarySO); the HUD pause every frame (unchanged values are skipped).
/// </summary>
public class StyleBlendGroup : MonoBehaviour
{
    public StyleSkinLibrarySO library;

    private StyleBlendButton[] _buttons;
    private GameMusicStyle _style = GameMusicStyle.Unknown, _base = GameMusicStyle.Unknown;
    private float _weight = -1f;

    /// <param name="baseStyle">Unknown = classic underneath (default); a style = cross-fade from it (StyleBlendButton.SetBlend).</param>
    public void Apply(GameMusicStyle style, float styleWeight, GameMusicStyle baseStyle = GameMusicStyle.Unknown)
    {
        if (_buttons == null || _buttons.Length == 0) _buttons = GetComponentsInChildren<StyleBlendButton>(true);
        else if (style == _style && baseStyle == _base && Mathf.Abs(styleWeight - _weight) < 0.002f) return;
        _style = style;
        _base = baseStyle;
        _weight = styleWeight;
        foreach (var button in _buttons) if (button != null) button.SetBlend(style, styleWeight, baseStyle);
    }
}
