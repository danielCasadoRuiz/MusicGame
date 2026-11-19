using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JukeboxView : MonoBehaviour
{
    public RectTransform list;
    public TextMeshProUGUI summary, selected, nowPlaying, status, playLabel, backgroundLabel;
    public Image cover;
    public Button back, preview, full, previous, playPause, next, lyrics, delete;
    public Button background;
    public GameObject dialog;
    public TextMeshProUGUI dialogTitle, dialogBody, confirmLabel;
    public Button confirm, cancel;
    public GameObject libraryPanel, lyricsPanel;
    public ScrollRect lyricsScroll;
    public RectTransform lyricsContent;
    public TextMeshProUGUI lyricsLine;
    public GameObject lyricsLock;
}
