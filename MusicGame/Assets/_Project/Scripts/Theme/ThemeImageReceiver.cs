using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generic receiver for an Image: an optional color token (interpolated) and/or an optional sprite
/// token ("assets no interpolables: fade out → swap → fade in" — Section 9/13 of the Theme/UI
/// refactor plan). Either can be toggled off independently in the Inspector, so this one component
/// covers "just tint this panel", "just swap this pattern", or both together.
/// </summary>
[RequireComponent(typeof(Image))]
public class ThemeImageReceiver : ThemeElementReceiver
{
    [Header("Color (optional)")]
    [SerializeField] private bool useColorToken = true;
    [SerializeField] private UIColorToken colorToken = UIColorToken.Surface;

    [Header("Sprite (optional)")]
    [SerializeField] private bool useSpriteToken;
    [SerializeField] private UISpriteToken spriteToken = UISpriteToken.Panel;

    private Image _image;

    private void Awake() => _image = GetComponent<Image>();

    /// <summary>For runtime AddComponent call sites (see UIFactory-based screens) — Inspector
    /// users on a real prefab just set the [SerializeField]s above directly instead.</summary>
    public void Initialize(UIColorToken? color, UISpriteToken? sprite)
    {
        useColorToken = color.HasValue;
        if (color.HasValue) colorToken = color.Value;
        useSpriteToken = sprite.HasValue;
        if (sprite.HasValue) spriteToken = sprite.Value;
    }

    protected override void Apply(UIStyleSO ui)
    {
        if (useColorToken) _image.color = UIStyleTokens.Resolve(ui, colorToken);
        if (useSpriteToken) _image.sprite = UIStyleTokens.Resolve(ui, spriteToken);
    }

    protected override void ApplyBlend(UIStyleSO from, UIStyleSO to, float t)
    {
        Color baseColor = useColorToken
            ? Color.Lerp(UIStyleTokens.Resolve(from, colorToken), UIStyleTokens.Resolve(to, colorToken), t)
            : _image.color;

        if (useSpriteToken)
        {
            var spriteFrom = UIStyleTokens.Resolve(from, spriteToken);
            var spriteTo   = UIStyleTokens.Resolve(to, spriteToken);
            if (spriteFrom != spriteTo)
            {
                // Fade out -> swap -> fade in, layered as an extra alpha multiplier on top of
                // whatever the color token is already doing this frame (if anything).
                float fadeAlpha = t < 0.5f ? 1f - (t / 0.5f) : (t - 0.5f) / 0.5f;
                if (t >= 0.5f) _image.sprite = spriteTo;
                baseColor.a *= fadeAlpha;
            }
        }

        _image.color = baseColor;
    }
}
