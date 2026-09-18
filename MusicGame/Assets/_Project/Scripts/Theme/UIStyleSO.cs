using TMPro;
using UnityEngine;

/// <summary>
/// UI content for one Theme layer (see BaseThemeSO/MusicStyleVisualSO/EventThemeSO — this same
/// class is used as BOTH the always-present BaseTheme category AND an optional per-layer override,
/// since an override is just "another UIStyleSO, or null to fall through to Base").
///
/// DESIGN TOKENS, not GameObject references: every field here is an abstract, reusable visual
/// concept (a color role, a font role, a sprite role) — never something specific like
/// "MainMenuPlayButtonColor" or "ResultsScoreColor". A UI element declares which TOKEN it consumes
/// (see ThemeColorReceiver/ThemeTextReceiver/ThemeImageReceiver's own Token enum, which mirrors
/// these fields 1:1) — the same MainMenu.prefab, TopBar.prefab etc. work for every Music Style
/// without ever being duplicated per style.
///
/// Fonts are TMP_FontAsset — TextMeshPro is this project's one UI text technology (see UIFactory).
/// </summary>
[CreateAssetMenu(fileName = "UIStyle", menuName = "MusicGame/Theme/UI Style")]
public class UIStyleSO : ScriptableObject
{
    [Header("Core")]
    public Color primaryColor   = Color.white;
    public Color secondaryColor = Color.gray;
    public Color accentColor    = Color.yellow;

    [Header("Surfaces")]
    public Color backgroundColor       = Color.black;
    public Color surfaceColor          = new(0.08f, 0.08f, 0.08f);
    public Color surfaceSecondaryColor = new(0.14f, 0.14f, 0.14f);

    [Header("Text")]
    public Color textPrimaryColor   = Color.white;
    public Color textSecondaryColor = new(0.7f, 0.7f, 0.7f);

    [Header("Buttons")]
    public Color buttonPrimaryColor   = new(1f, 1f, 1f, 0.12f);
    public Color buttonSecondaryColor = new(1f, 1f, 1f, 0.06f);

    [Header("Semantic")]
    public Color positiveColor = new(0.3f, 0.85f, 0.3f);
    public Color negativeColor = new(0.9f, 0.3f, 0.25f);

    [Header("Fonts")]
    [Tooltip("Null = fall through to TMP_Settings' own default font asset.")]
    public TMP_FontAsset displayFont;
    public TMP_FontAsset bodyFont;

    [Header("Sprites (optional — null is a completely normal, supported value)")]
    public Sprite backgroundSprite;
    public Sprite patternSprite;
    public Sprite panelSprite;

    [Header("Layout")]
    [Tooltip("Free-form tag a prefab-specific Theme Controller may read to pick an internal layout " +
             "variant (e.g. a TopBarThemeController checking for \"Compact\"/\"Arcade\"). Generic " +
             "receivers ignore this entirely. Empty/unrecognized means \"use the prefab's default " +
             "layout\" — see the UI Variants section of the Theme refactor plan.")]
    public string layoutVariant = "";
}
