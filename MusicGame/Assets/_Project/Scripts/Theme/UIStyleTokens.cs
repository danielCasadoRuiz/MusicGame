using TMPro;
using UnityEngine;

/// <summary>Abstract color roles — mirrors UIStyleSO's color fields 1:1. A UI element declares
/// WHICH role it plays (via ThemeColorReceiver/ThemeTextReceiver/ThemeImageReceiver), never a
/// concrete color or a prefab-specific name like "MainMenuPlayButtonColor".</summary>
public enum UIColorToken
{
    Primary, Secondary, Accent,
    Background, Surface, SurfaceSecondary,
    TextPrimary, TextSecondary,
    ButtonPrimary, ButtonSecondary,
    Positive, Negative,
}

/// <summary>Abstract sprite roles — mirrors UIStyleSO's sprite fields 1:1.</summary>
public enum UISpriteToken { Panel, Background, Pattern }

/// <summary>Abstract font roles — mirrors UIStyleSO's font fields 1:1.</summary>
public enum UIFontToken { Display, Body }

/// <summary>
/// Resolves a UIColorToken/UISpriteToken/UIFontToken against a real UIStyleSO — the single place
/// that maps the abstract token vocabulary to UIStyleSO's actual fields, so every generic receiver
/// (ThemeColorReceiver/ThemeTextReceiver/ThemeImageReceiver) shares the exact same mapping instead
/// of each re-implementing its own switch statement.
/// </summary>
public static class UIStyleTokens
{
    public static Color Resolve(UIStyleSO style, UIColorToken token) => token switch
    {
        UIColorToken.Primary          => style.primaryColor,
        UIColorToken.Secondary        => style.secondaryColor,
        UIColorToken.Accent           => style.accentColor,
        UIColorToken.Background       => style.backgroundColor,
        UIColorToken.Surface          => style.surfaceColor,
        UIColorToken.SurfaceSecondary => style.surfaceSecondaryColor,
        UIColorToken.TextPrimary      => style.textPrimaryColor,
        UIColorToken.TextSecondary    => style.textSecondaryColor,
        UIColorToken.ButtonPrimary    => style.buttonPrimaryColor,
        UIColorToken.ButtonSecondary  => style.buttonSecondaryColor,
        UIColorToken.Positive         => style.positiveColor,
        UIColorToken.Negative         => style.negativeColor,
        _                             => Color.white,
    };

    public static Sprite Resolve(UIStyleSO style, UISpriteToken token) => token switch
    {
        UISpriteToken.Panel      => style.panelSprite,
        UISpriteToken.Background => style.backgroundSprite,
        UISpriteToken.Pattern    => style.patternSprite,
        _                        => null,
    };

    public static TMP_FontAsset Resolve(UIStyleSO style, UIFontToken token) => token switch
    {
        UIFontToken.Display => style.displayFont,
        UIFontToken.Body    => style.bodyFont,
        _                   => null,
    };
}
