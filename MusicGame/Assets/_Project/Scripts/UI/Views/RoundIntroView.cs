using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the Round Intro PREFAB (Assets/_Project/Prefabs/UI/RoundIntro.prefab,
/// built via Tools > MusicGame > Build UI Prefabs) — the same big-centered-text shape as
/// CountdownScreenView/FinishBannerView, reused across "ROUND {n}", "3", "2", "1" and "FIGHT!" by
/// RoundIntroController swapping this one text's content.
///
/// `overlay` is the ONE dark curtain for the whole VS->RoundIntro->Countdown->Fighting reveal (see
/// RoundIntroController's own doc) — sits behind `text` but in front of the arena/FightHud, which are
/// already both visible underneath by the time this sequence starts. There is no separate transition
/// overlay anywhere else (FightController's own former one was removed) — one curtain, one owner.
/// </summary>
public class RoundIntroView : MonoBehaviour
{
    public GameObject      root;
    public Image           overlay;
    public TextMeshProUGUI text;
}
