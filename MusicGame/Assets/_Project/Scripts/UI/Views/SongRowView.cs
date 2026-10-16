using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Prefabs/UI/Items/SongRow.prefab — one Song Selection catalog row: the row button (its Image is the
/// selection-highlight background, deliberately NOT a theme receiver), the song name, the card's
/// Play/Pause preview button and the COMPLETED badge shown instead of it. SongSelectionController spawns
/// one per catalog entry into SongSelectionView.songListRoot (a VerticalLayoutGroup: the row keeps its own
/// height, the layout stretches its width) and only fills data / toggles state.
/// </summary>
public class SongRowView : MonoBehaviour
{
    public Button button;
    public Image background;
    public TextMeshProUGUI label;
    public Button playPauseButton;
    public TextMeshProUGUI playPauseLabel;
    public TextMeshProUGUI completedBadge;
}
