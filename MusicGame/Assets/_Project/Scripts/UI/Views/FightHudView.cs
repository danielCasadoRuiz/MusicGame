using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the Fight HUD PREFAB (Assets/_Project/Prefabs/UI/FightHud.prefab,
/// built via Tools > MusicGame > Build UI Prefabs). FightController only READS these references and
/// drives the timer/health/pause logic — it never creates or lays out this UI itself when a prefab
/// instance is wired.
/// </summary>
public class FightHudView : MonoBehaviour
{
    [Header("Root")]
    public GameObject root;

    [Header("Top bar")]
    public TextMeshProUGUI playerNameText;
    public Image       playerHealthFill;
    public TextMeshProUGUI playerRoundPipsText;
    public TextMeshProUGUI opponentNameText;
    public Image       opponentHealthFill;
    public TextMeshProUGUI opponentRoundPipsText;
    public TextMeshProUGUI timerText;
    public Button       pauseButton;
    public TextMeshProUGUI pauseButtonLabel;

    [Header("Pause menu")]
    public GameObject   pausePanel;
    public TextMeshProUGUI pauseTitleText;
    public Button       resumeButton;
    public TextMeshProUGUI resumeButtonLabel;
    public Button       mainMenuButton;
    public TextMeshProUGUI mainMenuButtonLabel;

    [Header("Mobile controls — visible ONLY during FightFlowState.Fighting, on real/simulated mobile")]
    public GameObject mobileControlsRoot;
    public RectTransform joystickBackground;
    public RectTransform joystickHandle;
    public GameObject punchButton;
    public TextMeshProUGUI punchButtonLabel;
    public GameObject kickButton;
    public TextMeshProUGUI kickButtonLabel;
}
