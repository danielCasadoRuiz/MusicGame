using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Views of the Progression screen — every visual lives in Prefabs/UI/Progression/*.prefab;
// ProgressionScreenController only instantiates rows and fills data.
//   ProgressionScreen.prefab   title, summary/requirements/seal lines, scroll list, challenge + back buttons, detail overlay
//   LevelHeaderRow.prefab      collapsible level header with race / fight / total averages
//   ColumnHeaderRow.prefab     table column titles (LocalizedText)
//   MatchRow.prefab            one match: race (song + stars), fight (portrait + rival + stars), total
//   BossHeaderRow / BossStageRow.prefab   final-rival section

/// <summary>Root of ProgressionScreen.prefab.</summary>
public class ProgressionScreenView : MonoBehaviour
{
    public TextMeshProUGUI summary;
    public TextMeshProUGUI requirements;
    public TextMeshProUGUI seal;
    public RectTransform listContent;
    public Button challengeButton;
    public TextMeshProUGUI challengeLabel;
    public Button backButton;
    public RectTransform detailRoot;
    public Button detailBackdrop;
    public EvaluationPanel detailPanel;
}
