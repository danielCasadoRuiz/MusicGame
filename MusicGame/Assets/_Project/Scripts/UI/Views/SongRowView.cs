using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Prefabs/UI/Items/SongRow.prefab — one Song Selection catalog row: the row button (its Image is the
/// selection-highlight background, deliberately NOT a theme receiver), the song name, the "played" check and the
/// card's Play/Pause preview button, each in its own column (name | check | PREVIEW). SongSelectionController
/// spawns one per catalog entry into SongSelectionView.songListRoot (a VerticalLayoutGroup: the row keeps its own
/// height, the layout stretches its width) and only fills data / toggles state.
/// The check is purely informative (all-time played history, never availability): no Selectable, raycastTarget off.
/// </summary>
public class SongRowView : MonoBehaviour
{
    public Button button;
    public Image background;
    public TextMeshProUGUI label;
    public Button playPauseButton;
    public TextMeshProUGUI playPauseLabel;
    [Tooltip("Played-at-least-once tick (placeholder sprite, swap freely). Not interactive.")]
    public Image playedCheck;
    public Color playedOnColor = new(0.45f, 0.85f, 0.5f, 1f);
    public Color playedOffColor = new(1f, 1f, 1f, 0.15f);

    /// <summary>What the check currently shows (the all-time played history).</summary>
    public bool Played { get; private set; }

    public void SetPlayed(bool played)
    {
        Played = played;
        if (playedCheck != null) playedCheck.color = played ? playedOnColor : playedOffColor;
    }
}
