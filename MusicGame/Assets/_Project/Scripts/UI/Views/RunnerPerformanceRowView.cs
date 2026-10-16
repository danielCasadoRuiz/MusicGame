using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Prefabs/UI/Items/RunnerPerformanceRow.prefab — one per-ring-type row of the Runner end screen
/// (an item of EndScreenView.evaluationPanel, the single results list): coloured label, collection-rate bar,
/// "NN%  a/b" info. GameplayHUD appends one per type the song generated (EvaluationPanel.AddItem; its
/// LayoutElement gives the height) and fills it.</summary>
public class RunnerPerformanceRowView : MonoBehaviour
{
    public TextMeshProUGUI label;
    public Image barFill;
    public TextMeshProUGUI info;
    [Tooltip("The bar fill uses the ring colour multiplied by this (alpha included, as before).")]
    public float barColorScale = 0.8f;

    public void Set(string labelText, Color ringColor, float rate, string infoText)
    {
        if (label != null) { label.text = labelText; label.color = ringColor; }
        if (barFill != null)
        {
            UIFactory.EnsureFillSprite(barFill);
            barFill.color = ringColor * barColorScale;
            barFill.fillAmount = Mathf.Clamp01(rate);
        }
        if (info != null) info.text = infoText;
    }
}
