using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shares the authored light/colour pair and reverses their roles at night with the appearance fade.</summary>
public sealed class SongCardAppearance : MonoBehaviour
{
    public StyleSkinLibrarySO library;
    public Image image;
    public TextMeshProUGUI label;
    public bool stage;
    public Texture2D stageMask;
    public Sprite stageArtwork;
    private GameMusicStyle style;
    private bool selected;
    private Material runtime;
    private SongRowView row;
    public bool Completed => row!=null&&row.button!=null&&!row.button.interactable;
    public Color TextInk => stage?Color.white:Completed
        ? Color.Lerp(new Color(.72f,.72f,.72f),new Color(.38f,.38f,.38f),AppearancePreference.LightWeight)
        : Color.Lerp(library.SongCardInk(style,true),library.songNormalInk,LightSurfaceWeight);
    public float LightSurfaceWeight=>stage?0:SurfaceWeight(selected,AppearancePreference.LightWeight);
    public static float SurfaceWeight(bool selected,float day)=>selected?1-Mathf.Clamp01(day):Mathf.Clamp01(day);
    public void SetState(GameMusicStyle value,bool isSelected)
    {
        style=value;selected=isSelected;Render();
    }
    private void LateUpdate()=>Render();
    private void OnDestroy(){if(runtime!=null){if(Application.isPlaying)Destroy(runtime);else DestroyImmediate(runtime);}}
    private void Render()
    {
        if(library==null||image==null||library.songCardAppearanceMaterial==null)return;
        if(row==null)row=GetComponentInParent<SongRowView>();
        var card=library.SongCard(style);if(card==null||card.sprite==null)return;
        if(runtime==null)runtime=new Material(library.songCardAppearanceMaterial){name="Song appearance instance",hideFlags=HideFlags.DontSave};
        var hero=card.stageArtwork!=null?card.stageArtwork:stageArtwork;
        image.sprite=stage&&hero!=null?hero:card.sprite;image.material=runtime;image.color=Color.white;
        image.type=Image.Type.Simple;image.preserveAspect=false;
        runtime.SetTexture("_NormalTex",card.normal!=null?card.normal.texture:card.sprite.texture);
        runtime.SetColor("_SelectedTint",card.selectedTint);
        runtime.SetFloat("_LightSurface",LightSurfaceWeight);
        runtime.SetFloat("_Completed",Completed?1:0);
        runtime.SetFloat("_Stage",stage?1:0);
        SongStyleSurface.Palette(library,style,out var stageBase,out var stageAccent);
        runtime.SetColor("_StageBase",stageBase);runtime.SetColor("_StageAccent",stageAccent);
        runtime.SetColor("_StageSecondary",Color.Lerp(stageBase,Color.white,.26f));
        runtime.SetFloat("_StageRecolour",card.stageArtwork!=null||style==GameMusicStyle.Dance?0:1);
        runtime.SetVector("_StageWindow",new Vector4(card.stageUVRange.x,card.stageUVRange.y,card.stageArtwork!=null?1:0,0));
        if(stageMask!=null)runtime.SetTexture("_StageMask",stageMask);
        var bounds=image.rectTransform.rect;
        float source=image.sprite.rect.width/image.sprite.rect.height,dest=bounds.width/Mathf.Max(1,bounds.height);
        runtime.SetVector("_StageCrop",dest>source?new Vector4(1,source/dest,0,0):new Vector4(dest/source,1,0,0));
        if(label!=null)label.color=TextInk;
        var fit=image.GetComponent<SongCardSurfaceMesh>();if(fit!=null&&fit.trimFrame){fit.trimFrame=false;fit.SetVerticesDirty();}
    }
}
