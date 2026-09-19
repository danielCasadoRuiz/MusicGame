using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the Match Result PREFAB (Assets/_Project/Prefabs/UI/MatchResult.prefab,
/// built via Tools > MusicGame > Build UI Prefabs) — ONE configurable view for BOTH Win and Lose (see
/// MatchResultController's own doc on why this isn't two parallel screens): a big "YOU WIN"/"YOU
/// LOSE" title, rival name, round score, a small match summary, a Win-only level-up line + Continue
/// button, Lose-only Fight Again/Replay Song buttons, and a small Rewarded-Ad confirmation sub-panel.
/// </summary>
public class MatchResultView : MonoBehaviour
{
    public GameObject      root;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI rivalText;
    public TextMeshProUGUI roundsText;
    public TextMeshProUGUI summaryText;

    [Header("Win only")]
    public TextMeshProUGUI levelText;
    public Button          continueButton;
    public TextMeshProUGUI continueButtonLabel;

    [Header("Lose only")]
    public Button          fightAgainButton;
    public TextMeshProUGUI fightAgainButtonLabel;
    public Button          replaySongButton;
    public TextMeshProUGUI replaySongButtonLabel;

    [Header("Both")]
    public Button          mainMenuButton;
    public TextMeshProUGUI mainMenuButtonLabel;

    [Header("Rewarded Ad confirmation (Lose, no lives)")]
    public GameObject      rewardAdModalRoot;
    public TextMeshProUGUI rewardAdTitleText;
    public Button          watchAdButton;
    public TextMeshProUGUI watchAdButtonLabel;
    public Button          cancelButton;
    public TextMeshProUGUI cancelButtonLabel;
}
