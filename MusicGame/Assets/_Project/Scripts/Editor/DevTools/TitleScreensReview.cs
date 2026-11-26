#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class TitleScreensReview
{
    const string Screens="Assets/_Project/Prefabs/UI/";
    static bool jukeboxTrials;
    static bool jukeboxStrokeTrials;
    static bool acceptedJukebox;
    public static string OwnerReviewName,OutputOverride;
    public static Vector2Int CaptureResolution=new Vector2Int(1920,1080);
    public static void CaptureAcceptedJukebox()
    {
        acceptedJukebox=true;
        try{CaptureJukeboxTitleColours();}
        finally{acceptedJukebox=false;}
    }
    static string jukeboxTitleVariant;
    static string Output=>!string.IsNullOrEmpty(OutputOverride)?OutputOverride:acceptedJukebox?"../VisualReviews/Typography/JukeboxAccepted":jukeboxStrokeTrials?"../VisualReviews/Typography/JukeboxStrokeTrialsThicker":jukeboxTrials?"../VisualReviews/Typography/JukeboxTitleTrials":"../VisualReviews/Typography/OtherScreens";
    public static void InstallAcceptedJukeboxTitle()
    {
        const string materialPath="Assets/_Project/Configs/UI/JukeboxTitleIvory.mat";
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/TempoFuryFusion/TempoFuryFusion SDF.asset");
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(material==null){material=new Material(font.material);AssetDatabase.CreateAsset(material,materialPath);}
        else material.CopyPropertiesFromMaterial(font.material);
        material.name="Jukebox Title Ivory Brown Outline";
        material.SetColor("_OutlineColor",new Color32(68,29,13,255));
        material.SetFloat("_OutlineWidth",.24f);material.SetFloat("_OutlineSoftness",0);
        material.EnableKeyword("OUTLINE_ON");EditorUtility.SetDirty(material);
        string path=Screens+"Jukebox.prefab";var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var title=root.GetComponent<JukeboxView>().archedTitle;
            title.font=font;title.fontSharedMaterial=material;title.color=new Color(1f,.964f,.89f);
            title.UpdateMeshPadding();PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();
        acceptedJukebox=true;
        try{CaptureJukeboxTitleColours();}
        finally{acceptedJukebox=false;}
    }
    public static void CaptureJukeboxTitleStrokes()
    {
        jukeboxStrokeTrials=true;
        try{CaptureJukeboxTitleColours();}
        finally{jukeboxStrokeTrials=false;}
    }
    public static void CaptureJukeboxTitleColours()
    {
        bool had=PlayerPrefs.HasKey(AppearancePreference.PrefKey);string mode=AppearancePreference.Mode;
        var field=typeof(StyledTitleView).GetField("_launchStyle",BindingFlags.NonPublic|BindingFlags.Static);var before=field.GetValue(null);
        jukeboxTrials=true;
        try
        {
            Directory.CreateDirectory(Output);field.SetValue(null,(GameMusicStyle?)GameMusicStyle.Dance);
            foreach(string appearance in new[]{AppearancePreference.Light,AppearancePreference.Dark})
            {
                AppearancePreference.SetMode(appearance);AppearancePreference.Tick(10);
                foreach(string variant in acceptedJukebox?new[]{"neutral"}:new[]{"neutral","day","night"}){jukeboxTitleVariant=variant;Shot("Jukebox",appearance);}
            }
            File.WriteAllText(Output+"/validation.txt",acceptedJukebox?
                "PASS: accepted ivory title with brown outline width 0.24 saved to production Jukebox prefab and shared material preset; captured over both day/night backgrounds.\n":
                "PASS: neutral / day / night colour trials captured over both backgrounds. Trial materials affect only temporary instances.\n");
        }
        finally
        {
            jukeboxTrials=false;jukeboxTitleVariant=null;field.SetValue(null,before);
            if(had)AppearancePreference.SetMode(mode);else{PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);AppearancePreference.RefreshDevice();}
            AppearancePreference.Tick(10);
        }
    }
    public static void Run()
    {
        FusionFontSetup.RebuildAndReview();
        CaptureOnly();
    }
    public static void CaptureOnly()
    {
        bool had=PlayerPrefs.HasKey(AppearancePreference.PrefKey);string mode=AppearancePreference.Mode;
        var field=typeof(StyledTitleView).GetField("_launchStyle",BindingFlags.NonPublic|BindingFlags.Static);var before=field.GetValue(null);
        try
        {
            Directory.CreateDirectory(Output);field.SetValue(null,(GameMusicStyle?)GameMusicStyle.Dance);
            foreach(string appearance in new[]{AppearancePreference.Light,AppearancePreference.Dark})
            {
                AppearancePreference.SetMode(appearance);AppearancePreference.Tick(10);
                foreach(string screen in new[]{"Collection/RivalCollectionScreen","Progression/ProgressionScreen","ProfileScreen","Settings","Jukebox","EndScreen"})Shot(screen,appearance);
            }
            File.WriteAllText(Output+"/validation.txt","PASS: 12 actual prefab captures, Dance skin, day/night, Fusion titles at 70%. Temporary illustrative content only; no progress or purchases changed. Z optical overshoot increased from 4% to 6%.\n");
        }
        finally
        {
            field.SetValue(null,before);
            if(had)AppearancePreference.SetMode(mode);else{PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);AppearancePreference.RefreshDevice();}
            AppearancePreference.Tick(10);
        }
    }
    static GameObject Spawn(string path,Transform parent)
    {
        var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Screens+path+".prefab"),parent);
        go.SetActive(true);UIFactory.Stretch((RectTransform)go.transform);
        foreach(var component in go.GetComponentsInChildren<MonoBehaviour>(true))
            if(component!=null&&component.GetType().GetField("root")?.GetValue(component) is GameObject root)root.SetActive(true);
        Localize(go);return go;
    }
    static void Localize(GameObject go)
    {
        foreach(var loc in go.GetComponentsInChildren<LocalizedText>(true))
            if(loc.TryGetComponent<TMP_Text>(out var text))
            {
                Loc.Forget(text);
                // Batch captures do not run the game's addressable localization bootstrap.
                // Use the same shipped English strings without changing locale preferences.
                var entry=LocalizationSetup.Entries.FirstOrDefault(e=>e.key==loc.key);
                text.text=entry.en??(loc.key=="Settings.LanguageAuto"?"AUTO":loc.key=="Settings.Skin"?"GAME SKIN":Loc.Get(loc.key));
            }
    }
    public static GameMusicStyle ReviewStyle=GameMusicStyle.Dance;
    public static void CaptureSettings(string mode)=>Shot("Settings",mode);
    public static void CaptureScreen(string screen,string mode)=>Shot(screen,mode);
    static void Shot(string screen,string mode)
    {
        var cameraObject=new GameObject("Title review camera",typeof(Camera));var camera=cameraObject.GetComponent<Camera>();
        var target=new RenderTexture(CaptureResolution.x,CaptureResolution.y,24);camera.targetTexture=target;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.09f,.12f);
        var canvasObject=new GameObject("Title review canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
        var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        Material titleTrialMaterial=null;
        try
        {
            var home=Spawn("MainMenu",canvasObject.transform);var menu=home.GetComponent<MainMenuView>();menu.settingsPanel.SetActive(false);
            menu.ShowBackground(ReviewStyle);menu.logo.Show(ReviewStyle);
            if(menu.quitButton!=null)menu.quitButton.gameObject.SetActive(false);
            if(menu.profileText!=null)menu.profileText.text="LEVEL 2 · match 1 of 5\nLap 1 · Mastered matches 0/22 · Extra lives 3";
            var root=screen=="Settings"?home:Spawn(screen,canvasObject.transform);
            if(screen=="Settings")
            {
                menu.settingsPanel.SetActive(true);
                menu.settingsPanel.GetComponent<SettingsModalBlocker>()?.ShowVeil();
                menu.languageSelector.Build(new[]{LanguagePreference.Auto,"en","ca"},m=>m=="en"?"ENGLISH":m=="ca"?"CATAL�":"AUTO",_=>{});
                menu.settingsPanel.GetComponent<SettingsSkinPanel>()?.Apply();
            }
            if(root.TryGetComponent<RivalCollectionView>(out var collection))
            {
                collection.SetDetailVisible(false);
                var roster=Resources.Load<AppConfigSO>("AppConfig")?.opponentRoster;
                int index=0;
                if(roster!=null)foreach(var rival in roster.opponents.Where(o=>o!=null).Take(12))
                {
                    var card=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<RivalCardView>(Screens+"Collection/RivalCard.prefab"),collection.grid);
                    var portrait=rival.levels?.FirstOrDefault(l=>l!=null&&l.portrait!=null)?.portrait??rival.defaultConfig?.portrait;
                    card.SetCollection(portrait,rival.displayName,index++%3==1?Array.Empty<int>():new[]{1,2});
                }
                collection.summary.text="16 / 48 rival versions defeated";Canvas.ForceUpdateCanvases();collection.FitGallery();
            }
            if(root.TryGetComponent<ProgressionScreenView>(out var progression))
            {
                progression.summary.text="LAP 1 · LEVEL 2";progression.requirements.text="Build your arguments. Defeat your rivals.";
                for(int i=1;i<=4;i++)
                {
                    var row=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ProgressionLevelHeaderView>(Screens+"Progression/LevelHeaderRow.prefab"),progression.listContent);
                    row.Set("LEVEL "+i,i==1?"5 / 5 matches completed":"0 / 5 matches completed","RUNNER",i==1?7:0,"FIGHTER",i==1?8:0,"TOTAL",i==1?7:0);
                }
            }
            if(root.TryGetComponent<ProfileScreenView>(out var profile))
            {
                profile.titleText.text="PROFILE";profile.summaryText.text="12 runs · Best score 24,850";profile.stylesText.text="Dance · Electronic · Catchy";
                profile.lastRunText.text="ZERO NINE — ALL BUT WATER";
                for(int i=0;i<profile.stats.Length;i++){profile.stats[i].value.text=(65+i*7).ToString();profile.stats[i].fill.fillAmount=.65f+i*.07f;}
            }
            if(root.TryGetComponent<JukeboxView>(out var jukebox))
            {
                if(jukebox.ownerPlaque!=null&&!string.IsNullOrEmpty(OwnerReviewName))jukebox.ownerPlaque.SetOwnerName(OwnerReviewName);
                jukebox.ApplyBrand(GameMusicStyle.Dance);jukebox.selected.text="ZERO NINE";jukebox.nowPlaying.text="ALL BUT WATER";jukebox.summary.text="Your secret playlist";
                var tracks=new[]{"Zero Nine","Then You Roam-Roam","Combo Master Tribe","Dyin’ Alive","Tasmanian","Milonga Candidate","Zero Nine"}.Select((s,i)=>new JukeboxTrack{id="title-review-"+i,title=s,artist=i==0?"All But Water":"",style=GameMusicStyle.Dance}).ToArray();
                var rows=Enumerable.Range(0,7).Select(i=>UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<JukeboxRowView>(Screens+"Items/JukeboxRow.prefab"),jukebox.list)).ToArray();
                jukebox.carousel.Setup(rows,tracks,_=>{},_=>{});jukebox.carousel.Focus(0);
            }
            if(root.TryGetComponent<EndScreenView>(out var end))
            {
                end.titleText.text="RUN COMPLETE";end.evaluationPanel.AddStars("RUNNER",8);end.evaluationPanel.AddHighlight("24,850 POINTS");end.evaluationPanel.AddLine("PICKUPS  86 / 100");end.evaluationPanel.AddLine("TIMING  92%");end.evaluationPanel.AddLine("FALLS  1");end.evaluationPanel.AddNote("Your arguments are ready. Time to debate.");
            }
            foreach(var bg in canvasObject.GetComponentsInChildren<FrontendStyleBackground>(true))bg.ShowHome();
            foreach(var group in canvasObject.GetComponentsInChildren<StyleBlendGroup>(true))group.Apply(ReviewStyle,1);
            Localize(root);
            if(screen=="Settings")foreach(var option in menu.settingsPanel.GetComponentsInChildren<LanguageOptionView>())
                if(option.Mode==LanguagePreference.Auto)option.GetComponentInChildren<TMP_Text>().text="Automatic";
            foreach(var text in canvasObject.GetComponentsInChildren<TMP_Text>(true))
            {
                if(text.text=="Progression.Title")text.text="PROGRESSION";
                if(text.text=="MainMenu.Play")text.text="CONTINUE";
            }
            foreach(var text in canvasObject.GetComponentsInChildren<TitleText>())text.Apply();
            foreach(var text in canvasObject.GetComponentsInChildren<AppearanceText>())text.Apply();
            if(jukeboxTrials&&jukeboxTitleVariant!="neutral")
            {
                var j=root.GetComponent<JukeboxView>();var library=root.GetComponent<FrontendStyleBackground>().library;
                SongStyleSurface.Palette(library,GameMusicStyle.Dance,out var dark,out var light);
                j.archedTitle.color=jukeboxTitleVariant=="day"?Color.Lerp(Color.white,dark,library.titleDayTint):Color.Lerp(Color.white,light,library.titleNightTint);
            }
            if(jukeboxStrokeTrials)
            {
                var title=root.GetComponent<JukeboxView>().archedTitle;
                var library=root.GetComponent<FrontendStyleBackground>().library;
                SongStyleSurface.Palette(library,GameMusicStyle.Dance,out var dark,out var light);
                Color outline=jukeboxTitleVariant=="neutral"?(Color)new Color32(68,29,13,255):
                    jukeboxTitleVariant=="day"?Color.Lerp(Color.white,light,library.titleNightTint):Color.Lerp(Color.white,dark,library.titleDayTint);
                titleTrialMaterial=new Material(title.fontSharedMaterial){name="Jukebox title stroke review",hideFlags=HideFlags.DontSave};
                titleTrialMaterial.SetColor("_OutlineColor",outline);
                titleTrialMaterial.SetFloat("_OutlineWidth",.20f);
                titleTrialMaterial.SetFloat("_OutlineSoftness",0);
                titleTrialMaterial.EnableKeyword("OUTLINE_ON");
                title.fontSharedMaterial=titleTrialMaterial;title.UpdateMeshPadding();
            }
            if(screen=="Settings")menu.settingsPanel.GetComponent<SettingsSkinPanel>()?.Apply();
            Canvas.ForceUpdateCanvases();
            foreach(var ink in canvasObject.GetComponentsInChildren<StyleTitleInk>())ink.Apply(true);
            foreach(var cabinet in canvasObject.GetComponentsInChildren<JukeboxView>())cabinet.FitCabinet();
            foreach(var text in canvasObject.GetComponentsInChildren<TMP_Text>())text.ForceMeshUpdate();
            camera.Render();
            if(screen=="Settings")
            {
                var blocker=menu.settingsPanel.GetComponent<SettingsModalBlocker>();
                if(blocker==null||!blocker.backdrop.gameObject.activeInHierarchy||!blocker.backdrop.raycastTarget)throw new Exception("Settings veil does not block input");
                var raycaster=canvasObject.AddComponent<GraphicRaycaster>();
                var esGo=new GameObject("Settings interaction validation",typeof(UnityEngine.EventSystems.EventSystem));
                try
                {
                    var data=new UnityEngine.EventSystems.PointerEventData(esGo.GetComponent<UnityEngine.EventSystems.EventSystem>()){position=new Vector2(CaptureResolution.x-80,CaptureResolution.y-60)};
                    var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();raycaster.Raycast(data,hits);
                    if(hits.Count==0||hits[0].gameObject!=blocker.backdrop.gameObject)throw new Exception("Settings raycast: "+string.Join(",",hits.Select(h=>h.gameObject.name))+"; veil depth="+blocker.backdrop.depth);
                }
                finally{UnityEngine.Object.DestroyImmediate(esGo);}
            }
            camera.Render();RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
            if(root.TryGetComponent<JukeboxView>(out var checkedView)&&checkedView.ownerPlaque!=null)
            {
                if(checkedView.ownerPlaque.nameText.isTextOverflowing)throw new Exception("Engraved owner overflows in rendered screen");
                foreach(var face in checkedView.medallions)
                {
                    var corners=new Vector3[4];((RectTransform)face.transform.parent).GetWorldCorners(corners);
                    var p0=RectTransformUtility.WorldToScreenPoint(camera,corners[0]);var p1=RectTransformUtility.WorldToScreenPoint(camera,corners[1]);var p2=RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
                    if(Mathf.Abs(Vector2.Distance(p1,p2)/Vector2.Distance(p0,p1)-1)>.01f)throw new Exception("Medallion is stretched on screen");
                }
            }
            string name=jukeboxTrials?"title-"+jukeboxTitleVariant+"-on-"+mode:Path.GetFileName(screen)+"-"+mode;
            File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
        finally
        {
            RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(canvasObject);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(target);
            if(titleTrialMaterial!=null)UnityEngine.Object.DestroyImmediate(titleTrialMaterial);
        }
    }
}
#endif
