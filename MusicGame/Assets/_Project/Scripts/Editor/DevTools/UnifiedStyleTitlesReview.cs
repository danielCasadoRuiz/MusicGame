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
public static class UnifiedStyleTitlesReview
{
    const string Root="Assets/_Project/Prefabs/UI/";
    const string Output="../VisualReviews/UnifiedStyleTitles";
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Run()
    {
        var lib=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(AppearanceSetup.LibraryPath);
        var launch=typeof(StyledTitleView).GetField("_launchStyle",BindingFlags.Static|BindingFlags.NonPublic);var oldLaunch=launch.GetValue(null);
        var sessionField=typeof(GameSession).GetField("<Instance>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic);var oldSession=sessionField.GetValue(null);
        var sessionGo=new GameObject("Unified style test song");var song=sessionGo.AddComponent<GameSession>();sessionField.SetValue(null,song);
        bool hadMode=PlayerPrefs.HasKey(AppearancePreference.PrefKey);string oldMode=AppearancePreference.Mode;
        var notes=new List<string>();int checks=0;
        try
        {
            Directory.CreateDirectory(Output);launch.SetValue(null,(GameMusicStyle?)GameMusicStyle.Groove);song.DetectedGameStyle=GameMusicStyle.Electronic;
            foreach(string mode in new[]{AppearancePreference.Light,AppearancePreference.Dark})
            {
                AppearancePreference.SetMode(mode);AppearancePreference.Tick(10);
                foreach(string screen in new[]{"MainMenu","SongSelection","Progression/ProgressionScreen","Collection/RivalCollectionScreen","ProfileScreen","Jukebox","CountdownScreen","RoundIntro","RoundEnd","FinishBanner","EndScreen","FightHud","MatchResult","VersusScreen","OpponentSelection","LiveHud","PauseMenu"})
                {
                    var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+screen+".prefab"));
                    try
                    {
                        var scope=root.GetComponent<UIStyleScope>();Check(scope!=null,"Screen missing style domain: "+screen);
                        bool game=scope.FollowsSong;var expectedStyle=game?song.DetectedGameStyle:GameMusicStyle.Groove;
                        var menu=root.GetComponent<MainMenuView>();if(menu!=null){menu.logo.Show(GameMusicStyle.Groove);menu.ShowBackground(GameMusicStyle.Groove);}
                        root.GetComponent<FrontendStyleBackground>()?.ShowHome();
                        foreach(var title in root.GetComponentsInChildren<TitleText>(true))
                        {
                            if(!title.enabled)continue;
                            var text=title.GetComponent<TMP_Text>();title.ApplyColor();var expected=UIStylePalette.ForStyle(lib,expectedStyle,AppearancePreference.LightWeight);expected.a=text.color.a;
                            Check(Vector4.Distance(text.color,expected)<.005f,screen+" title follows wrong style: "+title.name);checks++;
                            var faded=text.color;faded.a=.37f;text.color=faded;title.ApplyColor();Check(Mathf.Abs(text.color.a-.37f)<.001f,"Title tint resets animation alpha");faded.a=1;text.color=faded;
                        }
                        foreach(var title in root.GetComponentsInChildren<StyleTitleInk>(true)){title.Apply();var expected=UIStylePalette.ForStyle(lib,expectedStyle,AppearancePreference.LightWeight);Check(Vector3.Distance(new Vector3(title.GetComponent<TMP_Text>().color.r,title.GetComponent<TMP_Text>().color.g,title.GetComponent<TMP_Text>().color.b),new Vector3(expected.r,expected.g,expected.b))<.005f,"Colour-only title mismatch in "+screen);checks++;}
                        if(menu!=null){menu.settingsPanel.GetComponent<SettingsSkinPanel>().Apply();Check(Vector4.Distance(menu.settingsTitleText.color,UIStylePalette.ForStyle(lib,GameMusicStyle.Groove,AppearancePreference.LightWeight))<.005f,"Settings still uses neutral ink");}
                        if(root.TryGetComponent<CountdownScreenView>(out var count))foreach(string word in new[]{"3","2","1","TEMPO!"}){count.numberText.text=word;count.numberText.GetComponent<TitleText>().ApplyColor();Check(Vector4.Distance(count.numberText.color,UIStylePalette.ForStyle(lib,GameMusicStyle.Electronic,AppearancePreference.LightWeight))<.005f,"Runner countdown uses home style");}
                        if(root.TryGetComponent<RoundIntroView>(out var round))foreach(string word in new[]{"ROUND 1","3","2","1","FURY!"}){round.text.text=word;round.text.GetComponent<TitleText>().ApplyColor();Check(Vector4.Distance(round.text.color,UIStylePalette.ForStyle(lib,GameMusicStyle.Electronic,AppearancePreference.LightWeight))<.005f,"Fight countdown uses home style");}
                        notes.Add(screen+" "+mode+": "+expectedStyle);
                    }
                    finally{UnityEngine.Object.DestroyImmediate(root);}
                }
                TitleScreensReview.ReviewStyle=GameMusicStyle.Groove;TitleScreensReview.OutputOverride=Output+"/Groove";Directory.CreateDirectory(TitleScreensReview.OutputOverride);
                foreach(string screen in new[]{"Progression/ProgressionScreen","Collection/RivalCollectionScreen","ProfileScreen","Settings","Jukebox"})TitleScreensReview.CaptureScreen(screen,mode);
                CaptureAnnouncements(lib,mode);
            }
            Check(StyledTitleView.MenuStyle(lib)==GameMusicStyle.Groove,"Song recognition overwrote the initial menu skin");
            song.DetectedGameStyle=GameMusicStyle.Acoustic;Check(StyledTitleView.MenuStyle(lib)==GameMusicStyle.Groove,"Changing songs overwrote menu skin");
            File.WriteAllText("../unified-style-titles-validation.txt","PASS: "+checks+" production title checks, two appearance modes, explicit menu/song scopes; Settings uses the shared palette; both countdown sequences follow the song; animation alpha preserved; changing detected songs leaves the initial menu skin unchanged.\n"+string.Join("\n",notes));
        }
        finally
        {
            TitleScreensReview.OutputOverride=null;TitleScreensReview.ReviewStyle=GameMusicStyle.Dance;sessionField.SetValue(null,oldSession);UnityEngine.Object.DestroyImmediate(sessionGo);launch.SetValue(null,oldLaunch);
            AppearancePreference.SetMode(oldMode);AppearancePreference.Tick(10);if(!hadMode)PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);
        }
    }
    static void CaptureAnnouncements(StyleSkinLibrarySO lib,string mode)
    {
        var cameraGo=new GameObject("Announcement review camera",typeof(Camera));var camera=cameraGo.GetComponent<Camera>();camera.orthographic=true;camera.backgroundColor=mode==AppearancePreference.Light?new Color(.88f,.91f,.96f):new Color(.025f,.035f,.065f);camera.clearFlags=CameraClearFlags.SolidColor;
        var target=new RenderTexture(1920,1080,24);camera.targetTexture=target;
        var canvasGo=new GameObject("Announcement review",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;var scaler=canvasGo.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        try
        {
            foreach(string name in new[]{"CountdownScreen","RoundIntro"})
            {
                var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+name+".prefab"),canvasGo.transform);UIFactory.Stretch((RectTransform)root.transform);root.SetActive(true);
                var text=name=="CountdownScreen"?root.GetComponent<CountdownScreenView>().numberText:root.GetComponent<RoundIntroView>().text;
                foreach(string word in name=="CountdownScreen"?new[]{"3","TEMPO!"}:new[]{"ROUND 1","FURY!"})
                {
                    text.text=word;text.GetComponent<TitleText>().Apply();Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                    var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Output+"/"+name+"-"+word.Replace("!","").Replace(" ","-")+"-"+mode+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                }
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        finally{RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(canvasGo);UnityEngine.Object.DestroyImmediate(cameraGo);}
    }
}
#endif
