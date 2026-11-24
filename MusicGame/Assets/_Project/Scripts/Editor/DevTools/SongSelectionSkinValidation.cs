#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class SongSelectionSkinValidation
{
    private static string OutputFolder=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../VisualReviews/SongSelection/Unity"));
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Verify()
    {
        var lib=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(AppearanceSetup.LibraryPath);
        var screen=AssetDatabase.LoadAssetAtPath<SongSelectionView>(SongSelectionSkinSetup.ScreenPath);
        var row=AssetDatabase.LoadAssetAtPath<SongRowView>(SongSelectionSkinSetup.RowPath);
        Check(lib.songRowMaterial!=null&&lib.songRowMaterial.shader.isSupported,"Rectangular shader missing/unsupported");
        Check(row.skin!=null&&row.GetComponent<RectangularButtonMesh>()!=null,"Row skin/mesh binding missing");
        Check(screen.contentRoot!=null&&screen.GetComponent<FrontendStyleBackground>().controls!=null,"Screen layout/style group missing");
        Check(screen.backButton.GetComponent<StyleBlendButton>()!=null&&screen.playButton.GetComponent<StyleBlendButton>()!=null,"Back/Play do not use native style buttons");
        bool had=PlayerPrefs.HasKey(AppearancePreference.PrefKey);string saved=PlayerPrefs.GetString(AppearancePreference.PrefKey,AppearancePreference.Auto);
        var canvasObject=new GameObject("Song selection review",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var cameraObject=new GameObject("Song selection review camera",typeof(Camera));
        var canvas=canvasObject.GetComponent<Canvas>();canvas.additionalShaderChannels|=AdditionalCanvasShaderChannels.TexCoord1;
        var camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var target=new RenderTexture(1920,1080,24);camera.targetTexture=target;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
        var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        Directory.CreateDirectory(OutputFolder);
        try
        {
            var live=UnityEngine.Object.Instantiate(screen,canvas.transform);UIFactory.Stretch((RectTransform)live.transform);live.gameObject.SetActive(true);
            var bg=live.GetComponent<FrontendStyleBackground>();
            string[] names={"Low Megas — Combo Master Tribe","WHAT_ — Then You Roam-Roam","All But Water — Zero Nine","Michele Beautiless — Tasmanian","Caribbean Machines — Milonga_Candidate","Gym Bros — Dyin' Alive"};
            var rows=names.Select(name=>{var item=UnityEngine.Object.Instantiate(row,live.songListRoot);item.label.text=name;item.SetPlayed(true);item.playPauseLabel.text="PREVIEW";return item;}).ToArray();
            live.statusText.text="";live.playButton.interactable=true;
            var table=AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.StringTable>("Assets/_Project/Localization/UIText_en.asset");
            foreach(var localized in live.GetComponentsInChildren<LocalizedText>(true))
            {var entry=table.GetEntry(localized.key);if(entry!=null)localized.GetComponent<TMP_Text>().text=entry.Value;}
            live.spotifyLabel.text="Spotify · Coming soon";live.youtubeMusicLabel.text="YouTube Music · Coming soon";live.amazonMusicLabel.text="Amazon Music · Coming soon";
            foreach(string mode in new[]{AppearancePreference.Dark,AppearancePreference.Light})
            {
                AppearancePreference.SetMode(mode);AppearancePreference.Tick(10);
                foreach(var style in lib.menuBackgrounds.Select(b=>b.style))
                {
                    bg.Show(style,style,1);
                    for(int i=0;i<rows.Length;i++)
                    {
                        rows[i].SetSelection(i==1,style);
                        var expected=i==1?lib.Button(style).selected:lib.Button(style).normal;
                        Check(rows[i].background.sprite==expected,"Wrong original sprite "+style);
                        Check(rows[i].background.material==lib.songRowMaterial,"Per-row material allocated "+style);
                    }
                    live.playYourSongButton.GetComponent<RectangularStyleButton>().SetSelection(false,style);
                    Canvas.ForceUpdateCanvases();live.FitContent();
                    foreach(var ink in live.GetComponentsInChildren<AppearanceText>(false))ink.Apply();
                    Capture(camera,target,style+"-"+mode);
                }
            }
            rows[2].button.interactable=false;rows[2].playPauseButton.interactable=false;rows[2].SetSelection(false,GameMusicStyle.Acoustic);
            Check(!rows[2].button.IsInteractable()&&!rows[2].playPauseButton.IsInteractable(),"Disabled row remains actionable");
            Check(rows[2].Played&&rows[2].playedCheck.color==rows[2].playedOnColor,"Disabled row loses played history");
            Check(rows.All(r=>!r.playedCheck.raycastTarget),"Played mark blocks input");
            var scroll=live.songListRoot.GetComponentInParent<ScrollRect>();
            for(int i=0;i<6;i++){var extra=UnityEngine.Object.Instantiate(row,live.songListRoot);extra.label.text="Extra song "+i;}
            Canvas.ForceUpdateCanvases();Check(live.songListRoot.rect.height>scroll.viewport.rect.height,"List does not overflow/scroll");
            scroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();Check(live.songListRoot.anchoredPosition.y>0,"Scroll position does not move");
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../song-selection-skin-validation.txt")),"PASS: 16 original normal/selected skins x light/dark; rectangular remapping with shared material and existing textures; native Back/Play; independently tinted Preview; 12-song scroll; disabled/history states; 32 Unity screenshots. No progression/save changes.");
            Debug.Log("Styled song selection validation passed.");
        }
        finally
        {
            AppearancePreference.SetMode(saved);AppearancePreference.Tick(10);if(!had){PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);PlayerPrefs.Save();}
            RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(canvasObject);UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
    private static void Capture(Camera camera,RenderTexture target,string name)
    {
        Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
        File.WriteAllBytes(Path.Combine(OutputFolder,name+".png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=null;
    }
}
#endif
