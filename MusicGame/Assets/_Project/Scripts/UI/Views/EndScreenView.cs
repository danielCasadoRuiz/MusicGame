using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the end-of-song results PREFAB (Assets/_Project/Prefabs/UI/EndScreen.prefab,
/// instanced in Runner.unity; structure patched by RunnerScreenPrefabs). GameplayHUD only READS these
/// references and fills them — it never creates or lays out this UI itself.
///
/// ONE panel: title, ONE nested EvaluationPanel (`evaluationPanel`) holding every result line in order —
/// race stars, score, pickups/timing, falls + no-fall bonus, the per-ring-type bars (RunnerPerformanceRow
/// items, UIPrefabLibrarySO.runnerPerformanceRow), the resources for the fight, lives, the match record —
/// and the Restart / Continue buttons. The old separate blocks (rating word + bar, raw score line, session
/// average, the right-hand run-resources box, a second runtime-spawned evaluation panel) were merged into it.
/// </summary>
public class EndScreenView : MonoBehaviour
{
    [Header("Title (localized — see Loc)")]
    public TextMeshProUGUI titleText;

    [Header("The single results list (nested EvaluationPanel prefab)")]
    public EvaluationPanel evaluationPanel;

    [Header("Where the collected-bonus block sits (BonusConversionController docks its ONE block here)")]
    public RectTransform bonusSlot;

    [Header("Buttons")]
    public Button restartButton;
    public TextMeshProUGUI restartButtonLabel;
    public Button continueButton;
    public TextMeshProUGUI continueButtonLabel;
}
