using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the live top-bar HUD PREFAB (Assets/_Project/Prefabs/UI/LiveHud.prefab,
/// built once via Tools > MusicGame > Build UI Prefabs). GameplayHUD only READS these references
/// at runtime — it never creates or lays out this UI itself. Open the prefab in the Prefab editor
/// to retouch colors/fonts/spacing by hand; as long as these fields stay wired to the right
/// child objects, GameplayHUD keeps working unchanged.
/// </summary>
public class LiveHudView : MonoBehaviour
{
    [Header("Counters")]
    public Text kickValue;
    public Text snareValue;
    public Text hiHatValue;
    public Text beatValue;
    public Text onsetValue;
    public Text impactValue;
    public Text scoreValue;
    public Text totalValue;

    [Header("Progress")]
    public Image progressFill;

    [Header("Semantic tags strip")]
    public GameObject tagsStrip;
    public Text tagStyle;
    public Text tagVibe;
    public Text tagOther;
}
