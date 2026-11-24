using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>Two-row catalogue with finite continuous horizontal scrolling and authored decade filters.</summary>
public sealed class SongCatalogCarousel : MonoBehaviour, IBeginDragHandler
{
    public TextMeshProUGUI positionLabel;
    public RectTransform positionThumb;
    public Image positionTrack;
    public Button[] decadeButtons;
    private readonly List<int> visible=new();
    private IReadOnlyList<SongRowView> rows;
    private IReadOnlyList<int> decades;
    private Action<int> changed;
    private ScrollRect scroll;
    private RectTransform content;
    private int selected=-1,decade;
    private float targetOffset;
    private bool reveal;
    private Vector2 lastSize;
    private GameMusicStyle lastStyle=(GameMusicStyle)(-1);
    private float lastLight=-1;
    public int rowCount=2;
    public int visibleColumns=3;
    public float cardPadding;
    public float cardGap=24;
    public float referenceWidth;
    public int Center=>selected;
    public int Count=>rows?.Count??0;
    public int VisibleCount=>visible.Count;
    public int CurrentDecade=>decade;
    public float MaxOffset=>Mathf.Max(0,content.rect.width-scroll.viewport.rect.width);
    private float CardWidth=>((referenceWidth>0?referenceWidth:scroll.viewport.rect.width)-2*cardPadding-(visibleColumns-1)*cardGap)/visibleColumns;
    private float Pitch=>CardWidth+cardGap;

