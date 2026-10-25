using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The player's fight preparation, INSIDE the top bar on the player's side (between the health bar and the timer):
/// the five build stats (power bars) and, apart, the run's consumable charges (Special, x3, x4, extra lives). The
/// conversion animation's destinations. Rows are in BonusHelpCatalog.BuildStats order; cells in `resources` order.
/// FightController fills it every frame from the real wallet (no counter of its own) and scales it to fit.
/// </summary>
public class FightBuildPanelView : MonoBehaviour
{
    [System.Serializable]
    public class StatRow
    {
        public FightStatId stat;
        public RectTransform root;
        public Image icon;
        public TextMeshProUGUI label;
        public Image fill;
        public TextMeshProUGUI value;
    }

    [System.Serializable]
    public class ResourceCell
    {
        public BonusKind kind;
        public RectTransform root;
        public Image icon;
        public TextMeshProUGUI value;
    }

    public RectTransform root;
    public StatRow[] stats = System.Array.Empty<StatRow>();
    public ResourceCell[] resources = System.Array.Empty<ResourceCell>();
}
