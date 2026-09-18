using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the Versus PREFAB (Assets/_Project/Prefabs/UI/VersusScreen.prefab,
/// built via Tools > MusicGame > Build UI Prefabs). Fully static — VersusScreenController only
/// ever changes the two portraits/names it already finds here, never builds new children.
/// </summary>
public class VersusScreenView : MonoBehaviour
{
    public GameObject root;

    [Header("Player (left)")]
    public Image           playerPortrait;
    public TextMeshProUGUI playerNameText;

    [Header("Center")]
    public TextMeshProUGUI vsText;

    [Header("Opponent (right)")]
    public Image           opponentPortrait;
    public TextMeshProUGUI opponentNameText;
}
