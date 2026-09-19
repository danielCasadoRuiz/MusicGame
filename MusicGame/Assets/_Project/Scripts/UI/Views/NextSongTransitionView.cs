using TMPro;
using UnityEngine;

/// <summary>
/// Plain reference holder for the Next Song Transition PREFAB (Assets/_Project/Prefabs/UI/
/// NextSongTransition.prefab, built via Tools > MusicGame > Build UI Prefabs) — the single short
/// "LEVEL {n} / NEXT SONG / {name}" cartela shown after Continue on a won match (see
/// NextSongTransitionController's own doc). No buttons — purely a timed, fading transition.
/// </summary>
public class NextSongTransitionView : MonoBehaviour
{
    public GameObject      root;
    public CanvasGroup     canvasGroup;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI headerText;
    public TextMeshProUGUI songNameText;
}
