#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad]
public static class SettingsOpenPlayProbe
{
    const string Pending="SettingsOpen.PlayTest";
    static MainMenuView view;
    static int stage;
    static double at;
    static bool midpoint;
    static SettingsOpenPlayProbe(){EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=Changed;EditorApplication.delayCall+=ReportIfFinished;}
    static void ReportIfFinished()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||!SessionState.GetBool(Pending,false))return;
        var result=SessionState.GetString(Pending+"Result","");if(string.IsNullOrEmpty(result))return;
        File.WriteAllText("../settings-open-play.txt",result);SessionState.SetBool(Pending,false);
        var scene=SessionState.GetString(Pending+"Scene","");if(!string.IsNullOrEmpty(scene)&&File.Exists(scene))EditorSceneManager.OpenScene(scene);
    }
    public static void Begin()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var setup=EditorSceneManager.GetSceneManagerSetup();SessionState.SetString(Pending+"Scene",setup.Length>0?setup[0].path:"");
        SessionState.SetBool(Pending+"SkinHad",PlayerPrefs.HasKey(StyledTitleView.SkinPreferenceKey));SessionState.SetInt(Pending+"Skin",PlayerPrefs.GetInt(StyledTitleView.SkinPreferenceKey,-1));
        SessionState.SetBool(Pending+"AppearanceHad",PlayerPrefs.HasKey(AppearancePreference.PrefKey));SessionState.SetString(Pending+"Appearance",AppearancePreference.Mode);
        SessionState.SetBool(Pending+"LangHad",PlayerPrefs.HasKey(LanguagePreference.PrefKey));SessionState.SetString(Pending+"Lang",PlayerPrefs.GetString(LanguagePreference.PrefKey,LanguagePreference.Auto));
        SessionState.SetBool(Pending,true);SessionState.SetString(Pending+"Result","");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.delayCall+=ReportIfFinished;return;}
        if(!SessionState.GetBool(Pending,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            try
            {
                view=null;stage=-1;at=EditorApplication.timeSinceStartup;midpoint=false;
            }
            catch(Exception e){Finish("FAIL: "+e);}
        }
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            var saved=SessionState.GetString(Pending+"Scene","");if(!string.IsNullOrEmpty(saved))EditorSceneManager.OpenScene(saved);
            File.WriteAllText("../settings-open-play.txt",SessionState.GetString(Pending+"Result","FAIL: Missing result"));SessionState.SetBool(Pending,false);
        }
    }
    static void Poll()
    {
        if(!EditorApplication.isPlaying||!SessionState.GetBool(Pending,false))return;
        try
        {
            var age=EditorApplication.timeSinceStartup-at;if(age>20)throw new Exception("Settings transition timeout");
            if(stage==-1)
            {
                if(AppBootstrap.Context?.AppFlow.CurrentState!=GameFlowState.MainMenu){at=EditorApplication.timeSinceStartup;return;}
                if(age<3)return;
                var controller=UnityEngine.Object.FindFirstObjectByType<MainMenuController>();if(controller==null)return;
                view=(MainMenuView)typeof(MainMenuController).GetField("_view",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
                view.settingsButton.onClick.Invoke();
                var chooser=view.settingsPanel.GetComponentInChildren<SettingsSkinSelector>();var expected=chooser.Adjacent(1);chooser.next.onClick.Invoke();
                Check(StyledTitleView.MenuStyle(view.logo.library)==expected,"Next arrow callback failed");Check(PlayerPrefs.GetInt(StyledTitleView.SkinPreferenceKey)==(int)expected,"Selected skin not saved");
                var language=LanguagePreference.Mode;view.languageSelector.next.onClick.Invoke();Check(LanguagePreference.Mode!=language,"Language next callback failed");view.languageSelector.previous.onClick.Invoke();Check(LanguagePreference.Mode==language,"Language previous callback failed");
                // Interrupt twice while still blending; the final target must finish with fully opaque art.
                chooser.next.onClick.Invoke();chooser.next.onClick.Invoke();
                stage=0;at=EditorApplication.timeSinceStartup;return;
            }
            if(view==null)return;
            float blend=(float)typeof(MainMenuView).GetField("_backgroundBlend",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(view);
            if(stage==0)
            {
                if(blend>0&&blend<1)midpoint=true;
                if(age>2){Check(midpoint&&Mathf.Approximately(blend,1),"Skin transition did not animate or finish");
                    Check(Mathf.Approximately(view.presentationRoot.GetComponent<CanvasGroup>().alpha,1),"Modal changes the opacity of layered home artwork");
                    Check(Mathf.Approximately(view.logo.GetComponent<CanvasGroup>().alpha,1),"Logo transition left translucent lettering");
                    foreach(var button in view.buttonStyle.GetComponentsInChildren<StyleBlendButton>(true))if(button.styleLayer!=null&&button.styleLayer.enabled)Check(!button.classicLayer.enabled,"Classic art remains behind completed style transition");
                    view.settingsPanel.GetComponentInChildren<AppearanceSelectorView>().dark.onClick.Invoke();stage=1;at=EditorApplication.timeSinceStartup;}
            }
            else if(stage==1&&AppearancePreference.LightWeight==0)
            {view.settingsPanel.GetComponentInChildren<AppearanceSelectorView>().lightButton.onClick.Invoke();midpoint=false;stage=2;}
            else if(stage==2)
            {
                float light=AppearancePreference.LightWeight;if(light>0&&light<1)midpoint=true;
                if(light==1)
                {
                    Check(midpoint,"Day/night failed to transition");view.closeButton.onClick.Invoke();
                    Check(!view.settingsPanel.activeSelf,"Back failed to close Settings");Check(Mathf.Approximately(view.presentationRoot.GetComponent<CanvasGroup>().alpha,1),"Menu did not restore after closing");
                    Finish("PASS: rapid repeated skin-arrow callbacks finish with full-opacity logo/menu art and no classic layer; language arrow callbacks cycle and persist; skin persists; day/night fades; BACK restores menu. Original preferences and scene restored.\n");
                }
            }
        }
        catch(Exception e){Finish("FAIL: "+e);}
    }
    static void Finish(string result)
    {
        SessionState.SetString(Pending+"Result",result);
        if(SessionState.GetBool(Pending+"SkinHad",false))PlayerPrefs.SetInt(StyledTitleView.SkinPreferenceKey,SessionState.GetInt(Pending+"Skin",-1));else PlayerPrefs.DeleteKey(StyledTitleView.SkinPreferenceKey);
        if(SessionState.GetBool(Pending+"LangHad",false))PlayerPrefs.SetString(LanguagePreference.PrefKey,SessionState.GetString(Pending+"Lang",LanguagePreference.Auto));else PlayerPrefs.DeleteKey(LanguagePreference.PrefKey);
        AppearancePreference.SetMode(SessionState.GetString(Pending+"Appearance",AppearancePreference.Auto));if(!SessionState.GetBool(Pending+"AppearanceHad",false))PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);PlayerPrefs.Save();
        EditorApplication.ExitPlaymode();
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
#endif
