using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ContentStartupSmokeCheck
{
    const string Active="ContentStartupSmoke.Active";
    static ContentStartupSmokeCheck(){if(SessionState.GetBool(Active,false))EditorApplication.update+=Tick;}
    [InitializeOnEnterPlayMode]
    static void ProtectSave(EnterPlayModeOptions options){if(SessionState.GetBool(Active,false))PlayerProgressStore.SuspendWrites=true;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ProtectRuntimeSave(){if(SessionState.GetBool(Active,false))PlayerProgressStore.SuspendWrites=true;}
    public static void Run()
    {
        SessionState.SetBool(Active,true);SessionState.SetFloat("ContentStartupSmoke.Start",(float)EditorApplication.timeSinceStartup);
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");EditorApplication.isPlaying=true;
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Active,false))return;
        double elapsed=EditorApplication.timeSinceStartup-SessionState.GetFloat("ContentStartupSmoke.Start",0);
        if(EditorApplication.isPlaying)
        {
            PlayerProgressStore.SuspendWrites=true;
            bool ready=ContentDownloadManager.Instance!=null && ContentDownloadManager.Instance.IsInitialized && SceneManager.GetSceneByName("Frontend").isLoaded;
            if(ready && elapsed>35){Finish(true,"Core booted, content manager initialized, Frontend loaded, survived 35+ seconds without native crash. Save writes suspended.");return;}
        }
        if(elapsed>100)Finish(false,"Startup timed out before reaching initialized Frontend");
    }
    static void Finish(bool ok,string detail)
    {
        SessionState.SetBool(Active,false);EditorApplication.update-=Tick;
        Directory.CreateDirectory("Logs");File.WriteAllText("Logs/ContentStartupSmokeCheck.txt",(ok?"PASS: ":"FAIL: ")+detail+" "+DateTime.UtcNow.ToString("O"));
        Debug.Log(File.ReadAllText("Logs/ContentStartupSmokeCheck.txt"));EditorApplication.isPlaying=false;
        EditorApplication.delayCall+=()=>EditorApplication.Exit(ok?0:1);
    }
}
