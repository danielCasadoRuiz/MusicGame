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

    [Header("Paused overlay")]
    public GameObject overlayRoot;
    public Button      resumeButton;
    public Button      restartButton;
}
