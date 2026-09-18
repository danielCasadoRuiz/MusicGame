using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the end-of-song results PREFAB (Assets/_Project/Prefabs/UI/EndScreen.prefab,
/// built once via Tools > MusicGame > Build UI Prefabs). GameplayHUD only READS these references
/// and populates the text/fill values — it never creates or lays out this UI itself. Open the
/// prefab in the Prefab editor to retouch the graphic by hand; as long as these fields stay wired
/// to the right child objects, GameplayHUD keeps working unchanged.
///
/// performanceRowsContainer is the one part still populated dynamically (RESULTS rows depend on
/// which ring types this particular song's timeline actually generated) — GameplayHUD instantiates
/// a row per type into this container each time the results screen is shown.
/// </summary>
public class EndScreenView : MonoBehaviour
{
    [Header("Title (localized — see Loc)")]
    public TextMeshProUGUI titleText;

    [Header("Rating")]
    public TextMeshProUGUI ratingLabel;
    public Image ratingBarFill;
    public TextMeshProUGUI scoreSummary;

    [Header("Per-type performance (populated dynamically)")]
    public RectTransform performanceRowsContainer;

    [Header("Falls / bonus / session")]
    public TextMeshProUGUI fallsText;
    public TextMeshProUGUI noFallBonusText;
    public TextMeshProUGUI sessionText;

    [Header("Buttons")]
    public Button restartButton;
    public TextMeshProUGUI restartButtonLabel;
    public Button continueButton;
    public TextMeshProUGUI continueButtonLabel;
}
