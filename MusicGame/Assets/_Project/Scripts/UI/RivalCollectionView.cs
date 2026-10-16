using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Root of Prefabs/UI/Collection/RivalCollectionScreen.prefab: title, summary, card grid
/// (GridLayoutGroup + ContentSizeFitter: cell size / spacing / columns edited in the prefab), back button,
/// and the per-rival detail overlay (title, biography, versions row).</summary>
public class RivalCollectionView : MonoBehaviour
{
    public TextMeshProUGUI summary;
    public RectTransform grid;
    public Button backButton;
    public RectTransform detailRoot;
    public TextMeshProUGUI detailTitle;
    public TextMeshProUGUI detailBio;
    public RectTransform detailRow;
    public Button detailClose;
}
