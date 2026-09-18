using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the Intro screen PREFAB (Assets/_Project/Prefabs/UI/IntroScreen.prefab,
/// built via Tools > MusicGame > Build UI Prefabs). IntroScreenController only READS these
/// references and drives the fade/skip sequence — it never creates or lays out this UI itself when
/// a prefab instance is wired. Open the prefab in the Prefab editor to retouch the graphic by hand.
/// </summary>
public class IntroScreenView : MonoBehaviour
{
    public GameObject   root;
    public CanvasGroup  canvasGroup;
    public Button       skipButton;
    public TextMeshProUGUI titleText;
}
