using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the Match Result PREFAB (Assets/_Project/Prefabs/UI/MatchResult.prefab,
/// built via Tools > MusicGame > Build UI Prefabs) — a big centered "YOU WIN"/"YOU LOSE" text plus a
/// single exit button. Deliberately minimal/temporary (see MatchResultController's own doc on why no
/// Continue/Replay/Level-Up UI exists yet).
/// </summary>
public class MatchResultView : MonoBehaviour
{
    public GameObject      root;
    public TextMeshProUGUI text;
    public Button          mainMenuButton;
    public TextMeshProUGUI mainMenuButtonLabel;
}
