#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class AppearanceSetup
{
    public const string LibraryPath="Assets/_Project/Configs/UI/StyleSkinLibrary.asset";
    public const string MaterialPath="Assets/_Project/Configs/UI/MenuAppearance.mat";
    public const string Screens="Assets/_Project/Prefabs/UI/";
    public static readonly string[] LightStyles={"Acoustic","Catchy","Chill","Cinematic","Dance","Dark","Dreamy","Electronic","Energetic","Experimental","Funky","Groove","Heavy","Jazzy","Rock","Soulful"};

    [MenuItem("Tools/MusicGame/UI/Setup Day And Night Appearance")]
    public static void InstallAndVerify() { Install(); AppearanceValidation.Verify(); }

    public static void InstallBackgrounds()
    {
        var lib=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(LibraryPath);
        foreach(var style in LightStyles)
        {
            string path=$"Assets/_Project/Art/UI/MenuBackgrounds/Day/{style}.png";
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            if(importer==null)throw new Exception("Missing day texture "+style);
            importer.textureType=TextureImporterType.Default;importer.mipmapEnabled=false;
            importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=2048;
            importer.alphaSource=TextureImporterAlphaSource.None;importer.sRGBTexture=true;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
            var entry=lib.menuBackgrounds.Find(b=>b.style.ToString()==style);
            if(entry==null)throw new Exception("Missing style entry "+style);
            entry.dayTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            entry.dayKeepsLightInk=style=="Dark";
        }
        AssetDatabase.ImportAsset("Assets/_Project/Shaders/MenuAppearance.shader",ImportAssetOptions.ForceSynchronousImport);
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material==null)
        {
            material=new Material(Shader.Find("MusicGame/UI/MenuAppearance"));
            AssetDatabase.CreateAsset(material,MaterialPath);
        }
        lib.menuAppearanceMaterial=material;EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
    }

    public static void Install()
    {
        InstallBackgrounds();
        LocalizationSetup.AddMissingEntries(LocalizationSetup.Entries.Where(e=>e.key.StartsWith("Settings.Appearance")).Select(e=>e.key));
        foreach(var screen in new[]{"MainMenu","Jukebox","SongSelection","AnalyzingScreen","Progression/ProgressionScreen","Collection/RivalCollectionScreen"})
        {
            string path=Screens+screen+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                if(screen=="MainMenu")ConfigureMainMenu(root);else ConfigureScreen(root);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        AssetDatabase.SaveAssets();
    }

    public static void ConfigureMainMenu(GameObject root)
    {
        var view=root.GetComponent<MainMenuView>();if(view==null)return;
        Ink(view.profileText,null,view);Ink(view.menuStatusText,null,view);
        var panel=(RectTransform)view.settingsPanel.transform;
        panel.sizeDelta=new Vector2(520,660);
        foreach(var receiver in panel.GetComponents<ThemeColorReceiver>())UnityEngine.Object.DestroyImmediate(receiver);
        panel.GetComponent<Image>().color=new Color(.035f,.045f,.065f,1f);
        var title=view.settingsTitleText.GetComponent<TitleText>();
        if(title!=null){title.tier=TitleTier.Section;title.Apply();}
        Box(view.settingsTitleText.rectTransform,0,20,480,55);
        Box(view.volumeLabelText.rectTransform,0,95,440,24);
        Box((RectTransform)view.volumeSlider.transform,0,126,440,16);
        if(view.languageSelector!=null)Box((RectTransform)view.languageSelector.transform,0,166,440,94);
        string[] channels={"Music","Sfx","Voice"};
        var sliders=new[]{view.musicVolumeSlider,view.sfxVolumeSlider,view.voiceVolumeSlider};
        for(int i=0;i<3;i++)
        {
            var label=panel.Find(channels[i]+"VolumeLabel") as RectTransform;
            if(label!=null)Box(label,0,278+i*56,440,22);
            if(sliders[i]!=null)Box((RectTransform)sliders[i].transform,0,308+i*56,440,14);
        }
        var row=panel.Find("AppearanceSelector") as RectTransform;
        if(row==null)row=UIFactory.CreateRect("AppearanceSelector",panel);
        Box(row,0,474,440,88);
        var selector=row.GetComponent<AppearanceSelectorView>();if(selector==null)selector=row.gameObject.AddComponent<AppearanceSelectorView>();
        var heading=Text(row,"Heading",view.volumeLabelText.font,18);Box(heading.rectTransform,0,0,440,25);
        Localize(heading,"Settings.Appearance");heading.alignment=TextAlignmentOptions.Left;
        var modes=new[]{"Auto","Light","Dark"};var buttons=new Button[3];
        for(int i=0;i<3;i++)
        {
            var found=row.Find(modes[i]);
            var button=found!=null?found.GetComponent<Button>():UIFactory.CreateButton(modes[i],row,"",out _);
            buttons[i]=button;
            Box((RectTransform)button.transform,-150+i*150,36,140,48);
            var label=button.GetComponentInChildren<TextMeshProUGUI>(true);
            label.font=view.volumeLabelText.font;label.fontSize=18;label.enableAutoSizing=true;label.fontSizeMin=13;label.fontSizeMax=18;label.raycastTarget=false;
            Localize(label,"Settings.Appearance"+modes[i]);
            button.image.sprite=null;button.colors=ColorBlock.defaultColorBlock;
        }
        selector.automatic=buttons[0];selector.lightButton=buttons[1];selector.dark=buttons[2];selector.Refresh();
        UIFactory.SetBox((RectTransform)view.closeButton.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,24),new Vector2(220,48));
        if(view.logo!=null&&view.logo.library!=null)SettingsDSetup.Configure(root);
    }

    public static void ConfigureScreen(GameObject root)
    {
        var bg=root.GetComponent<FrontendStyleBackground>();if(bg==null)return;
        var title=root.transform.Find("Title")?.GetComponent<TextMeshProUGUI>();Ink(title,bg,null);
        var collection=root.GetComponent<RivalCollectionView>();
        if(collection!=null)Ink(collection.summary,bg,null);
        var progression=root.GetComponent<ProgressionScreenView>();
        if(progression!=null){Ink(progression.summary,bg,null);Ink(progression.requirements,bg,null);Ink(progression.seal,bg,null);}
        // Keep data-list surfaces dark so their existing semantic/status text stays readable in either mode.
        if(root.GetComponent<SongSelectionView>()!=null||progression!=null)
            foreach(var scroll in root.GetComponentsInChildren<ScrollRect>(true))
            {
                var surface=scroll.GetComponent<Image>();
                if(surface==null)surface=scroll.gameObject.AddComponent<Image>();
                surface.color=new Color(.035f,.045f,.065f,.94f);
            }
    }

    private static void Ink(TextMeshProUGUI text,FrontendStyleBackground bg,MainMenuView menu)
    {
        if(text==null)return;
        var ink=text.GetComponent<AppearanceText>();
        if(ink==null){var original=text.color;ink=text.gameObject.AddComponent<AppearanceText>();ink.night=original;}
        ink.background=bg;ink.mainMenu=menu;
    }
    private static TextMeshProUGUI Text(RectTransform parent,string name,TMP_FontAsset font,int size)
    {
        var child=parent.Find(name);var text=child!=null?child.GetComponent<TextMeshProUGUI>():UIFactory.CreateText(name,parent,"",size,new Color(.9f,.88f,.82f));
        text.font=font;text.raycastTarget=false;return text;
    }
    private static void Localize(TextMeshProUGUI text,string key)
    {
        var localized=text.GetComponent<LocalizedText>();if(localized==null)localized=text.gameObject.AddComponent<LocalizedText>();
        localized.key=key;text.text=LocalizationSetup.Entries.First(e=>e.key==key).en;
    }
    private static void Box(RectTransform rect,float x,float y,float width,float height)
    {var top=new Vector2(.5f,1);UIFactory.SetBox(rect,top,top,top,new Vector2(x,-y),new Vector2(width,height));}
}
#endif
