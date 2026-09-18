using TMPro;
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
    public TextMeshProUGUI kickValue;
    public TextMeshProUGUI snareValue;
    public TextMeshProUGUI hiHatValue;
    public TextMeshProUGUI beatValue;
    public TextMeshProUGUI onsetValue;
    public TextMeshProUGUI impactValue;
    public TextMeshProUGUI scoreValue;
    public TextMeshProUGUI totalValue;

    [Header("Counter labels (localized — see Loc)")]
    public TextMeshProUGUI kickLabel;
    public TextMeshProUGUI snareLabel;
    public TextMeshProUGUI hiHatLabel;
    public TextMeshProUGUI beatLabel;
    public TextMeshProUGUI onsetLabel;
    public TextMeshProUGUI impactLabel;
    public TextMeshProUGUI scoreLabel;
    public TextMeshProUGUI totalLabel;

    [Header("Progress")]
    public Image progressFill;

    [Header("Semantic tags strip")]
    public GameObject tagsStrip;
    public TextMeshProUGUI tagStyle;
    public TextMeshProUGUI tagVibe;
    public TextMeshProUGUI tagOther;

    [Header("Song-finished banner (shown from SongFinishedEvent through the farewell stretch)")]
    public TextMeshProUGUI timeUpText;
}
