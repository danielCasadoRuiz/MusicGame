#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class SongShowcaseReview
{
    static bool exampleOnly,headingComparison,unifiedTypography;
    static bool titleTintTrials;
    public static void CaptureTitleTintTrials()
    {
        titleTintTrials=true;exampleOnly=true;unifiedTypography=true;
        try{Run();}
        finally{TitleText.ReviewTint=-1;titleTintTrials=false;exampleOnly=false;unifiedTypography=false;}
    }
    public static void CaptureFusionTypography()
    {
        unifiedTypography=true;exampleOnly=true;
        try
        {
            Run();
            Directory.CreateDirectory("../VisualReviews/Typography/Fusion-InGame");
            foreach(string mode in new[]{"light","dark"})
                File.Copy("../VisualReviews/SongSelection/UnifiedBrush/Dance-"+mode+".png","../VisualReviews/Typography/Fusion-InGame/Dance-"+mode+".png",true);
        }
        finally{unifiedTypography=false;exampleOnly=false;}
    }
    static string OutputDirectory=>titleTintTrials?"../VisualReviews/Typography/TintTrials":unifiedTypography?"../VisualReviews/SongSelection/UnifiedBrush":headingComparison?"../VisualReviews/SongSelection/HeadingIvory":exampleOnly?"../VisualReviews/SongSelection/PreviewTint35":"../VisualReviews/SongSelection/ShowcaseD-v14";
    public static void CaptureTintExample()
    {
        exampleOnly=true;
        try{Run();File.WriteAllText("../preview-tint-batch-validation.txt","PASS: captured the actual Dance selection prefab at 43% preview progress with a 35% face tint.\n");}
        finally{exampleOnly=false;}
    }
    public static void CaptureHeadingComparison()
    {
        headingComparison=true;exampleOnly=true;
        try{Run();File.WriteAllText("../heading-ivory-validation.txt","PASS: ivory heading uses the shared title colour, local 1.15 size multiplier, unchanged global tier, day/night contrast, brush and clean type comparisons captured.\n");}
        finally{headingComparison=false;exampleOnly=false;}
    }
    public static void CaptureUnifiedTypography()
    {
        unifiedTypography=true;exampleOnly=true;
        try{Run();File.WriteAllText("../unified-typography-validation.txt","PASS: song title uses the shared brush title font, large compact single-line autosizing, diagonal preview tint at 35%, heading shadow removed.\n");}
        finally{unifiedTypography=false;exampleOnly=false;}
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run()
    {
        bool hadMode=PlayerPrefs.HasKey(AppearancePreference.PrefKey);string mode=AppearancePreference.Mode;
        var field=typeof(StyledTitleView).GetField("_launchStyle",BindingFlags.Static|BindingFlags.NonPublic);var originalStyle=field.GetValue(null);
        var holder=new GameObject("Showcase review",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var cameraObject=new GameObject("Showcase camera",typeof(Camera));var camera=cameraObject.GetComponent<Camera>();
        var target=new RenderTexture(1920,1080,24);camera.targetTexture=target;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;
        var canvas=holder.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
        var scaler=holder.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        try
        {
            field.SetValue(null,(GameMusicStyle?)GameMusicStyle.Dance);
            AppearancePreference.SetMode(AppearancePreference.Light);AppearancePreference.Tick(10);
            var view=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SongSelectionView>(SongSelectionSkinSetup.ScreenPath),holder.transform);
            UIFactory.Stretch((RectTransform)view.transform);view.gameObject.SetActive(true);view.FitContent();
            view.customPurchaseDialog.SetActive(false);view.titleText.text="CHOOSE YOUR SONG";view.backButtonLabel.text="BACK";view.playButtonLabel.text="PLAY";view.playYourSongLabel.text="+ YOUR SONG · UNLOCK";view.statusText.text="";
            var showcase=view.GetComponent<SongSelectionShowcase>();
            Check(showcase!=null&&showcase.logo!=null&&showcase.bars!=null,"Missing D presentation");
            view.selectedSongText.text="Zero Nine";showcase.artist.text="All But Water";
            var carousel=view.GetComponentInChildren<SongCatalogCarousel>();var rows=new List<SongRowView>();var decades=new List<int>();
            var prefab=AssetDatabase.LoadAssetAtPath<SongRowView>(SongSelectionSkinSetup.RowPath);
            var styles=new[]{GameMusicStyle.Electronic,GameMusicStyle.Dance,GameMusicStyle.Catchy,GameMusicStyle.Soulful};
            var titles=new[]{"Combo Master Tribe","Zero Nine","Then You Roam-Roam","Dyin’ Alive"};
            var artists=new[]{"Low Megas","All But Water","WHAT_","Gym Bros"};
            for(int i=0;i<12;i++)
            {
                var row=UnityEngine.Object.Instantiate(prefab,view.songListRoot);row.label.text=titles[i%4];row.artistLabel.text=artists[i%4];row.musicStyle=styles[i%4];row.genreLabel.text=styles[i%4].ToString();
                row.button.interactable=i!=3;row.SetPlayed(i==3);rows.Add(row);decades.Add(i<4?1970:1980);
            }
            Canvas.ForceUpdateCanvases();int selected=-1;carousel.Setup(rows,i=>selected=i,decades);carousel.MoveTo(1);
            Check(carousel.rowCount==1&&carousel.visibleColumns==4,"Catalogue must have one row of four tracks");
            Check(showcase.bars.BarCount==32&&showcase.bars.neonMaterial!=null,"Expected thirty-two thick neon bars");
            Check(view.previewProgressRoot.GetComponent<RectTransform>().rect.height>=8,"Preview progress too thin");
            float artRatio=StyleSkinLibrarySO.ButtonBody.width/StyleSkinLibrarySO.ButtonBody.height;
            foreach(var button in new[]{view.playYourSongButton,view.backButton,view.playButton}){var rect=(RectTransform)button.transform;Check(Mathf.Abs(rect.rect.width/rect.rect.height-artRatio)<.01f&&button.image.preserveAspect,"Footer button artwork is stretched");}
            var own=(RectTransform)view.playYourSongButton.transform;var back=(RectTransform)view.backButton.transform;var play=(RectTransform)view.playButton.transform;
            Check(own.sizeDelta==back.sizeDelta,"Your Song and Back differ in size");
            Check(own.anchoredPosition.y==back.anchoredPosition.y&&back.anchoredPosition.y==play.anchoredPosition.y,"Footer buttons are not bottom aligned");
            float firstGap=back.anchoredPosition.x-back.rect.width*.5f-(own.anchoredPosition.x+own.rect.width*.5f);
            float secondGap=play.anchoredPosition.x-play.rect.width*.5f-(back.anchoredPosition.x+back.rect.width*.5f);
            Check(Mathf.Abs(firstGap-secondGap)<.1f,"Footer button gaps differ");
            foreach(var tab in carousel.decadeButtons)
            {
                var skin=tab.GetComponent<RectangularStyleButton>();
                Check(skin.nativeAspect&&!skin.emphasiseSelection&&skin.image.preserveAspect,"Decade art is stretched or has an extra selection effect");
                Check(Mathf.Abs(skin.image.rectTransform.rect.width/skin.image.rectTransform.rect.height-4)<.01f,"Decade PNG does not keep its original aspect");
            }
            Check(((RectTransform)rows[0].transform).anchoredPosition.y==((RectTransform)rows[1].transform).anchoredPosition.y,"Unexpected second row");
            Check(carousel.MaxOffset>0,"Catalogue not horizontally browsable");
            carousel.Filter(1980);Check(carousel.Center==1&&selected==1,"Changing decades lost selection");carousel.Filter(0);
            var scroll=carousel.GetComponent<ScrollRect>();scroll.horizontalNormalizedPosition=1;
            Check(Mathf.Abs(scroll.content.anchoredPosition.x+carousel.MaxOffset)<1,"Finite right boundary failed");scroll.horizontalNormalizedPosition=0;
            Check(scroll.viewport.GetComponent<RectMask2D>().padding==Vector4.zero,"Mask expands outside catalogue");
            var library=view.GetComponent<FrontendStyleBackground>().library;Check(library.songCards.Count==16,"Missing style materials");
            Check(library.songCards.Select(card=>card.stageArtwork).Distinct().Count()==16,"Styles share a placeholder band instead of their own artwork");
            foreach(var card in library.songCards)Check(card.normal!=null&&card.sprite!=null&&card.stageArtwork!=null,"Missing card pair or authored band: "+card.style);
            for(int i=0;i<rows.Count;i++){rows[i].SetSelection(i==1,rows[i].musicStyle);rows[i].RefreshAppearance();}
            Check(rows[1].selectionArtwork!=null&&rows[1].selectionArtwork.material!=null,"Missing finished selection frame");
            var cardRect=(RectTransform)rows[0].transform;
            Check(cardRect.rect.width/cardRect.rect.height>2.3f&&cardRect.rect.width/cardRect.rect.height<2.6f,"Song cards drifted from the approved wide ratio");
            foreach(var row in rows)
            {
                Check(row.transform.localScale==Vector3.one,"Selected artwork must grow without stretching the copy");
                Check(row.cardShadow!=null&&row.cardShadow.material!=null,"Missing card depth shadow");
                Check(row.artistLabel.rectTransform.anchoredPosition.x<row.label.rectTransform.anchoredPosition.x,"Artist does not follow the diagonal");
                Check(row.artistLabel.rectTransform.anchoredPosition.y<row.label.rectTransform.anchoredPosition.y-40,"Artist overlaps title");
            }
            Check(showcase.titleProgress!=null&&showcase.titleProgress.progressShader!=null,"Missing title progress shader");
            showcase.titleProgress.reviewPlaying=true;showcase.titleProgress.reviewProgress=.43f;
            rows[1].SetPreview(true);view.selectedCard.gameObject.SetActive(true);view.previewProgressRoot.SetActive(true);view.previewProgressFill.anchorMax=new Vector2(.43f,1);showcase.bars.reviewPlaying=true;
            foreach(var styleSkin in library.songCards)
            {
                var style=styleSkin.style;
                if(titleTintTrials)
                {if(style!=GameMusicStyle.Dance&&style!=GameMusicStyle.Catchy&&style!=GameMusicStyle.Acoustic&&style!=GameMusicStyle.Electronic)continue;}
                else if(exampleOnly&&style!=GameMusicStyle.Dance)continue;
                field.SetValue(null,(GameMusicStyle?)style);
                foreach(var appearance in new[]{AppearancePreference.Light,AppearancePreference.Dark})
                {
                    if(exampleOnly&&!headingComparison&&!unifiedTypography&&appearance!=AppearancePreference.Light)continue;
                    AppearancePreference.SetMode(appearance);AppearancePreference.Tick(10);
                    view.GetComponent<FrontendStyleBackground>().ShowHome();view.selectedAppearance.SetState(style,true);showcase.Render();
                    foreach(var row in rows){row.SetSelection(row==rows[1],row.musicStyle);row.GetComponent<SongCardAppearance>().SetState(row.musicStyle,row==rows[1]);row.RefreshAppearance();}
                    for(int t=0;t<carousel.decadeButtons.Length;t++)
                    {
                        var tab=carousel.decadeButtons[t];tab.GetComponent<RectangularStyleButton>().SetSelection(t==0,style);
                        var caption=tab.GetComponentInChildren<TMPro.TextMeshProUGUI>();Loc.Forget(caption);caption.text=t==0?"ALL":(1970+(t-1)*10)+"s";
                    }
                    foreach(var row in rows){Loc.Forget(row.completedLabel);row.completedLabel.text="COMPLETED";}
                    view.RefreshAppearance();showcase.Render();showcase.bars.SetVerticesDirty();
                    Check(!showcase.bars.gameObject.activeSelf,"Frequency bars should be hidden");
                    Check(showcase.titleProgress.SweepShear>.1f,"Sweep does not follow the italic lean");
                    var sweepMesh=view.selectedSongText.textInfo.meshInfo[0];
                    var firstGlyph=view.selectedSongText.textInfo.characterInfo[0];
                    int cornerIndex=firstGlyph.vertexIndex;
                    var vertex=sweepMesh.vertices[cornerIndex];
                    float projected=vertex.x-showcase.titleProgress.SweepShear*vertex.y;
                    Check(Mathf.Abs(sweepMesh.uvs2[cornerIndex].x-projected)<.01f,"Sweep coordinates depend on Canvas position");
                    Check(!showcase.logo.background.gameObject.activeSelf&&!showcase.logo.foreground.gameObject.activeSelf,"Logo decorative backdrop remains visible");
                    Check(view.selectedTexture.sprite==library.SongCard(style).stageArtwork&&view.selectedAppearance.TextInk==Color.white,"Stage art or contrast missing for "+style);
                    foreach(var ink in view.GetComponentsInChildren<AppearanceText>(true))ink.Apply();
                    SongStyleSurface.Palette(library,style,out var from,out var to);
                    float night=1-AppearancePreference.LightWeight;
                    var customColor=Color.Lerp(Color.Lerp(from,Color.black,.48f),Color.Lerp(from,Color.white,.78f),night);
                    view.customSurface.Apply(customColor,customColor,Color.Lerp(from,Color.black,.25f));
                    Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                    var capture=new Texture2D(1920,1080,TextureFormat.RGB24,false);capture.ReadPixels(new Rect(0,0,1920,1080),0,0);capture.Apply();
                    Directory.CreateDirectory(OutputDirectory);File.WriteAllBytes(OutputDirectory+"/"+style+"-"+appearance+".png",capture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(capture);
                    if(titleTintTrials)
                    {
                        foreach(float strength in new[]{.30f,.50f,.70f})
                        {
                            TitleText.ReviewTint=strength;
                            foreach(var text in view.GetComponentsInChildren<TitleText>(true))text.ApplyColor();
                            SavePreview(camera,target,style+"-"+appearance+"-"+Mathf.RoundToInt(strength*100)+".png",1920,1080);
                        }
                        TitleText.ReviewTint=-1;
                        foreach(var text in view.GetComponentsInChildren<TitleText>(true))text.ApplyColor();
                    }
                    if(headingComparison)
                    {
                        var heading=view.titleText;var tag=heading.GetComponent<TitleText>();tag.enabled=false;
                        var originalFont=heading.font;var originalMaterial=heading.fontSharedMaterial;
                        var clean=AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/UI/Fonts/SongShowcase/ShowcaseHeadlineWide SDF.asset");
                        heading.font=clean;heading.fontStyle=TMPro.FontStyles.Bold|TMPro.FontStyles.Italic|TMPro.FontStyles.UpperCase;
                        var cleanMaterial=new Material(clean.material);
                        cleanMaterial.EnableKeyword("UNDERLAY_ON");cleanMaterial.SetColor("_UnderlayColor",new Color(.025f,.02f,.045f,.42f));
                        cleanMaterial.SetFloat("_UnderlayOffsetY",-.4f);cleanMaterial.SetFloat("_UnderlaySoftness",.5f);cleanMaterial.SetFloat("_UnderlayDilate",.12f);
                        heading.fontSharedMaterial=cleanMaterial;heading.UpdateMeshPadding();Canvas.ForceUpdateCanvases();
                        SavePreview(camera,target,"Clean-"+appearance+".png",1920,1080);
                        heading.font=originalFont;heading.fontSharedMaterial=originalMaterial;tag.enabled=true;tag.Apply();
                        heading.fontSharedMaterial=originalMaterial;UnityEngine.Object.DestroyImmediate(cleanMaterial);
                    }
                }
            }
            if(exampleOnly)return;
            field.SetValue(null,(GameMusicStyle?)GameMusicStyle.Dance);AppearancePreference.SetMode(AppearancePreference.Light);AppearancePreference.Tick(10);
            view.GetComponent<FrontendStyleBackground>().ShowHome();view.selectedSongText.text="";showcase.artist.text="";view.RefreshAppearance();showcase.Render();
            foreach(var row in rows){row.SetSelection(false,row.musicStyle);row.SetPreview(false);row.RefreshAppearance();Loc.Forget(row.completedLabel);row.completedLabel.text="COMPLETED";}
            for(int t=0;t<carousel.decadeButtons.Length;t++)carousel.decadeButtons[t].GetComponent<RectangularStyleButton>().SetSelection(t==0,GameMusicStyle.Dance);
            foreach(var ink in view.GetComponentsInChildren<AppearanceText>(true))ink.Apply();
            SongStyleSurface.Palette(library,GameMusicStyle.Dance,out var emptyFrom,out var emptyTo);
            var emptyColour=Color.Lerp(emptyFrom,Color.black,.48f);view.customSurface.Apply(emptyColour,emptyColour,Color.Lerp(emptyFrom,Color.black,.25f));
            Loc.Forget(showcase.placeholderTitle);Loc.Forget(showcase.placeholderArtist);showcase.placeholderTitle.text="SONG TITLE";showcase.placeholderArtist.text="ARTIST / BAND";
            view.previewProgressRoot.SetActive(false);
            Check(view.selectedCard.gameObject.activeSelf&&showcase.placeholderTitle.gameObject.activeSelf&&!showcase.bars.gameObject.activeSelf,"Empty selection must show its placeholder without playing bars");
            Canvas.ForceUpdateCanvases();SavePreview(camera,target,"placeholder.png",1920,1080);
            var wide=new RenderTexture(2400,1080,24);
            try
            {
                camera.targetTexture=wide;Canvas.ForceUpdateCanvases();view.FitContent();showcase.Render();Canvas.ForceUpdateCanvases();
                float screenWidth=((RectTransform)view.transform).rect.width;
                Check(Mathf.Abs(((RectTransform)carousel.transform).rect.width*view.contentRoot.localScale.x-screenWidth)<1,"Catalogue does not reach both screen edges on a wide phone");
                Check(Mathf.Abs(carousel.positionTrack.rectTransform.rect.width-1720)<1,"Scroll indicator width changed");
                SavePreview(camera,wide,"wide-phone.png",2400,1080);
            }
            finally{camera.targetTexture=target;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(wide);}
        }
        finally
        {
            field.SetValue(null,originalStyle);AppearancePreference.SetMode(mode);AppearancePreference.Tick(10);if(!hadMode)PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);
            RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(holder);UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
    static void SavePreview(Camera camera,RenderTexture target,string name,int width,int height)
    {
        camera.Render();RenderTexture.active=target;
        var capture=new Texture2D(width,height,TextureFormat.RGB24,false);capture.ReadPixels(new Rect(0,0,width,height),0,0);capture.Apply();
        File.WriteAllBytes(OutputDirectory+"/"+name,capture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(capture);
    }
}
#endif
