#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
public static class SettingsPauseSetup
{
    static RectTransform Box(Transform t,float x,float y,float w,float h)
    {var r=(RectTransform)t;r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);return r;}
    public static void Configure(GameObject root)
    {
        var menu=root.GetComponent<MainMenuView>();var panel=menu.settingsPanel;var skin=panel.GetComponent<SettingsSkinPanel>();
        foreach(var oldName in new[]{"DSurface","DRim","DRow0","DRow1","DRow2","DRow3","SkinChooser"}){var old=panel.transform.Find(oldName);if(old!=null)Object.DestroyImmediate(old.gameObject);}
        skin.surface=null;skin.border=null;skin.rows=new Image[0];skin.values=new TMP_Text[0];skin.openLayout=true;
        var title=menu.settingsTitleText;Box(title.transform,0,342,800,85);title.fontSize=64;
        var headings=new List<TMP_Text>();
        string[] labels={"VolumeLabel","MusicVolumeLabel","SfxVolumeLabel","VoiceVolumeLabel"};
        var sliders=new[]{menu.volumeSlider,menu.musicVolumeSlider,menu.sfxVolumeSlider,menu.voiceVolumeSlider};
        skin.tracks=new Image[4];
        for(int i=0;i<4;i++)
        {
            float y=210-70*i;var label=panel.transform.Find(labels[i]).GetComponent<TMP_Text>();Box(label.transform,0,y+33,560,36);label.alignment=TextAlignmentOptions.Center;label.fontSize=25;label.fontStyle=FontStyles.Bold|FontStyles.Italic;headings.Add(label);
            Box(sliders[i].transform,0,y,560,32);
            Box(sliders[i].fillRect.parent,0,0,560,8);Box(sliders[i].handleRect.parent,0,0,534,26);
            var track=sliders[i].transform.Find("DTrack");Box(track,0,0,560,8);skin.tracks[i]=track.GetComponent<Image>();
        }
        var lang=menu.languageSelector;Box(lang.transform,0,-104,560,76);
        var languageHeading=lang.GetComponentsInChildren<TMP_Text>(true).First();Box(languageHeading.transform,0,34,560,36);languageHeading.fontSize=25;languageHeading.alignment=TextAlignmentOptions.Center;headings.Add(languageHeading);
        foreach(var oldLanguageArrow in new[]{"Previous language","Next language","Current language"}){var child=lang.transform.Find(oldLanguageArrow);if(child!=null)Object.DestroyImmediate(child.gameObject);}
        var currentLanguage=UIFactory.CreateText("Current language",(RectTransform)lang.transform,"",29,Color.white,TextAlignmentOptions.Center,FontStyles.Bold|FontStyles.Italic);currentLanguage.font=menu.volumeLabelText.font;Box(currentLanguage.transform,0,0,320,52);
        var languagePrevious=Arrow((RectTransform)lang.transform,"Previous language",-215,0,true);
        var languageNext=Arrow((RectTransform)lang.transform,"Next language",215,0,false);
        lang.BindArrows(languagePrevious,languageNext,currentLanguage);
        var languageOptions=lang.transform.Find("Options");if(languageOptions!=null)languageOptions.gameObject.SetActive(false);
        var appearance=panel.GetComponentInChildren<AppearanceSelectorView>(true);Box(appearance.transform,0,-204,560,76);
        var appearanceHeading=appearance.GetComponentsInChildren<TMP_Text>(true).First(t=>t.transform.parent==appearance.transform);Box(appearanceHeading.transform,0,34,560,36);appearanceHeading.fontSize=25;appearanceHeading.alignment=TextAlignmentOptions.Center;headings.Add(appearanceHeading);
        appearance.automatic.gameObject.SetActive(false);
        int appearanceIndex=0;
        foreach(var option in new[]{appearance.lightButton,appearance.dark})
        {
            Box(option.transform,-90+appearanceIndex++*180,0,240,60);
            var mesh=option.GetComponent<SongCardSurfaceMesh>();if(mesh!=null)Object.DestroyImmediate(mesh);
            option.image.preserveAspect=true;option.image.material=null;
            var label=option.GetComponentInChildren<TMP_Text>();label.fontSize=23;
        }
        var chooser=new GameObject("SkinChooser",typeof(RectTransform),typeof(SettingsSkinSelector));chooser.transform.SetParent(panel.transform,false);Box(chooser.transform,0,-304,560,76);
        var selector=chooser.GetComponent<SettingsSkinSelector>();selector.menu=menu;
        var heading=UIFactory.CreateText("Heading",(RectTransform)chooser.transform,"SKIN STYLE",25,Color.white,TextAlignmentOptions.Center,FontStyles.Bold|FontStyles.Italic);Box(heading.transform,0,34,560,36);heading.alignment=TextAlignmentOptions.Center;headings.Add(heading);
        var headingLoc=heading.gameObject.AddComponent<LocalizedText>();headingLoc.key="Settings.Skin";
        var name=UIFactory.CreateText("Skin name",(RectTransform)chooser.transform,"",28,Color.black,TextAlignmentOptions.Center,FontStyles.Bold|FontStyles.Italic);Box(name.transform,0,0,320,52);selector.styleName=name;skin.skinName=name;
        skin.skinNameBackground=null;name.font=menu.volumeLabelText.font;name.fontSharedMaterial=menu.volumeLabelText.fontSharedMaterial;
        var left=UIFactory.CreateButton("Previous skin",(RectTransform)chooser.transform,"",out var leftText);Object.DestroyImmediate(leftText.gameObject);Box(left.transform,-215,0,68,68);left.image.color=Color.clear;left.transition=Selectable.Transition.None;
        var right=UIFactory.CreateButton("Next skin",(RectTransform)chooser.transform,"",out var rightText);Object.DestroyImmediate(rightText.gameObject);Box(right.transform,215,0,68,68);right.image.color=Color.clear;right.transition=Selectable.Transition.None;
        // Native triangular arrows: no additional raster assets.
        var arrows=new List<Image>();
        foreach(var tuple in new[]{(button:left,angle:180f),(button:right,angle:0f)})
        {
            var arrow=new GameObject("Arrow",typeof(RectTransform),typeof(CanvasRenderer),typeof(SettingsSkinArrow));arrow.transform.SetParent(tuple.button.transform,false);Box(arrow.transform,0,0,24,32);arrow.transform.localEulerAngles=new Vector3(0,0,tuple.angle);var graphic=arrow.GetComponent<SettingsSkinArrow>();graphic.raycastTarget=false;
        }
        selector.previous=left;selector.next=right;skin.skinArrowGraphics=panel.GetComponentsInChildren<SettingsSkinArrow>(true);selector.Refresh();
        skin.headings=headings.ToArray();
        Box(menu.closeButton.transform,0,-407,275,62);menu.closeButtonLabel.fontSize=28;
        var blocker=panel.GetComponent<SettingsModalBlocker>();blocker.menuPresentation=menu.presentationRoot.GetComponent<CanvasGroup>();if(blocker.menuPresentation==null)blocker.menuPresentation=menu.presentationRoot.gameObject.AddComponent<CanvasGroup>();var veil=blocker.backdrop.GetComponent<ModalVeilTone>();veil.day=new Color(.97f,.975f,.99f,.68f);veil.night=new Color(.012f,.016f,.03f,.95f);
    }
    static Button Arrow(RectTransform parent,string name,float x,float y,bool left)
    {
        var button=UIFactory.CreateButton(name,parent,"",out var label);Object.DestroyImmediate(label.gameObject);Box(button.transform,x,y,68,68);button.image.color=Color.clear;button.transition=Selectable.Transition.None;
        var go=new GameObject("Arrow",typeof(RectTransform),typeof(CanvasRenderer),typeof(SettingsSkinArrow));go.transform.SetParent(button.transform,false);Box(go.transform,0,0,20,28);go.transform.localEulerAngles=new Vector3(0,0,left?180:0);go.GetComponent<SettingsSkinArrow>().raycastTarget=false;return button;
    }
}
#endif
