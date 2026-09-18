using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generic receiver: tints ANY Graphic (Image, TextMeshProUGUI, RawImage — anything with a `.color`)
/// to one UIColorToken, with a real lerp transition. Drop this on a child, pick a Token in the
/// Inspector, done — no controller code needed for trivial tinting (Section 2/12 of the Theme/UI
/// refactor plan: generic receivers handle the common case, prefab-specific controllers only exist
/// for real special behavior).
/// </summary>
[RequireComponent(typeof(Graphic))]
public class ThemeColorReceiver : ThemeElementReceiver
{
    [SerializeField] private UIColorToken token = UIColorToken.Primary;

    private Graphic _graphic;

    private void Awake() => _graphic = GetComponent<Graphic>();

    /// <summary>For runtime AddComponent call sites (see UIFactory-based screens) — Inspector
    /// users on a real prefab just set the [SerializeField] above directly instead.</summary>
    public void Initialize(UIColorToken colorToken) => token = colorToken;

    protected override void Apply(UIStyleSO ui) => _graphic.color = UIStyleTokens.Resolve(ui, token);

    protected override void ApplyBlend(UIStyleSO from, UIStyleSO to, float t) =>
        _graphic.color = Color.Lerp(UIStyleTokens.Resolve(from, token), UIStyleTokens.Resolve(to, token), t);
}
