using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Prefabs/UI/Collection/RivalCard.prefab — one card: portrait (lit when revealed, black silhouette
/// otherwise), name and footer. Colours are prefab fields.</summary>
public class RivalCardView : MonoBehaviour
{
    public Button button;
    public Image portrait;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI footer;
    public Color revealedColor = Color.white;
    public Color silhouetteColor = new(0f, 0f, 0f, 0.92f);
    public Color emptyColor = new(1f, 1f, 1f, 0.15f);

    public void Set(Sprite sprite, bool revealed, string name, string footerText, bool interactable)
    {
        if (portrait != null)
        {
            portrait.sprite = sprite;
            portrait.color = sprite == null ? emptyColor : revealed ? revealedColor : silhouetteColor;
        }
        if (nameText != null) nameText.text = name;
        if (footer != null) footer.text = footerText;
        if (button != null) button.interactable = interactable;
    }
}
