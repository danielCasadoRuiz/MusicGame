using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>MatchRow.prefab — one campaign position: number + status, race record (song + stars), fight record
/// (rival portrait + name · version + stars) and total. Purely informative: no Button, opens nothing.
/// Row colours (current / even / odd) are prefab fields.</summary>
public class ProgressionMatchRowView : MonoBehaviour
{
    public Image background;
    public TextMeshProUGUI numberAndStatus;
    public TextMeshProUGUI runner;
    public StarRatingView runnerStars;
    public Image portrait;
    public TextMeshProUGUI combat;
    public StarRatingView combatStars;
    public StarRatingView totalStars;
    public Color currentColor = new(1f, 0.82f, 0.35f, 0.16f);
    public Color evenColor = new(1f, 1f, 1f, 0.07f);
    public Color oddColor = new(1f, 1f, 1f, 0.03f);
    public Color emptyPortraitColor = new(1f, 1f, 1f, 0.08f);

    public void Set(int match, bool current, string numberStatus, string runnerText, int runnerHalf,
                    Sprite rivalPortrait, string combatText, int combatHalf, int totalHalf)
    {
        if (background != null) background.color = current ? currentColor : match % 2 == 0 ? evenColor : oddColor;
        if (numberAndStatus != null) numberAndStatus.text = numberStatus;
        if (runner != null) runner.text = runnerText;
        if (combat != null) combat.text = combatText;
        runnerStars?.SetHalfStars(runnerHalf);
        combatStars?.SetHalfStars(combatHalf);
        totalStars?.SetHalfStars(totalHalf);
        if (portrait != null)
        {
            portrait.sprite = rivalPortrait;
            portrait.color = rivalPortrait != null ? Color.white : emptyPortraitColor;
        }
    }
}
