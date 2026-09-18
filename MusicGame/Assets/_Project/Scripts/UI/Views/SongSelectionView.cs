using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain reference holder for the Song Selection PREFAB (Assets/_Project/Prefabs/UI/
/// SongSelection.prefab, built via Tools > MusicGame > Build UI Prefabs). Only the STATIC chrome
/// (title, streaming row, back/play buttons, status text, and the empty song-list container) is
/// baked into the prefab — the catalog rows themselves stay dynamic runtime population, added into
/// songListRoot by SongSelectionController every time exactly as before (Section 9 of the multi-scene
/// refactor plan explicitly calls this out as fine). SongSelectionController only READS these
/// references when a prefab instance is wired; it never creates or lays out the static chrome itself.
/// </summary>
public class SongSelectionView : MonoBehaviour
{
    [Header("Root")]
    public GameObject   root;
    public TextMeshProUGUI titleText;

    [Header("Song list (rows added dynamically at runtime)")]
    public RectTransform songListRoot;

    [Header("Play Your Song — a standalone button, NOT part of the scrollable list")]
    public Button      playYourSongButton;
    public TextMeshProUGUI playYourSongLabel;

    [Header("Streaming placeholders")]
    public Button      spotifyButton;
    public TextMeshProUGUI spotifyLabel;
    public Button      youtubeMusicButton;
    public TextMeshProUGUI youtubeMusicLabel;
    public Button      amazonMusicButton;
    public TextMeshProUGUI amazonMusicLabel;

    [Header("Bottom bar")]
    public Button      backButton;
    public TextMeshProUGUI backButtonLabel;
    public Button      playButton;
    public TextMeshProUGUI playButtonLabel;
    public TextMeshProUGUI statusText;
}
