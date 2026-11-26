#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
public static class SongSelectionPolishReview
{
    const string Output="../VisualReviews/SongSelection/GeometryAndInk";
    static void Check(bool v,string msg){if(!v)throw new Exception(msg);}
    public static void Run()
    {
        bool had=PlayerPrefs.HasKey(AppearancePreference.PrefKey);string mode=AppearancePreference.Mode;
        var field=typeof(StyledTitleView).GetField("_launchStyle",BindingFlags.Static|BindingFlags.NonPublic);var prior=field.GetValue(null);
        var cameraGo=new GameObject("Song layout camera",typeof(Camera));var camera=cameraGo.GetComponent<Camera>();camera.orthographic=true;
        var target=new RenderTexture(1920,1080,24);camera.targetTexture=target;
        var canvasGo=new GameObject("Song layout capture",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
        var scaler=canvasGo.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        var notes=new List<string>();
        try
        {
            Directory.CreateDirectory(Output);
            var view=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SongSelectionView>(SongSelectionSkinSetup.ScreenPath),canvasGo.transform);UIFactory.Stretch((RectTransform)view.transform);view.gameObject.SetActive(true);view.FitContent();
            view.customPurchaseDialog.SetActive(false);view.titleText.text="CHOOSE YOUR SONG";view.backButtonLabel.text="BACK";view.playButtonLabel.text="PLAY";view.playYourSongLabel.text="+ YOUR SONG · UNLOCK";view.statusText.text="";
            var showcase=view.GetComponent<SongSelectionShowcase>();view.selectedSongText.text="Zero Nine";showcase.artist.text="All But Water";
            var carousel=view.GetComponentInChildren<SongCatalogCarousel>(true);var rows=new List<SongRowView>();
            string[] names={"Zero Nine","Dyin’ Alive","Then You Roam-Roam","Combo Master Tribe"},artists={"All But Water","Gym Bros","WHAT_","Low Megas"};
            GameMusicStyle[] styles={GameMusicStyle.Groove,GameMusicStyle.Soulful,GameMusicStyle.Catchy,GameMusicStyle.Electronic};
            for(int i=0;i<8;i++){var row=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SongRowView>(SongSelectionSkinSetup.RowPath),view.songListRoot);row.label.text=names[i%4];row.artistLabel.text=artists[i%4];row.musicStyle=styles[i%4];row.genreLabel.text=styles[i%4].ToString();rows.Add(row);}
            Canvas.ForceUpdateCanvases();carousel.Setup(rows,_=>{},Enumerable.Repeat(1970,8).ToArray());carousel.MoveTo(0);
            foreach(var style in new[]{GameMusicStyle.Groove,GameMusicStyle.Dance,GameMusicStyle.Electronic,GameMusicStyle.Acoustic})
            {
                field.SetValue(null,(GameMusicStyle?)style);
                foreach(var appearance in new[]{AppearancePreference.Light,AppearancePreference.Dark})
                {
                    AppearancePreference.SetMode(appearance);AppearancePreference.Tick(10);view.GetComponent<FrontendStyleBackground>().ShowHome();
                    foreach(var button in new[]{view.backButton,view.playButton})button.GetComponent<RectangularStyleButton>()?.SetSelection(false,style);
                    foreach(var row in rows){row.SetSelection(row==rows[0],row.musicStyle);row.RefreshAppearance();}
                    for(int i=0;i<carousel.decadeButtons.Length;i++){carousel.decadeButtons[i].GetComponent<RectangularStyleButton>().SetSelection(i==0,style);var t=carousel.decadeButtons[i].GetComponentInChildren<TMP_Text>();Loc.Forget(t);t.text=i==0?"ALL":(1970+(i-1)*10)+"s";}
                    view.RefreshAppearance();showcase.Render();view.titleText.GetComponent<TitleText>().ApplyColor();
                    Canvas.ForceUpdateCanvases();foreach(var row in rows)row.RefreshAppearance();
                    ValidateGeometry(rows[0]);ValidateGeometry(rows[1]);ValidateFooter(view);
                    var lib=view.GetComponent<FrontendStyleBackground>().library;Color ink=lib.SongHeadingColor(style,appearance==AppearancePreference.Light);
                    Check(Vector4.Distance(view.titleText.color,ink)<.002f,"Heading does not use sampled texture colour");
                    notes.Add(style+" "+appearance+" title "+ColorUtility.ToHtmlStringRGB(ink));
                    Save(camera,target,style+"-"+appearance+".png");
                }
            }
            VerifyPause(view.GetComponent<FrontendStyleBackground>().library,canvasGo.transform,notes);
            File.WriteAllText("../song-selection-polish-validation.txt","PASS: selected/unselected aspect and edge slopes match; frame, shadow and surface match; Back / Your Song / Play ordering, identical CTA sizes, equal footer gaps and opacity; extended custom-button endcaps; sampled texture heading palette; Runner and Fighter pause titles use song rather than menu style.\n"+string.Join("\n",notes));
        }
        finally
        {
            field.SetValue(null,prior);AppearancePreference.SetMode(mode);AppearancePreference.Tick(10);if(!had)PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);
            RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(canvasGo);UnityEngine.Object.DestroyImmediate(cameraGo);
        }
    }
    static void ValidateGeometry(SongRowView row)
    {
        var r=(RectTransform)row.transform;var surface=row.background.rectTransform;var mesh=row.background.GetComponent<SongCardSurfaceMesh>();
        Check(Mathf.Abs(surface.rect.width/surface.rect.height-r.rect.width/r.rect.height)<.001f,"Selected card changes aspect ratio");
        Check(Mathf.Abs(mesh.slant/surface.rect.height-row.cardSlant/r.rect.height)<.001f,"Diagonal slopes differ between states");
        var frame=(RectTransform)row.selectionFrame.transform;Check(Vector2.Distance(frame.offsetMin,surface.offsetMin)<.001f&&Vector2.Distance(frame.offsetMax,surface.offsetMax)<.001f,"Frame does not follow surface bounds");
        if(row.skin.Selected)using(var vertices=new VertexHelper())
        {
            var outline=row.selectionArtwork.GetComponent<SongNeonStroke>();
            SongNeonStroke.Quad(vertices,outline.GetComponent<RectTransform>().rect,outline.padding,outline.slant,Color.white);var v=UIVertex.simpleVert;vertices.PopulateUIVertex(ref v,0);
            float encoded=v.uv1.x-v.uv1.y+2*v.uv1.w-2*v.uv1.z;Check(Mathf.Abs(encoded-mesh.slant)<.01f,"Frame shader receives a different diagonal");
        }
        Check(Mathf.Abs(row.cardShadow.GetComponent<SongNeonStroke>().slant-mesh.slant)<.001f,"Shadow diagonal differs");
    }
    static void ValidateFooter(SongSelectionView view)
    {
        var a=(RectTransform)view.playYourSongButton.transform;var b=(RectTransform)view.backButton.transform;var c=(RectTransform)view.playButton.transform;
        Check(Vector2.Distance(a.rect.size,c.rect.size)<.001f,"Your Song must match Play size");
        Check(Mathf.Abs(a.anchoredPosition.y-b.anchoredPosition.y)<.001f&&Mathf.Abs(c.anchoredPosition.y-b.anchoredPosition.y)<.001f,"Footer bottoms differ");
        float first=a.anchoredPosition.x-a.rect.width/2-(b.anchoredPosition.x+b.rect.width/2),second=c.anchoredPosition.x-c.rect.width/2-(a.anchoredPosition.x+a.rect.width/2);
        Check(Mathf.Abs(first-second)<.01f&&Mathf.Abs(first-32)<.01f,"Footer gaps differ");Check(view.backButton.GetComponent<CanvasGroup>().alpha==1,"Back remains dimmed");
        Check(b.anchoredPosition.x<a.anchoredPosition.x&&a.anchoredPosition.x<c.anchoredPosition.x,"Footer order must be Back / Your Song / Play");Check(view.playYourSongButton.GetComponent<StyleBlendButton>().styleLayer.material!=null,"CTA accent missing");Check(view.playYourSongButton.GetComponentsInChildren<StyleButtonStretchMesh>(true).Length==2,"Missing fixed endcaps");
    }
    static void VerifyPause(StyleSkinLibrarySO lib,Transform parent,List<string> notes)
    {
        var f=typeof(GameSession).GetField("<Instance>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic);var old=f.GetValue(null);var go=new GameObject("Pause style fixture");var session=go.AddComponent<GameSession>();f.SetValue(null,session);session.DetectedGameStyle=GameMusicStyle.Electronic;
        try
        {
            foreach(var path in new[]{"PauseMenu","FightHud"})
            {
                var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/"+path+".prefab"),parent);
                try
                {
                    var overlay=path=="PauseMenu"?root.GetComponent<PauseView>().overlayRoot:root.GetComponent<FightHudView>().pausePanel;
                    foreach(var title in overlay.GetComponentsInChildren<TitleText>(true))
                    {
                        title.ApplyColor();SongStyleSurface.Palette(lib,GameMusicStyle.Electronic,out var dark,out var light);
                        float day=AppearancePreference.LightWeight;var expected=UIStylePalette.ForStyle(lib,GameMusicStyle.Electronic,day);
                        expected.a=title.GetComponent<TMP_Text>().color.a;Check(Vector4.Distance(expected,title.GetComponent<TMP_Text>().color)<.005f,"Pause still uses home skin in "+path);
                    }
                    notes.Add(path+": Electronic song overrides Acoustic home skin");
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
        }
        finally{f.SetValue(null,old);UnityEngine.Object.DestroyImmediate(go);}
    }
    static void Save(Camera camera,RenderTexture target,string name)
    {
        camera.Render();RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Output+"/"+name,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=null;
    }
}
#endif
