using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the pause-menu PREFAB (Assets/_Project/Prefabs/UI/PauseMenu.prefab,
/// built once via Tools > MusicGame > Build UI Prefabs). PauseController only READS these
/// references — it never creates or lays out this UI itself. Open the prefab in the Prefab
/// editor to retouch the graphic by hand.
/// </summary>
public class PauseView : MonoBehaviour
{
    [Header("Pause button (shown during live gameplay)")]
    public GameObject pauseButtonRoot;
    public Button     pauseButton;
    public TextMeshProUGUI pauseButtonLabel;

    [Header("Paused overlay")]
    public GameObject overlayRoot;
    public TextMeshProUGUI titleText;
    public Button      resumeButton;
    public TextMeshProUGUI resumeButtonLabel;
    public Button      restartButton;
    public TextMeshProUGUI restartButtonLabel;

    [Header("Camera view toggle (sits just below the Pause button, live gameplay only)")]
    public GameObject cameraToggleButtonRoot;
    public Button      cameraToggleButton;
    public Image       cameraToggleIcon;
    public TextMeshProUGUI cameraToggleLabel;
    [Tooltip("Optional — if left empty the icon Image is simply hidden and only the text switches.")]
    public Sprite      thirdPersonIcon;
    public Sprite      firstPersonIcon;
}
