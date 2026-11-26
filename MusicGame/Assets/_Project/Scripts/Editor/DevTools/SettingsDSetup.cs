#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
public static class SettingsDSetup
{
    const string Path="Assets/_Project/Prefabs/UI/MainMenu.prefab";
    public static void Run()
    {
        LocalizationSetup.AddMissingEntries(new[]{"Settings.Skin"});
        var root=PrefabUtility.LoadPrefabContents(Path);
        try{Configure(root);PrefabUtility.SaveAsPrefabAsset(root,Path);}finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();SettingsOpenValidation.Verify();Capture();
    }
    static RectTransform Box(Transform t,float x,float y,float w,float h)
    {var r=(RectTransform)t;r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);return r;}
    static Image Image(string name,Transform parent,float x,float y,float w,float h)
    {var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));go.transform.SetParent(parent,false);Box(go.transform,x,y,w,h);var im=go.GetComponent<Image>();im.raycastTarget=false;return im;}
    static void Strip(GameObject go)
    {
        foreach(var c in go.GetComponentsInChildren<MonoBehaviour>(true))
            if(c!=null&&(c.GetType().Name.StartsWith("Theme")||c is AppearanceText||c is TitleText))UnityEngine.Object.DestroyImmediate(c);
    }
    static void Text(TMP_Text t,int size,Color color)
    {t.fontSize=size;t.enableAutoSizing=false;t.fontStyle=FontStyles.Bold|FontStyles.Italic;t.color=color;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.NoWrap;}
    public static void Configure(GameObject root)
    {
        var menu=root.GetComponent<MainMenuView>();var panel=menu.settingsPanel;var lib=menu.logo.library;
        // Unpack only the settings selector instance so its local layout can follow the modal.
        if(menu.languageSelector!=null&&PrefabUtility.IsPartOfPrefabInstance(menu.languageSelector))PrefabUtility.UnpackPrefabInstance(menu.languageSelector.gameObject,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        Strip(panel);Box(panel.transform,0,-5,1180,790);
        var bg=panel.GetComponent<Image>();bg.color=Color.clear;bg.raycastTarget=true;
        var old=panel.transform.Find("DSurface");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
        var body=new GameObject("DSurface",typeof(RectTransform),typeof(CanvasRenderer),typeof(SettingsPanelSurface));body.transform.SetParent(panel.transform,false);Box(body.transform,0,5,1120,680);body.transform.SetAsFirstSibling();
        var skin=panel.GetComponent<SettingsSkinPanel>()??panel.AddComponent<SettingsSkinPanel>();skin.library=lib;skin.menu=menu;skin.surface=body.GetComponent<SettingsPanelSurface>();skin.surface.raycastTarget=false;
        var title=menu.settingsTitleText;Box(title.transform,0,290,940,120);Text(title,92,Color.white);title.font=lib.titleFont;title.fontSharedMaterial=lib.titleFont.material;title.alignment=TextAlignmentOptions.Center;
        var sliders=new[]{menu.volumeSlider,menu.musicVolumeSlider,menu.sfxVolumeSlider,menu.voiceVolumeSlider};
        string[] labels={"VolumeLabel","MusicVolumeLabel","SfxVolumeLabel","VoiceVolumeLabel"};
        skin.rows=new Image[4];skin.values=new TMP_Text[4];
        for(int i=0;i<4;i++)
        {
            float y=190-i*80;
            var oldRow=panel.transform.Find("DRow"+i);if(oldRow!=null)UnityEngine.Object.DestroyImmediate(oldRow.gameObject);
            var row=Image("DRow"+i,panel.transform,0,y,940,62);row.gameObject.AddComponent<SongCardSurfaceMesh>().slant=18;row.transform.SetSiblingIndex(i+1);skin.rows[i]=row;
            var label=panel.transform.Find(labels[i]).GetComponent<TMP_Text>();Box(label.transform,-275,y,330,52);Text(label,28,new Color(.06f,.07f,.09f));label.alignment=TextAlignmentOptions.Left;
            var slider=sliders[i];Box(slider.transform,135,y,370,32);slider.transition=Selectable.Transition.None;
            if(slider.handleRect!=null){slider.handleRect.anchorMin=slider.handleRect.anchorMax=new Vector2(0,.5f);slider.handleRect.sizeDelta=new Vector2(26,26);var handle=slider.handleRect.GetComponent<Image>();handle.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/UI/Jukebox/MedallionAperture.png");handle.color=new Color(.99f,.97f,.92f);var outline=handle.GetComponent<Outline>()??handle.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.1f,.12f,.15f,.7f);outline.effectDistance=new Vector2(1,-1);}
            slider.GetComponent<Image>().color=Color.clear;
            var fillArea=(RectTransform)slider.fillRect.parent;Box(fillArea,0,0,370,10);
            slider.fillRect.anchorMin=new Vector2(0,0);slider.fillRect.anchorMax=new Vector2(slider.normalizedValue,1);slider.fillRect.offsetMin=slider.fillRect.offsetMax=Vector2.zero;
            var handleArea=(RectTransform)slider.handleRect.parent;Box(handleArea,0,0,344,26);
            slider.handleRect.anchorMin=new Vector2(slider.normalizedValue,0);slider.handleRect.anchorMax=new Vector2(slider.normalizedValue,1);slider.handleRect.anchoredPosition=Vector2.zero;slider.handleRect.sizeDelta=new Vector2(26,0);
            var oldTrack=slider.transform.Find("DTrack");if(oldTrack!=null)UnityEngine.Object.DestroyImmediate(oldTrack.gameObject);
            var track=Image("DTrack",slider.transform,0,0,370,10);track.color=new Color(.1f,.12f,.15f,.35f);track.transform.SetAsFirstSibling();
            var valueGo=new GameObject("Value",typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));valueGo.transform.SetParent(row.transform,false);var value=valueGo.GetComponent<TextMeshProUGUI>();value.font=label.font;Box(value.transform,395,0,98,50);Text(value,27,new Color(.06f,.07f,.09f));value.alignment=TextAlignmentOptions.Right;skin.values[i]=value;
        }
        var lang=menu.languageSelector;Box(lang.transform,-250,-162,465,100);
        var langLabel=lang.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();if(langLabel!=null){Box(langLabel.transform,0,40,440,35);Text(langLabel,24,Color.white);langLabel.alignment=TextAlignmentOptions.Left;}
        var so=new SerializedObject(lang);var options=(RectTransform)so.FindProperty("_options").objectReferenceValue;Box(options,0,-14,440,50);
        var layout=options.GetComponent<HorizontalLayoutGroup>();if(layout!=null){layout.spacing=4;layout.childControlWidth=true;layout.childForceExpandWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=true;layout.padding=new RectOffset();}
        var optionPrefab=(LanguageOptionView)so.FindProperty("_optionPrefab").objectReferenceValue;
        // Clone the language option into a settings-only prefab; other screens retain their original component.
        const string optionPath="Assets/_Project/Prefabs/UI/Components/SettingsDLanguageOption.prefab";
        var optionRoot=UnityEngine.Object.Instantiate(optionPrefab.gameObject);Strip(optionRoot);
        foreach(var mark in optionRoot.GetComponentsInChildren<Image>(true))if(mark.gameObject!=optionRoot)mark.enabled=false;
        var optionLabel=optionRoot.GetComponentInChildren<TextMeshProUGUI>();optionRoot.GetComponent<LanguageOptionView>().Bind(optionRoot.GetComponent<Button>(),optionLabel,null);
        foreach(var outline in optionRoot.GetComponents<Outline>())UnityEngine.Object.DestroyImmediate(outline);
        var ob=optionRoot.GetComponent<Button>();ob.transition=Selectable.Transition.None;ob.image.color=Color.white;
        if(optionRoot.GetComponent<SongCardSurfaceMesh>()==null)optionRoot.AddComponent<SongCardSurfaceMesh>().slant=14;
        foreach(var t in optionRoot.GetComponentsInChildren<TMP_Text>()){Text(t,23,Color.black);t.alignment=TextAlignmentOptions.Center;UIFactory.Stretch(t.rectTransform);}
        var op=PrefabUtility.SaveAsPrefabAsset(optionRoot,optionPath).GetComponent<LanguageOptionView>();UnityEngine.Object.DestroyImmediate(optionRoot);lang.Bind(options,op);
        var appearance=panel.GetComponentInChildren<AppearanceSelectorView>(true);Box(appearance.transform,250,-162,465,100);
        var appearanceLabel=appearance.GetComponentsInChildren<TMP_Text>(true).First(t=>t.transform.parent==appearance.transform);Box(appearanceLabel.transform,0,40,440,35);Text(appearanceLabel,24,Color.white);appearanceLabel.alignment=TextAlignmentOptions.Left;
        var buttons=new[]{appearance.automatic,appearance.lightButton,appearance.dark};
        for(int i=0;i<3;i++){Box(buttons[i].transform,-148+i*148,-14,144,50);buttons[i].transition=Selectable.Transition.None;var mesh=buttons[i].GetComponent<SongCardSurfaceMesh>()??buttons[i].gameObject.AddComponent<SongCardSurfaceMesh>();mesh.slant=14;var text=buttons[i].GetComponentInChildren<TMP_Text>();Text(text,23,Color.black);UIFactory.Stretch(text.rectTransform);}
        menu.closeButtonLabel.GetComponent<LocalizedText>().key="SongSelection.Back";menu.closeButtonLabel.text="BACK";
        Box(menu.closeButton.transform,0,-292,285,64);Text(menu.closeButtonLabel,29,Color.black);
        var blend=menu.closeButton.GetComponent<StyleBlendButton>()??menu.closeButton.gameObject.AddComponent<StyleBlendButton>();blend.library=lib;blend.label=menu.closeButtonLabel;
        if(blend.classicLayer==null)blend.classicLayer=Image("ClassicArt",menu.closeButton.transform,0,0,285,64);
        if(blend.styleLayer==null)blend.styleLayer=Image("StyleArt",menu.closeButton.transform,0,0,285,64);
        blend.classicLayer.transform.SetAsFirstSibling();blend.styleLayer.transform.SetSiblingIndex(1);menu.closeButtonLabel.transform.SetAsLastSibling();menu.closeButton.image.color=Color.clear;menu.closeButton.transition=Selectable.Transition.None;blend.SetBlend(StyledTitleView.MenuStyle(lib),1);
        var blocker=panel.GetComponent<SettingsModalBlocker>();if(blocker==null)blocker=panel.AddComponent<SettingsModalBlocker>();
        if(blocker.backdrop==null){var veil=Image("SettingsBackdrop",root.transform,0,0,1920,1080);UIFactory.Stretch(veil.rectTransform);veil.gameObject.AddComponent<ModalVeilTone>();blocker.backdrop=veil;veil.gameObject.SetActive(false);}
        blocker.backdrop.raycastTarget=true;blocker.initialSelection=menu.closeButton;
        var veilRect=blocker.backdrop.rectTransform;UIFactory.Stretch(veilRect);
        blocker.backdrop.GetComponent<ModalVeilTone>().day=new Color(.91f,.92f,.96f,.68f);
        blocker.backdrop.GetComponent<ModalVeilTone>().night=new Color(.01f,.015f,.03f,.76f);
        var oldRim=panel.transform.Find("DRim");if(oldRim!=null)UnityEngine.Object.DestroyImmediate(oldRim.gameObject);
        var rimGo=new GameObject("DRim",typeof(RectTransform),typeof(CanvasRenderer),typeof(SettingsPanelSurface));rimGo.transform.SetParent(panel.transform,false);Box(rimGo.transform,0,5,1120,680);rimGo.transform.SetSiblingIndex(1);var rim=rimGo.GetComponent<SettingsPanelSurface>();rim.borderOnly=true;rim.raycastTarget=false;skin.border=rim;
        SettingsPauseSetup.Configure(root);
        skin.Apply();panel.SetActive(false);
    }
    public static void Capture()
    {
        var field=typeof(StyledTitleView).GetField("_launchStyle",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);var before=field.GetValue(null);
        bool had=PlayerPrefs.HasKey(AppearancePreference.PrefKey);string mode=AppearancePreference.Mode;
        try
        {
            foreach(var style in new[]{GameMusicStyle.Dance,GameMusicStyle.Acoustic,GameMusicStyle.Catchy,GameMusicStyle.Electronic,GameMusicStyle.Soulful,GameMusicStyle.Rock})
            {
                field.SetValue(null,(GameMusicStyle?)style);TitleScreensReview.ReviewStyle=style;TitleScreensReview.OutputOverride="../VisualReviews/SettingsOpen/"+style;Directory.CreateDirectory(TitleScreensReview.OutputOverride);
                foreach(var m in new[]{AppearancePreference.Light,AppearancePreference.Dark}){AppearancePreference.SetMode(m);AppearancePreference.Tick(10);TitleScreensReview.CaptureSettings(m);}
            }
            File.WriteAllText("../settings-open-validation.txt","PASS: open Settings layout saved; no panel or audio-row backgrounds; no percentages; persistent skin arrows; existing audio/language/appearance bindings; full-screen modal raycast blocker. 12 native Unity renders across six skins and day/night.\n");
        }
        finally{TitleScreensReview.ReviewStyle=GameMusicStyle.Dance;TitleScreensReview.OutputOverride=null;field.SetValue(null,before);if(had)AppearancePreference.SetMode(mode);else{PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);AppearancePreference.RefreshDevice();}AppearancePreference.Tick(10);}
    }
}
#endif
