using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A collected bonus in the conversion tray: its icon and how many were collected (counts down as they leave).</summary>
public class ConversionChipView : MonoBehaviour
{
    public Image icon;
    public TextMeshProUGUI countText;
    public CanvasGroup group;
}
