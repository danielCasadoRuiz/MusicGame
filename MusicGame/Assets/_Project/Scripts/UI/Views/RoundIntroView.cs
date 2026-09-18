using TMPro;
using UnityEngine;

/// <summary>
/// Plain reference holder for the Round Intro PREFAB (Assets/_Project/Prefabs/UI/RoundIntro.prefab,
/// built via Tools > MusicGame > Build UI Prefabs) — the same big-centered-text shape as
/// CountdownScreenView/FinishBannerView, reused across "ROUND {n}", "3", "2", "1" and "FIGHT!" by
/// RoundIntroController swapping this one text's content.
/// </summary>
public class RoundIntroView : MonoBehaviour
{
    public GameObject      root;
    public TextMeshProUGUI text;
}
