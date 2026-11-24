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
    public RectangularStyleButton skin;
    public Button button;
    public Image background;
    public TextMeshProUGUI label;
    public TextMeshProUGUI artistLabel;
    public TextMeshProUGUI genreLabel;
    public GameMusicStyle musicStyle;
    public Image genreBadge;
    public Image styleEdge;
    public RawImage styleWatermark;
    public GameObject selectionFrame;
    public SongSelectionFrame selectionRivet;
    public TextMeshProUGUI completedLabel;
    public RectTransform equalizer;
    public RectTransform[] equalizerBars;
    public Image playedBadge;
    public bool IsPreviewing { get; private set; }
    public bool showcase;
    public Image selectionArtwork;
    public Image cardShadow;
    private string laidOutTitle;
    private float laidOutWidth=-1;
    public Button playPauseButton;
    public TextMeshProUGUI playPauseLabel;
    [Tooltip("Played-at-least-once tick (placeholder sprite, swap freely). Not interactive.")]
    public Image playedCheck;
    public Color playedOnColor = new(0.45f, 0.85f, 0.5f, 1f);
    public Color playedOffColor = new(1f, 1f, 1f, 0.15f);

    /// <summary>What the check currently shows (the all-time played history).</summary>
    public bool Played { get; private set; }

    public void SetSelection(bool selected,GameMusicStyle style)
    {
        if(skin==null)return;
        skin.SetSelection(selected,style);
        if(selectionFrame!=null)selectionFrame.SetActive(showcase&&selected);
        var group=GetComponent<CanvasGroup>();if(group!=null)group.alpha=1;
        var home=GetComponentInParent<FrontendStyleBackground>();
        if(selectionRivet!=null)selectionRivet.color=skin.library.HudAccent(home!=null?home.HomeStyle:style);
        if(artistLabel!=null)artistLabel.color=skin.label.color;
        if(genreLabel!=null)genreLabel.color=skin.label.color;
        if(styleEdge!=null)styleEdge.gameObject.SetActive(false);
        if(styleWatermark!=null)styleWatermark.gameObject.SetActive(false);
        if(genreBadge!=null)genreBadge.enabled=false;
        if(genreLabel!=null){var ink=skin.label.color;genreLabel.color=new Color(ink.r,ink.g,ink.b,.82f);}
        SetPreview(IsPreviewing);
        playedOnColor=selected?skin.label.color:new Color(.12f,.36f,.24f);
        playedOffColor=selected?new Color(skin.label.color.r,skin.label.color.g,skin.label.color.b,.18f):new Color(.1f,.14f,.18f,.14f);
        SetPlayed(Played);
    }

    public void SetPlayed(bool played)
    {
        Played = played;
        if(playedBadge!=null)playedBadge.gameObject.SetActive(false);
        if(playedCheck!=null)playedCheck.gameObject.SetActive(false);
        if(completedLabel!=null){completedLabel.gameObject.SetActive(!button.interactable);Loc.Set(completedLabel,"SongSelection.Completed");completedLabel.color=label.color;}
    }
    public void SetPreview(bool playing)
    {
        IsPreviewing=playing;
        if(equalizer!=null)equalizer.gameObject.SetActive(playing);
    }
    private void Update()
    {
        if(!IsPreviewing||equalizerBars==null)return;
        for(int i=0;i<equalizerBars.Length;i++)
        {
            var bar=equalizerBars[i];if(bar==null)continue;
            bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,5+19*(.5f+.5f*Mathf.Sin(Time.unscaledTime*(8+i*.9f)+i*1.7f)));
            bar.GetComponent<Image>().color=label.color;
        }
    }
    private void LateUpdate()=>RefreshAppearance();
    public void RefreshAppearance()
    {
        if(showcase&&skin!=null)
        {
            transform.localScale=Vector3.one;
            var grow=skin.Selected?new Vector2(22,40):Vector2.zero;
            float blend=Application.isPlaying?1-Mathf.Exp(-14*Time.unscaledDeltaTime):1;
            if(background!=null)
            {
                var rect=background.rectTransform;
                rect.offsetMin=Vector2.Lerp(rect.offsetMin,-grow*.5f,blend);rect.offsetMax=Vector2.Lerp(rect.offsetMax,grow*.5f,blend);
                if(cardShadow!=null)
                {
                    cardShadow.rectTransform.offsetMin=rect.offsetMin+new Vector2(0,-7);
                    cardShadow.rectTransform.offsetMax=rect.offsetMax+new Vector2(0,-7);
                }
            }
            if(selectionArtwork!=null)
            {
                var rect=(RectTransform)selectionFrame.transform;
                rect.offsetMin=-grow*.5f-new Vector2(3,3);rect.offsetMax=grow*.5f+new Vector2(3,3);
            }
            if(label!=null&&artistLabel!=null&&(laidOutTitle!=label.text||!Mathf.Approximately(laidOutWidth,label.rectTransform.rect.width)))
            {
                laidOutTitle=label.text;laidOutWidth=label.rectTransform.rect.width;
                float height=Mathf.Clamp(label.GetPreferredValues(label.text,Mathf.Max(1,laidOutWidth),0).y,40,86);
                var pos=artistLabel.rectTransform.anchoredPosition;pos.y=label.rectTransform.anchoredPosition.y-height-12;
                float slant=background.GetComponent<SongCardSurfaceMesh>().slant;
                pos.x=label.rectTransform.anchoredPosition.x-slant*(height+12)/Mathf.Max(1,((RectTransform)transform).rect.height);
                artistLabel.rectTransform.anchoredPosition=pos;
            }
            if(selectionFrame!=null)selectionFrame.SetActive(skin.Selected);
            if(selectionRivet!=null)selectionRivet.color=Color.Lerp(new Color(1,.84f,.45f),Color.white,.35f);
        }
        var appearance=GetComponent<SongCardAppearance>();
        if(appearance!=null&&appearance.library!=null)label.color=appearance.TextInk;
        if(selectionArtwork!=null&&appearance!=null&&appearance.library!=null)
        {
            var currentSkin=GetComponentInParent<FrontendStyleBackground>();
            SongStyleSurface.Palette(appearance.library,currentSkin!=null?currentSkin.HomeStyle:musicStyle,out var dark,out var accent);
            // The frame belongs to the screen skin; its colour follows the day/night transition.
            float bright=AppearancePreference.LightWeight;
            selectionArtwork.color=Color.Lerp(dark,accent,bright);
        }
        if(artistLabel!=null)artistLabel.color=label.color;
        if(genreLabel!=null){var ink=label.color;genreLabel.color=new Color(ink.r,ink.g,ink.b,.82f);}
        if(completedLabel!=null)completedLabel.color=Color.Lerp(Color.white,Color.black,AppearancePreference.LightWeight);
    }
}