    public void Setup(IReadOnlyList<SongRowView> items,Action<int> onChanged,IReadOnlyList<int> itemDecades=null)
    {
        rows=items;changed=onChanged;decades=itemDecades;
        scroll=GetComponent<ScrollRect>();content=scroll.content;
        scroll.enabled=true;scroll.horizontal=true;scroll.vertical=false;
        scroll.movementType=ScrollRect.MovementType.Clamped;scroll.inertia=true;scroll.decelerationRate=.13f;scroll.scrollSensitivity=65;
        if(scroll.verticalScrollbar!=null)scroll.verticalScrollbar.gameObject.SetActive(false);
        if(scroll.horizontalScrollbar!=null)scroll.horizontalScrollbar.gameObject.SetActive(false);
        scroll.verticalScrollbar=null;scroll.horizontalScrollbar=null;
        var layout=content.GetComponent<VerticalLayoutGroup>();if(layout!=null)layout.enabled=false;
        var fitter=content.GetComponent<ContentSizeFitter>();if(fitter!=null)fitter.enabled=false;
        content.anchorMin=new Vector2(0,0);content.anchorMax=new Vector2(0,1);content.pivot=new Vector2(0,1);
        content.offsetMin=content.offsetMax=Vector2.zero;
        scroll.onValueChanged.AddListener(_=>UpdateIndicator());
        if(decadeButtons!=null)for(int i=0;i<decadeButtons.Length;i++)
        {
            int value=i==0?0:1970+(i-1)*10;
            decadeButtons[i].onClick.AddListener(()=>Filter(value));
        }
        Filter(0);
    }
    private int DecadeFor(int index)=>decades!=null&&index<decades.Count?decades[index]:0;
    public void Filter(int value)
    {
        decade=value;visible.Clear();reveal=false;scroll.StopMovement();
        for(int i=0;i<Count;i++)
        {
            bool show=value==0||DecadeFor(i)==value;
            rows[i].gameObject.SetActive(show);if(show)visible.Add(i);
        }
        // Filtering changes visibility, never the player's choice or its preview.
        Layout();content.anchoredPosition=Vector2.zero;
        lastStyle=(GameMusicStyle)(-1);UpdateStyle();UpdateIndicator();
    }
    private void Layout()
    {
        float height=(scroll.viewport.rect.height-2*cardPadding-(rowCount-1)*cardGap)/rowCount;
        int columns=(visible.Count+rowCount-1)/rowCount;
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,Mathf.Max(scroll.viewport.rect.width,2*cardPadding+columns*Pitch-cardGap));
        content.sizeDelta=new Vector2(content.sizeDelta.x,0);
        for(int n=0;n<visible.Count;n++)
        {
            var rect=(RectTransform)rows[visible[n]].transform;
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=Vector2.one*.5f;
            rect.sizeDelta=new Vector2(CardWidth,height);rect.localScale=Vector3.one;
            rect.anchoredPosition=new Vector2(cardPadding+n/rowCount*Pitch+CardWidth*.5f,-cardPadding-(n%rowCount)*(height+cardGap)-height*.5f);
            var group=rows[visible[n]].GetComponent<CanvasGroup>();if(group!=null){group.alpha=1;group.blocksRaycasts=group.interactable=true;}
        }
        lastSize=scroll.viewport.rect.size;
    }
    public void MoveTo(int index,Action onArrival=null)
    {        if(index<0||index>=Count||!visible.Contains(index)||!rows[index].button.interactable)return;
        selected=index;changed?.Invoke(index);
        float left=visible.IndexOf(index)/rowCount*Pitch,right=left+CardWidth+2*cardPadding,offset=-content.anchoredPosition.x;
        targetOffset=Mathf.Clamp(left<offset?left:right>offset+scroll.viewport.rect.width?right-scroll.viewport.rect.width:offset,0,MaxOffset);
        reveal=true;scroll.StopMovement();onArrival?.Invoke();
    }
    private void LateUpdate()
    {
        if(scroll==null)return;
        if(lastSize!=scroll.viewport.rect.size)Layout();
        if(reveal)
        {
            float next=Mathf.MoveTowards(-content.anchoredPosition.x,targetOffset,2200*Mathf.Min(.05f,Time.unscaledDeltaTime));
            content.anchoredPosition=new Vector2(-next,0);if(Mathf.Approximately(next,targetOffset))reveal=false;
        }
        UpdateStyle();UpdateIndicator();
    }
    private void UpdateStyle()
    {
        var bg=GetComponentInParent<FrontendStyleBackground>();if(bg==null)return;
        float light=bg.InkLightWeight;
        if(bg.HomeStyle==lastStyle&&Mathf.Approximately(light,lastLight))return;
        lastStyle=bg.HomeStyle;lastLight=light;
        for(int i=0;i<Count;i++)rows[i].SetSelection(i==selected,rows[i].musicStyle);
        if(decadeButtons!=null)for(int i=0;i<decadeButtons.Length;i++)
        {
            int value=i==0?0:1970+(i-1)*10;
            var surface=decadeButtons[i].GetComponentInChildren<SongStyleSurface>();
            var texture=decadeButtons[i].GetComponent<RectangularStyleButton>();
            if(texture!=null)texture.SetSelection(value==decade,bg.HomeStyle);
            SongStyleSurface.Palette(bg.library,bg.HomeStyle,out var from,out var to);
            bool active=value==decade;
            if(surface!=null)surface.Apply(active?from:Color.Lerp(new Color(.80f,.83f,.88f),Color.white,.45f),active?to:new Color(.96f,.97f,.99f),Color.Lerp(bg.library.HudAccent(bg.HomeStyle),Color.white,.3f));
            var text=decadeButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            text.text=value==0?Loc.Get("SongSelection.AllDecades"):value.ToString()+"s";
            text.fontStyle=FontStyles.Bold;text.fontWeight=FontWeight.Bold;
            if(texture==null)text.color=new Color(.025f,.035f,.055f);
        }
        if(positionLabel!=null)positionLabel.color=Color.Lerp(new Color(.85f,.86f,.9f),new Color(.18f,.21f,.27f),light);
        if(positionTrack!=null)positionTrack.color=new Color(.025f,.035f,.055f,.55f);
        if(positionThumb!=null)positionThumb.GetComponent<Image>().color=Color.Lerp(bg.library.HudAccent(bg.HomeStyle),Color.white,.72f);
    }
    private void UpdateIndicator()
    {
        if(positionLabel!=null)positionLabel.text=visible.Count==0?Loc.Get("SongSelection.EmptyDecade"):Loc.Get("SongSelection.CatalogCount",visible.Count);
        if(positionThumb==null)return;
        float fraction=content.rect.width>0?Mathf.Clamp01(scroll.viewport.rect.width/content.rect.width):1;
        float amount=MaxOffset>0?Mathf.Clamp01(-content.anchoredPosition.x/MaxOffset):0;
        positionThumb.anchorMin=new Vector2(amount*(1-fraction),0);positionThumb.anchorMax=new Vector2(amount*(1-fraction)+fraction,1);
        positionThumb.offsetMin=positionThumb.offsetMax=Vector2.zero;
    }
    private void OnDisable(){reveal=false;}
    public void OnBeginDrag(PointerEventData data){reveal=false;}
}
