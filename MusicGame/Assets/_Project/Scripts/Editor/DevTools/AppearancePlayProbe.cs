#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Real Play-mode smoke check: actual Settings listeners and the unscaled runtime fade driver.</summary>
[InitializeOnLoad]
public static class AppearancePlayProbe
{
    private const string Pending="Appearance.PlayProbe", Restore="Appearance.PlayProbe.Restore";
    private static MainMenuView view;
    private static AppearanceSelectorView selector;
    private static int stage;
    private static double started;
    private static bool midpoint;
    static AppearancePlayProbe()
    {
        EditorApplication.playModeStateChanged+=Changed;
        EditorApplication.update+=Poll;
    }
    public static void Run()
    {
        SessionState.SetBool("Appearance.PlayProbe.HadPref",PlayerPrefs.HasKey(AppearancePreference.PrefKey));
        SessionState.SetString("Appearance.PlayProbe.Pref",PlayerPrefs.GetString(AppearancePreference.PrefKey,AppearancePreference.Auto));
        SessionState.SetString("Appearance.PlayProbe.Result","");
        SessionState.SetBool(Pending,true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    private static void Changed(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Pending,false))
        {
            try
            {
                started=EditorApplication.timeSinceStartup;stage=0;
                AppearancePreference.Updated+=Observe;
                var canvasObject=new GameObject("Appearance live test",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
                canvasObject.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
                var prefab=AssetDatabase.LoadAssetAtPath<MainMenuView>(AppearanceSetup.Screens+"MainMenu.prefab");
                view=UnityEngine.Object.Instantiate(prefab,canvasObject.transform);
                UIFactory.Stretch((RectTransform)view.transform);view.gameObject.SetActive(true);
                view.ShowBackground(GameMusicStyle.Electronic);view.settingsPanel.SetActive(true);
                selector=view.settingsPanel.GetComponentInChildren<AppearanceSelectorView>();
                selector.dark.onClick.Invoke();
                Check(AppearancePreference.Mode==AppearancePreference.Dark,"Dark Settings callback missing");
                Check(UnityEngine.Object.FindObjectsByType<AppearancePreferenceDriver>(FindObjectsSortMode.None).Length==1,"Need one runtime appearance driver");
            }
            catch(Exception e){Finish("FAIL: "+e);}
        }
        if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(Restore,false))
        {
            SessionState.SetBool(Restore,false);
            string result=SessionState.GetString("Appearance.PlayProbe.Result","FAIL: Missing result");
            File.WriteAllText("../appearance-play-validation.txt",result);
            Debug.Log(result);EditorApplication.Exit(result.StartsWith("PASS")?0:1);
        }
    }
    private static void Poll()
    {
        if(!EditorApplication.isPlaying||!SessionState.GetBool(Pending,false)||view==null)return;
        try
        {
            if(EditorApplication.timeSinceStartup-started>35)throw new Exception("Timed out waiting for runtime transition");
            float w=AppearancePreference.LightWeight;
            if(stage==0&&w==0)
            {
                midpoint=false;selector.lightButton.onClick.Invoke();
                Check(AppearancePreference.Mode==AppearancePreference.Light&&AppearancePreference.LightWeight==0,"Light callback jumps or is missing");
                stage=1;
            }
            else if(stage==1)
            {
                if(w>0&&w<1)midpoint=true;
                if(w==1)
                {
                    Check(midpoint,"Runtime fade never produced intermediate frames");
                    Check(Mathf.Approximately(view.menuBackground.material.GetFloat("_Light"),1),"Visible menu did not follow runtime fade");
                    selector.dark.onClick.Invoke();stage=2;
                }
            }
            else if(stage==2&&w>0&&w<1)
            {
                selector.lightButton.onClick.Invoke();
                Check(Mathf.Approximately(w,AppearancePreference.LightWeight),"Rapid Settings reversal jumped");stage=3;
            }
            else if(stage==3&&w==1)
            {
                view.settingsPanel.SetActive(false);view.gameObject.SetActive(false);view.gameObject.SetActive(true);
                Check(Mathf.Approximately(view.menuBackground.material.GetFloat("_Light"),1),"Reopening loses appearance");
                var prefab=AssetDatabase.LoadAssetAtPath<RivalCollectionView>(CollectionCardSetup.ScreenPath);
                var collection=UnityEngine.Object.Instantiate(prefab,view.transform.parent);
                collection.gameObject.SetActive(true);collection.SetDetailVisible(false);
                var bg=collection.GetComponent<FrontendStyleBackground>();bg.Show(GameMusicStyle.Electronic,GameMusicStyle.Electronic,1);
                Check(Mathf.Approximately(bg.backdrop.material.GetFloat("_Light"),1),"New Collection does not inherit light mode");
                bg.Show(GameMusicStyle.Acoustic,GameMusicStyle.Electronic,.5f);
                Check(Mathf.Approximately(bg.previousBackdrop.color.a,.5f),"Live music-style fade broken");
                Finish("PASS: actual Play-mode Settings callbacks; one runtime driver; visible intermediate fade frames; endpoint shader update; rapid reversal; reopening; new Collection appearance inheritance and independent music-style fade. Original preference restored.");
            }
        }
        catch(Exception e){Finish("FAIL: "+e);}
    }
    private static void Observe()
    {
        float w=AppearancePreference.LightWeight;
        if(stage==1 && w>0 && w<1)midpoint=true;
        if(stage==2 && w>0 && w<1)
        {
            selector.lightButton.onClick.Invoke();
            Check(Mathf.Approximately(w,AppearancePreference.LightWeight),"Rapid reversal jumped");stage=3;
        }
    }
    private static void Finish(string result)
    {
        AppearancePreference.Updated-=Observe;
        SessionState.SetBool(Pending,false);SessionState.SetBool(Restore,true);
        SessionState.SetString("Appearance.PlayProbe.Result",result);
        string pref=SessionState.GetString("Appearance.PlayProbe.Pref",AppearancePreference.Auto);
        AppearancePreference.SetMode(pref);
        if(!SessionState.GetBool("Appearance.PlayProbe.HadPref",false)){PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);PlayerPrefs.Save();}
        EditorApplication.ExitPlaymode();
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
#endif
