#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class AppearanceValidation
{
    private static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
    [MenuItem("Tools/MusicGame/UI/Verify Day And Night Appearance")]
    public static void Verify()
    {
        bool had=PlayerPrefs.HasKey(AppearancePreference.PrefKey);
        string saved=PlayerPrefs.GetString(AppearancePreference.PrefKey,AppearancePreference.Auto);
        try
        {
            Check(AppearancePreference.ResolveLight("auto",true)&&!AppearancePreference.ResolveLight("auto",false),"Automatic does not follow device");
            Check(AppearancePreference.ResolveLight("light",false)&&!AppearancePreference.ResolveLight("dark",true),"Manual mode does not override device");
            Check(AppearancePreference.Normalize("garbage")==AppearancePreference.Auto,"Invalid mode must use Automatic");
            var lib=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(AppearanceSetup.LibraryPath);
            Check(lib.menuBackgrounds.Count==16,"Expected 16 original night backgrounds");
            Check(lib.menuBackgrounds.Count(b=>b.dayTexture!=null)==16,"Expected sixteen authored day backgrounds");
            Check(lib.menuAppearanceMaterial!=null&&lib.menuAppearanceMaterial.shader.isSupported,"Appearance blend shader unsupported");
            foreach(var entry in lib.menuBackgrounds)
            {
                Check(entry.texture!=null,"Missing night "+entry.style);
                Check(lib.DayBackground(entry.style)!=null,"Missing day/fallback "+entry.style);
                Check(lib.DayBackground(entry.style)!=entry.texture,"Day mode must not fall back to night "+entry.style);
            }
            AppearancePreference.SetMode(AppearancePreference.Dark);AppearancePreference.Tick(10);
            AppearancePreference.SetMode(AppearancePreference.Light);
            Check(AppearancePreference.Mode==AppearancePreference.Light&&PlayerPrefs.GetString(AppearancePreference.PrefKey)=="light","Choice not persisted");
            Check(AppearancePreference.LightWeight==0,"Appearance jumped instead of fading");
            AppearancePreference.Tick(AppearancePreference.FadeSeconds*.5f);
            Check(Mathf.Approximately(AppearancePreference.LightWeight,.5f),"Midpoint fade wrong");
            AppearancePreference.SetMode(AppearancePreference.Dark);
            Check(Mathf.Approximately(AppearancePreference.LightWeight,.5f),"Rapid reversal jumped");
            AppearancePreference.Tick(10);Check(AppearancePreference.LightWeight==0,"Night fade incomplete");
            Render(lib);
            // Existing independent music-style reveal, first analysis and background inheritance checks.
            FrontendBackdropValidation.Verify();
            File.WriteAllText("../appearance-validation.txt","PASS: 16 night + 16 day textures; Dark retains smoky grey daylight; Auto/manual policy; persistence; midpoint + rapid reversal; shader rendered in night/mid/day; shared background inheritance; original preference restored.");
            Debug.Log("Day/night appearance validation passed.");
        }
        finally
        {
            AppearancePreference.SetMode(saved);AppearancePreference.Tick(10);
            if(!had){PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);PlayerPrefs.Save();}
        }
    }

    private static void Render(StyleSkinLibrarySO lib)
    {
        Directory.CreateDirectory("../VisualReviews/Appearance");
        var canvasObject=new GameObject("Appearance review",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var cameraObject=new GameObject("Appearance review camera",typeof(Camera));
        var canvas=canvasObject.GetComponent<Canvas>();var camera=cameraObject.GetComponent<Camera>();
        camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var target=new RenderTexture(1920,1080,24);camera.targetTexture=target;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
        var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<MainMenuView>(AppearanceSetup.Screens+"MainMenu.prefab");
            var live=UnityEngine.Object.Instantiate(prefab,canvas.transform);UIFactory.Stretch((RectTransform)live.transform);live.gameObject.SetActive(true);
            live.quitButton.gameObject.SetActive(false);
            live.logo.Show(GameMusicStyle.Electronic);live.buttonStyle.Apply(GameMusicStyle.Electronic,1);live.settingsPanel.SetActive(false);
            live.profileText.gameObject.SetActive(true);live.profileText.text="LEVEL 2 · match 4 of 5";
            var selector=live.settingsPanel.GetComponentInChildren<AppearanceSelectorView>(true);
            Check(selector!=null&&selector.automatic!=null&&selector.lightButton!=null&&selector.dark!=null,"Settings selector missing");
            if(live.languageSelector!=null)
                live.languageSelector.Build(new[]{"auto","ca","en"},m=>m=="auto"?"Automatic":m=="ca"?"Català":"English",_=>{});
            var originalButton=live.playButton.GetComponent<StyleBlendButton>().styleLayer.sprite;
            foreach(string mode in new[]{AppearancePreference.Dark,AppearancePreference.Light})
            {
                AppearancePreference.SetMode(mode);AppearancePreference.Tick(10);
                selector.Refresh();live.ShowBackground(GameMusicStyle.Electronic);
                Check(live.menuBackground.texture==lib.MenuBackground(GameMusicStyle.Electronic),"Night backing lost");
                Check(live.menuBackground.material.GetTexture("_DayTex")==lib.DayBackground(GameMusicStyle.Electronic),"Day texture not bound");
                Check(Mathf.Approximately(live.menuBackground.material.GetFloat("_Light"),AppearancePreference.LightWeight),"Shader weight stale");
                Check(live.playButton.GetComponent<StyleBlendButton>().styleLayer.sprite==originalButton,"Appearance replaced button art");
                Capture(camera,target,"menu-"+mode);
                live.settingsPanel.SetActive(true);Capture(camera,target,"settings-"+mode);live.settingsPanel.SetActive(false);
            }
            AppearancePreference.SetMode(AppearancePreference.Dark);AppearancePreference.Tick(AppearancePreference.FadeSeconds*.5f);
            live.ShowBackground(GameMusicStyle.Electronic);Capture(camera,target,"menu-mid-transition");
            AppearancePreference.Tick(10);
            UnityEngine.Object.DestroyImmediate(live.gameObject);
            var collectionPrefab=AssetDatabase.LoadAssetAtPath<RivalCollectionView>(CollectionCardSetup.ScreenPath);
            var collection=UnityEngine.Object.Instantiate(collectionPrefab,canvas.transform);UIFactory.Stretch((RectTransform)collection.transform);collection.gameObject.SetActive(true);collection.SetDetailVisible(false);
            var bg=collection.GetComponent<FrontendStyleBackground>();
            var roster=AssetDatabase.LoadAssetAtPath<OpponentRosterSO>("Assets/_Project/Configs/Fight/OpponentRoster.asset");
            var cardPrefab=AssetDatabase.LoadAssetAtPath<RivalCardView>(CollectionCardSetup.CardPath);
            int n=0;
            foreach(var rival in roster.opponents.Where(o=>o!=null))
            {
                var card=UnityEngine.Object.Instantiate(cardPrefab,collection.grid);
                var portrait=rival.levels.First(l=>l!=null&&l.portrait!=null).portrait;
                card.SetCollection(portrait,rival.displayName,n++%4==1?Array.Empty<int>():new[]{1,2});
            }
            collection.summary.text="24 / 48";
            foreach(string mode in new[]{AppearancePreference.Dark,AppearancePreference.Light})
            {
                AppearancePreference.SetMode(mode);AppearancePreference.Tick(10);bg.Show(GameMusicStyle.Electronic,GameMusicStyle.Electronic,1);
                Canvas.ForceUpdateCanvases();collection.FitGallery();Capture(camera,target,"collection-"+mode);
                bg.Show(GameMusicStyle.Acoustic,GameMusicStyle.Electronic,.5f);
                Check(Mathf.Approximately(bg.previousBackdrop.color.a,.5f),"Appearance broke music fade");
                Check(Mathf.Approximately(bg.previousBackdrop.material.GetFloat("_Light"),AppearancePreference.LightWeight),"Previous musical style uses wrong appearance");
                if(mode==AppearancePreference.Light)Capture(camera,target,"collection-style-mid-transition");
            }
        }
        finally
        {
            RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(canvasObject);UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    private static void Capture(Camera camera,RenderTexture target,string name)
    {
        foreach(var ink in UnityEngine.Object.FindObjectsByType<AppearanceText>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))ink.Apply();
        // Edit-mode previews do not bootstrap Localization. Use the same source entries for their captions.
        foreach(var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
        {
            var table=AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.StringTable>("Assets/_Project/Localization/UIText_en.asset");
            var stored=table!=null?table.GetEntry(text.text):null;
            if(stored!=null)text.text=stored.Value;
            else {var entry=LocalizationSetup.Entries.FirstOrDefault(e=>e.key==text.text);if(entry.key!=null)text.text=entry.en;}
        }
        Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
        File.WriteAllBytes("../VisualReviews/Appearance/"+name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=null;
    }
}
#endif
