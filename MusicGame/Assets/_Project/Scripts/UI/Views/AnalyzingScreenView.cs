using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the Analyzing screen PREFAB (Assets/_Project/Prefabs/UI/
/// AnalyzingScreen.prefab, built via Tools > MusicGame > Build UI Prefabs). AnalyzingScreenController
/// only READS these references and drives progress/tip rotation — it never creates or lays out this
/// UI itself when a prefab instance is wired.
/// </summary>
public class AnalyzingScreenView : MonoBehaviour
{
    public GameObject root;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI tipText;
    public Image      progressFill;
}
