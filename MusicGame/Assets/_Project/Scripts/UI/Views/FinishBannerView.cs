using TMPro;
using UnityEngine;

/// <summary>
/// Plain reference holder for the Finish banner PREFAB (Assets/_Project/Prefabs/UI/
/// FinishBanner.prefab, built via Tools > MusicGame > Build UI Prefabs). FinishBannerController
/// only READS this reference — it never creates or lays out this UI itself when a prefab
/// instance is wired. Same shape as CountdownScreenView, its closest sibling.
/// </summary>
public class FinishBannerView : MonoBehaviour
{
    public GameObject root;
    public TextMeshProUGUI text;
}
