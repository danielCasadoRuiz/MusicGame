using TMPro;
using UnityEngine;

/// <summary>
/// Plain reference holder for the Round End PREFAB (Assets/_Project/Prefabs/UI/RoundEnd.prefab,
/// built via Tools > MusicGame > Build UI Prefabs) — same big-centered-text shape as RoundIntroView,
/// reused across "KO"/"TIME UP" and the round result text by RoundEndController swapping this one
/// text's content.
/// </summary>
public class RoundEndView : MonoBehaviour
{
    public GameObject      root;
    public TextMeshProUGUI text;
}
