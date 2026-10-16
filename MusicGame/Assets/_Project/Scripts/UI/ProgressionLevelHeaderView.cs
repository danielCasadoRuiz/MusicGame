using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>LevelHeaderRow.prefab — collapsible level header: name + this-lap progress + mastered count,
/// and the race / fight / total averages (label with value and base, plus stars).</summary>
public class ProgressionLevelHeaderView : MonoBehaviour
{
    public Button button;
    public TextMeshProUGUI title;
    public TextMeshProUGUI line;
    public TextMeshProUGUI runnerLabel, combatLabel, totalLabel;
    public StarRatingView runnerStars, combatStars, totalStars;

    public void Set(string titleText, string lineText, string runner, int runnerHalf, string combat, int combatHalf, string total, int totalHalf)
    {
        if (title != null) title.text = titleText;
        if (line != null) line.text = lineText;
        if (runnerLabel != null) runnerLabel.text = runner;
        if (combatLabel != null) combatLabel.text = combat;
        if (totalLabel != null) totalLabel.text = total;
        runnerStars?.SetHalfStars(runnerHalf);
        combatStars?.SetHalfStars(combatHalf);
        totalStars?.SetHalfStars(totalHalf);
    }
}
