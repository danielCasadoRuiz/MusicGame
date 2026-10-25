using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The full help (bonuses / moves), one screen for both ways in: from a tip card's "More info" and from the pause
/// menu. Own canvas (sorted above every pause menu). Pure reference holder for HelpScreen.prefab; rows are
/// HelpRow.prefab / HelpHeader.prefab instances under `content`.
/// </summary>
public class HelpScreenView : MonoBehaviour
{
    public RectTransform root;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI introText;
    public ScrollRect scroll;
    public RectTransform content;
    public HelpRowView rowPrefab;
    public TextMeshProUGUI headerPrefab;
    public Button closeButton;
    public TextMeshProUGUI closeLabel;
    public TextMeshProUGUI hintText;
}
