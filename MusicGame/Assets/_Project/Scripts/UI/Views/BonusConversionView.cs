using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runner → fight conversion overlay: the bonus block (title, budget and the tray of collected bonuses with their
/// counts — ONE instance shown in the runner results, carried to the bottom of the fight presentation and converted
/// there) and a layer the icons fly in towards the fight HUD's build panel. Own canvas, sorted at runtime above the
/// runner end screen, the roulette and the Versus card, below the help (the rival's bubble keeps clear of the block).
/// Pure reference holder for BonusConversion.prefab.
/// </summary>
public class BonusConversionView : MonoBehaviour
{
    public RectTransform root;
    public CanvasGroup group;
    [Tooltip("The bonus block that travels: title + budget + tray (bottom-centre pivot).")]
    public RectTransform block;
    public CanvasGroup blockGroup;
    public TextMeshProUGUI titleText;
    public RectTransform tray;
    public ConversionChipView chipPrefab;
    public RectTransform flightLayer;
    public Image flyerPrefab;
    public Button skipButton;
    public TextMeshProUGUI skipLabel;
    public TextMeshProUGUI budgetText;
}
