using TMPro;
using UnityEngine;

/// <summary>Concept D: current-skin header and selected-song stage, independent of each catalogue song's skin.</summary>
public sealed class SongSelectionShowcase : MonoBehaviour
{
    public SongSelectionView view;
    public StyledTitleView logo;
    public TextMeshProUGUI artist;
    public SongMusicBars bars;
    public SongTitlePreviewProgress titleProgress;
    public SongCatalogCarousel catalogue;
    public TextMeshProUGUI placeholderTitle,placeholderArtist;
    private bool placeholdersBound;
    public SongStyleSurface logoPlate;
    public UnityEngine.UI.RawImage paintLeft,paintRight;
    GameMusicStyle shown=(GameMusicStyle)(-1);
    void LateUpdate()=>Render();
    public void Render()
    {
        var bg=GetComponent<FrontendStyleBackground>();if(bg==null)return;
        if(catalogue!=null)
        {
            float width=((RectTransform)view.transform).rect.width/Mathf.Max(.1f,view.contentRoot.localScale.x);
            var rect=(RectTransform)catalogue.transform;
            if(width>0&&!Mathf.Approximately(rect.rect.width,width))rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
            if(view.selectedTexture!=null&&width>0)
            {
                var artwork=view.selectedTexture.rectTransform;
                float extra=Mathf.Max(0,width-view.selectedCard.rectTransform.rect.width)+160;
                if(!Mathf.Approximately(artwork.sizeDelta.x,extra))
                {
                    artwork.sizeDelta=new Vector2(extra,artwork.sizeDelta.y);
                    view.selectedAppearance.SetState(bg.HomeStyle,true);
                }
            }
        }
        if(logo!=null&&shown!=bg.HomeStyle){logo.Show(bg.HomeStyle);shown=bg.HomeStyle;}
        if(logo!=null){if(logo.background!=null)logo.background.gameObject.SetActive(false);if(logo.foreground!=null)logo.foreground.gameObject.SetActive(false);}
        SongStyleSurface.Palette(bg.library,bg.HomeStyle,out var from,out var to);
        if(logoPlate!=null)logoPlate.Apply(Color.Lerp(from,Color.black,.8f),Color.Lerp(from,Color.black,.62f),new Color(.9f,.74f,.43f));
        if(paintLeft!=null)paintLeft.color=to;
        if(paintRight!=null)paintRight.color=bg.HomeStyle==GameMusicStyle.Dance?new Color(.03f,.82f,.97f):from;
        bool hasSong=!string.IsNullOrWhiteSpace(view.selectedSongText.text);
        view.selectedSongText.gameObject.SetActive(hasSong);
        if(placeholderTitle!=null&&placeholderArtist!=null)
        {
            if(!placeholdersBound){Loc.Set(placeholderTitle,"SongSelection.PlaceholderTitle");Loc.Set(placeholderArtist,"SongSelection.PlaceholderArtist");placeholdersBound=true;}
            placeholderTitle.gameObject.SetActive(!hasSong);placeholderArtist.gameObject.SetActive(!hasSong);
            placeholderTitle.color=Color.white;placeholderArtist.color=Color.white;
        }
        if(artist!=null){artist.gameObject.SetActive(hasSong);artist.color=view.selectedAppearance.TextInk;}
        if(view.previewProgressRoot!=null)view.previewProgressRoot.SetActive(false);
        var glow=Color.Lerp(to,Color.white,.06f);
        if(bars!=null)bars.gameObject.SetActive(false);
        if(titleProgress!=null)titleProgress.Render(glow);
    }
}
