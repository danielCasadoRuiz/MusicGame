using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Prefabs/UI/Items/OpponentCell.prefab — one Opponent Selection roulette cell: background (the
/// highlight target), portrait and name. A real Button (for its Image + colour-tint machinery) with no
/// onClick wired — display-only during the roulette; no manual picking in V1.</summary>
public class OpponentCellView : MonoBehaviour
{
    public Button button;
    public Image background;
    public Image portrait;
    public TextMeshProUGUI nameText;
    [Tooltip("Portrait tint when the rival has no portrait art yet (a plain dim box).")]
    public Color emptyPortraitColor = new(1f, 1f, 1f, 0.2f);

    public void Set(string displayName, Sprite sprite)
    {
        if (nameText != null) nameText.text = displayName;
        if (portrait == null) return;
        portrait.sprite = sprite;
        portrait.color  = sprite != null ? Color.white : emptyPortraitColor;
    }
}
