using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the Main Menu PREFAB (Assets/_Project/Prefabs/UI/MainMenu.prefab,
/// built via Tools > MusicGame > Build UI Prefabs). MainMenuController only READS these references
/// and wires interaction — it never creates or lays out this UI itself when a prefab instance is
/// wired. Open the prefab in the Prefab editor to retouch the graphic by hand.
/// </summary>
public class MainMenuView : MonoBehaviour
{
    [Header("Root")]
    public GameObject root;
    public TextMeshProUGUI titleText;

    [Header("Buttons")]
    public Button      playButton;
    public TextMeshProUGUI playButtonLabel;
    public Button      settingsButton;
    public TextMeshProUGUI settingsButtonLabel;
    public Button      quitButton;
    public TextMeshProUGUI quitButtonLabel;

    [Header("Settings panel")]
    public GameObject  settingsPanel;
    public TextMeshProUGUI settingsTitleText;
    public TextMeshProUGUI volumeLabelText;
    public Slider      volumeSlider;
    public Button      closeButton;
    public TextMeshProUGUI closeButtonLabel;
}
