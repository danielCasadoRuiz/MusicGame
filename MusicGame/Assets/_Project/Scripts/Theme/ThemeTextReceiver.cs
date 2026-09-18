using TMPro;
using UnityEngine;

/// <summary>
/// Generic receiver for a TextMeshProUGUI: binds a color token (interpolated) AND a font token
/// (hard-swapped mid-transition — fonts aren't interpolable). Covers the common "this label is
/// TextPrimary in DisplayFont" case with zero controller code.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class ThemeTextReceiver : ThemeElementReceiver
{
    [SerializeField] private UIColorToken colorToken = UIColorToken.TextPrimary;
    [SerializeField] private UIFontToken  fontToken  = UIFontToken.Body;

    private TMP_Text _text;

    private void Awake() => _text = GetComponent<TMP_Text>();

    /// <summary>For runtime AddComponent call sites (see UIFactory-based screens) — Inspector
    /// users on a real prefab just set the [SerializeField]s above directly instead.</summary>
    public void Initialize(UIColorToken color, UIFontToken font)
    {
        colorToken = color;
        fontToken  = font;
    }

    protected override void Apply(UIStyleSO ui)
    {
        _text.color = UIStyleTokens.Resolve(ui, colorToken);
        var font = UIStyleTokens.Resolve(ui, fontToken);
        if (font != null) _text.font = font;
    }

    protected override void ApplyBlend(UIStyleSO from, UIStyleSO to, float t)
    {
        _text.color = Color.Lerp(UIStyleTokens.Resolve(from, colorToken), UIStyleTokens.Resolve(to, colorToken), t);

        if (t >= 0.5f)
        {
            var font = UIStyleTokens.Resolve(to, fontToken);
            if (font != null) _text.font = font;
        }
    }
}
