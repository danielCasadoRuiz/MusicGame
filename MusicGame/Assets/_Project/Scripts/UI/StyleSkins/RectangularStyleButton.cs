using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Existing normal/selected menu art mapped onto a rectangular list row.</summary>
public sealed class RectangularStyleButton : MonoBehaviour
{
    public StyleSkinLibrarySO library;
    public Button button;
    public Image image;
    public TextMeshProUGUI label;
    public bool songCard;
    public bool invertWithAppearance;
    public bool emphasiseSelection;
    public bool nativeAspect;
    private Material appearanceMaterial;
    private float renderedDay=-1;
    public bool Selected { get; private set; }
    public GameMusicStyle Style { get; private set; }
    private void OnEnable() => SetSelection(Selected,HomeStyle());
    private void LateUpdate(){if(invertWithAppearance&&!songCard&&!Mathf.Approximately(renderedDay,AppearancePreference.LightWeight))SetSelection(Selected,Style);}
    private void OnDestroy(){if(appearanceMaterial!=null){if(Application.isPlaying)Destroy(appearanceMaterial);else DestroyImmediate(appearanceMaterial);}}
    private GameMusicStyle HomeStyle()
    {
        var background=GetComponentInParent<FrontendStyleBackground>();
        if(songCard){var row=GetComponent<SongRowView>();if(row!=null)return row.musicStyle;}
        return background!=null?background.HomeStyle:StyledTitleView.MenuStyle(library);
    }
    public void SetSelection(bool selected,GameMusicStyle style)
    {
        Selected=selected;Style=style;
        if(library==null||image==null)return;
        var card=songCard?library.SongCard(style):null;
        if(card!=null&&card.sprite!=null)
        {
            var appearance=GetComponent<SongCardAppearance>();
            if(appearance!=null){appearance.library=library;appearance.image=image;appearance.label=label;appearance.SetState(style,selected);return;}
            image.sprite=!selected&&card.normal!=null?card.normal:card.sprite;image.material=null;image.type=Image.Type.Simple;image.preserveAspect=false;
            image.color=selected?card.selectedTint:button!=null&&!button.interactable?new Color(.8f,.8f,.8f):Color.white;
            if(label!=null)label.color=library.SongCardInk(style,selected);
            var surface=GetComponent<SongCardSurfaceMesh>();if(surface!=null){surface.trimFrame=selected;surface.SetVerticesDirty();}
            var outline=GetComponent<Outline>();if(outline!=null)outline.enabled=false;
            return;
        }
        var skin=library.Button(style);
        if(invertWithAppearance)
        {
            renderedDay=AppearancePreference.LightWeight;
            float mix=selected?renderedDay:1-renderedDay;
            if(appearanceMaterial==null)appearanceMaterial=new Material(library.songRowMaterial){name="Filter day-night instance",hideFlags=HideFlags.DontSave};
            var normal=skin?.normal??library.classicNormal;var active=skin?.selected??library.classicSelected;
            image.sprite=normal;image.material=appearanceMaterial;image.type=Image.Type.Simple;image.preserveAspect=nativeAspect;image.color=Color.white;
            appearanceMaterial.SetTexture("_OtherTex",active.texture);appearanceMaterial.SetFloat("_StateMix",mix);
            appearanceMaterial.SetFloat("_NativeUV",nativeAspect?1:0);
            SongStyleSurface.Palette(library,style,out var from,out var to);
            appearanceMaterial.SetFloat("_Emphasis",selected&&emphasiseSelection?1:0);
            appearanceMaterial.SetColor("_BodyTint",Color.Lerp(from,Color.black,.42f));
            if(label!=null)label.color=Color.Lerp(library.labelNormal,SelectedInk(library,style),mix);
            if(label!=null&&selected&&emphasiseSelection)label.color=Color.white;
            return;
        }
        image.sprite=skin!=null?(selected?skin.selected:skin.normal):(selected?library.classicSelected:library.classicNormal);
        image.material=library.songRowMaterial;image.type=Image.Type.Simple;image.preserveAspect=false;
        image.color=button!=null&&!button.interactable?new Color(.62f,.62f,.62f,1):Color.white;
        if(label!=null)label.color=selected?SelectedInk(library,style):library.labelNormal;
    }
    public static Color SelectedInk(StyleSkinLibrarySO lib,GameMusicStyle style)
    {
        // These selected skins have a light yellow/ivory centre.
        return style==GameMusicStyle.Acoustic||style==GameMusicStyle.Energetic||style==GameMusicStyle.Groove||style==GameMusicStyle.Chill||style==GameMusicStyle.Dance||style==GameMusicStyle.Funky||style==GameMusicStyle.Cinematic||style==GameMusicStyle.Experimental
            ?lib.labelNormal:lib.labelSelected;
    }
}
