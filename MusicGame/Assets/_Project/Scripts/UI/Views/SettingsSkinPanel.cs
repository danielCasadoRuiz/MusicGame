using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Settings D: existing style artwork and palette, shared across every style and appearance.</summary>
public sealed class SettingsSkinPanel : MonoBehaviour
{
    public StyleSkinLibrarySO library;
    public MainMenuView menu;
    public SettingsPanelSurface surface,border;
    public bool openLayout;
    public TMP_Text[] headings;
    public Image[] tracks;
    public SettingsSkinArrow[] skinArrowGraphics;
    public Image skinNameBackground;
    public Image[] skinArrows;
    public TMP_Text skinName;
    public Image[] rows;
    public TMP_Text[] values;
    public void LateUpdate()=>Apply();
    public void Apply()
    {
        if(library==null||menu==null)return;
        var style=menu.logo!=null&&menu.logo.Shown!=GameMusicStyle.Unknown?menu.logo.Shown:StyledTitleView.MenuStyle(library);
        SongStyleSurface.Palette(library,style,out var a,out var b);
        var card=library.SongCard(style);float day=AppearancePreference.LightWeight;
        if(surface!=null){surface.artwork=card?.sprite;surface.color=Color.Lerp(new Color(.22f,.25f,.29f,.98f),new Color(.34f,.38f,.42f,.98f),day);
        surface.rim=Color.Lerp(Color.white,b,.6f);surface.Refresh();}
        if(border!=null){border.rim=Color.Lerp(Color.white,b,.35f);border.Refresh();}
        foreach(var row in rows){row.sprite=card?.normal;row.color=new Color(1,1,1,.96f);}
        var sliders=new[]{menu.volumeSlider,menu.musicVolumeSlider,menu.sfxVolumeSlider,menu.voiceVolumeSlider};
        for(int i=0;i<sliders.Length;i++)
        {
            var slider=sliders[i];if(slider==null)continue;
            if(slider.fillRect!=null)slider.fillRect.GetComponent<Image>().color=Color.Lerp(b,Color.white,.2f);
            if(values!=null&&i<values.Length&&values[i]!=null)values[i].text=Mathf.RoundToInt(slider.value*100)+"%";
        }
        foreach(var option in GetComponentsInChildren<LanguageOptionView>())
            Option(option.GetComponent<Button>(),option.Mode==LanguagePreference.Mode,style);
        var appearance=GetComponentInChildren<AppearanceSelectorView>();
        if(appearance!=null)
        {
            Option(appearance.automatic,AppearancePreference.Mode==AppearancePreference.Auto,style);
            Option(appearance.lightButton,AppearancePreference.Mode==AppearancePreference.Light||(AppearancePreference.Mode==AppearancePreference.Auto&&day>=.5f),style);
            Option(appearance.dark,AppearancePreference.Mode==AppearancePreference.Dark||(AppearancePreference.Mode==AppearancePreference.Auto&&day<.5f),style);
        }
        var back=menu.closeButton.GetComponent<StyleBlendButton>();if(back!=null)back.SetBlend(style,1);
        if(openLayout)
        {
            GetComponentInChildren<SettingsSkinSelector>()?.Refresh();
            Color ink=Color.Lerp(new Color(.98f,.97f,.94f),new Color(.055f,.065f,.085f),day);
            menu.settingsTitleText.color=UIStylePalette.Ink(menu,library);
            if(headings!=null)foreach(var heading in headings)if(heading!=null)heading.color=ink;
            if(tracks!=null)foreach(var track in tracks)if(track!=null)track.color=Color.Lerp(new Color(1,1,1,.24f),new Color(.06f,.07f,.1f,.25f),day);
            foreach(var slider in sliders)if(slider!=null&&slider.fillRect!=null)slider.fillRect.GetComponent<Image>().color=Color.Lerp(b,Color.Lerp(b,Color.black,.3f),day);
            if(skinNameBackground!=null){skinNameBackground.sprite=library.Button(style)?.normal;skinNameBackground.color=Color.white;}
            if(skinName!=null)skinName.color=ink;
            if(menu.languageSelector.currentLabel!=null)menu.languageSelector.currentLabel.color=ink;
            foreach(var arrow in menu.languageSelector.GetComponentsInChildren<SettingsSkinArrow>())arrow.color=ink;
            if(skinArrowGraphics!=null)foreach(var arrow in skinArrowGraphics)if(arrow!=null)arrow.color=ink;
            if(skinArrows!=null)foreach(var arrow in skinArrows)if(arrow!=null)arrow.color=ink;
        }
        else menu.settingsTitleText.color=UIStylePalette.Ink(menu,library);
    }
    void Option(Button button,bool selected,GameMusicStyle style)
    {
        if(button==null)return;
        var artwork=library.Button(style);button.transition=Selectable.Transition.None;
        button.image.sprite=selected?artwork?.selected:artwork?.normal;button.image.color=Color.white;button.image.preserveAspect=true;button.image.material=null;
        var text=button.GetComponentInChildren<TMP_Text>();
        if(text!=null){text.color=selected?RectangularStyleButton.SelectedInk(library,style):library.labelNormal;text.fontStyle=FontStyles.Bold|FontStyles.Italic;}

    }
}
