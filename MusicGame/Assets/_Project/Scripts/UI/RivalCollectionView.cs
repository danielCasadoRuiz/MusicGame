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
    public RectTransform detailPanel;
    public CanvasGroup galleryInteraction;
    public RectTransform detailRow;
    public Button detailClose;
    public Image detailAccent;
    public Image[] detailBands;
    private GameMusicStyle _detailStyle = GameMusicStyle.Unknown;

    private Vector2 _lastViewport, _lastGrid;
    private void LateUpdate()
    {
        FitGallery();
        var background = GetComponent<FrontendStyleBackground>();
        if (background != null && background.HomeStyle != _detailStyle) ApplyDetailStyle(background.HomeStyle);
        if (detailPanel != null)
        {
            var viewport = ((RectTransform)transform).rect.size;
            float scale = Mathf.Min(1f, (viewport.x - 48f) / 1240f, (viewport.y - 48f) / 790f);
            detailPanel.localScale = Vector3.one * Mathf.Max(.1f, scale);
        }
    }

    public void SetDetailVisible(bool visible)
    {
        if (galleryInteraction != null) galleryInteraction.interactable = !visible;
        if (backButton != null)
        {
            backButton.interactable = !visible;
            backButton.gameObject.SetActive(!visible);
        }
        if (detailRoot != null)
        {
            detailRoot.gameObject.SetActive(visible);
            if (visible) detailRoot.SetAsLastSibling();
        }
        var events = UnityEngine.EventSystems.EventSystem.current;
        if (events != null) events.SetSelectedGameObject(visible && detailClose != null ? detailClose.gameObject : null);
    }

    public void ApplyDetailStyle(GameMusicStyle style)
    {
        var background = GetComponent<FrontendStyleBackground>();
        if (background == null || background.library == null) return;
        _detailStyle = style;
        var accent = background.library.HudAccent(style);
        if (detailAccent != null) detailAccent.color = accent;
        if (detailBands != null)
            for (int i=0; i<detailBands.Length; i++)
                if (detailBands[i] != null) detailBands[i].color = Color.Lerp(new Color(.97f,.96f,.93f),accent,i==0?.13f:.055f);
        if (detailTitle != null) detailTitle.color = Color.Lerp(new Color(.10f,.10f,.12f),accent,.22f);
    }

    public void FitGallery()
    {
        if (grid == null) return;
        var viewport = ((RectTransform)transform).rect.size;
        var design = grid.rect.size;
        if (viewport == _lastViewport && design == _lastGrid) return;
        if (design.x <= 0 || design.y <= 0 || viewport.x <= 0 || viewport.y <= 0) return;
        float scale = Mathf.Min(1f, Mathf.Min((viewport.x - 64f) / design.x, (viewport.y - 270f) / design.y));
        grid.localScale = Vector3.one * Mathf.Max(.1f, scale);
        _lastViewport = viewport;
        _lastGrid = design;
    }
}
