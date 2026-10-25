#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Isolated play-mode integration tests. Does not boot Core or read/write player saves.</summary>
[InitializeOnLoad]
public static class ArenaSceneLifecycleProbe
{
    const string Active="ArenaSceneProbe.Active", OldBuilder="ArenaSceneProbe.Builder";
    static IEnumerator<bool> routine;
    static readonly List<string> report=new();
    static double deadline;
    static ArenaSceneLifecycleProbe() { if(SessionState.GetBool(Active,false)) EditorApplication.update+=Tick; }
    public static void RunFromCommandLine()
    {
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        SessionState.SetInt(OldBuilder,settings.ActivePlayModeDataBuilderIndex);
        int fast=settings.DataBuilders.FindIndex(b=>b!=null && b.GetType().Name=="BuildScriptFastMode");
        if(fast<0) throw new Exception("Addressables fast-mode provider not available");
        settings.ActivePlayModeDataBuilderIndex=fast; EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Assets/_Project/Scenes/Debug");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene,"Assets/_Project/Scenes/Debug/ArenaSceneLifecycleProbe.unity");
        SessionState.SetBool(Active,true); EditorApplication.update+=Tick; EditorApplication.isPlaying=true;
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying) return;
        try
        {
            if(routine==null) { deadline=EditorApplication.timeSinceStartup+300; routine=Run(); }
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Probe timed out");
            if(!routine.MoveNext()) Finish(0);
        }
        catch(Exception e) { report.Add("FAIL "+e); Finish(1); }
    }
    static void Check(bool condition,string message)
    { if(!condition) throw new Exception(message); report.Add("PASS "+message); }
    static IEnumerable<bool> Wait(Task task)
    { while(!task.IsCompleted) yield return true; if(task.IsFaulted) throw task.Exception; }
    static ArenaEnvironmentLoader Loader(out GameObject owner)
    {
        owner=new GameObject("ArenaLoaderProbe");
        var floor=new GameObject("Placeholder"); floor.transform.SetParent(owner.transform);
        var loader=owner.AddComponent<ArenaEnvironmentLoader>(); loader.Initialize(floor); return loader;
    }
    static bool AnyArenaScene()
    {
        for(int i=0;i<SceneManager.sceneCount;i++) if(SceneManager.GetSceneAt(i).name.StartsWith("Monteverdi_Arena_01_")) return true;
        return false;
    }
    static IEnumerator<bool> Run()
    {
        yield return true;
        Check(AppBootstrap.Context==null,"isolated debug scene; no Core bootstrap or player-save writes");
        var catalog=ArenaSceneCatalog.Current;
        var source=new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(ArenaSceneSetup.Master));
        var identity=SceneManager.GetActiveScene();
        int originalQuality=QualitySettings.GetQualityLevel();
        var sparse=ScriptableObject.CreateInstance<ArenaSceneCatalog>();
        sparse.entries.Add(new ArenaSceneCatalog.Entry {sourceArenaGuid=source.AssetGUID,variants=catalog.Find(source.AssetGUID).variants.Where(v=>v.qualityName=="Low" || v.qualityName=="Ultra").ToList()});
        sparse.Resolve(source.AssetGUID,ArenaSceneSetup.Qualities,3,out var fallback);
        Check(fallback=="Low","missing exact quality falls back below requested quality using existing resolver rule");
        sparse.entries[0].variants.RemoveAll(v=>v.qualityName=="Low");
        sparse.Resolve(source.AssetGUID,ArenaSceneSetup.Qualities,0,out fallback);
        Check(fallback=="Ultra","when no lower variant exists, uses lowest available higher variant");
        Object.Destroy(sparse);
        foreach(string quality in ArenaSceneSetup.Qualities)
        {
            int index=QualityAssetResolver.IndexOf(QualitySettings.names,quality); QualitySettings.SetQualityLevel(index,true);
            var loader=Loader(out var owner); loader.Request(source,"probe/"+quality);
            foreach(var step in Wait(loader.LoadingTask)) yield return step;
            Check(loader.IsReady && loader.LoadedQuality==quality && loader.LoadedScene.isLoaded,"loads requested "+quality+" scene");
            Check(ArenaEnvironmentLoader.LiveSceneHandles==1,"exactly one owned scene handle for "+quality);
            Check(loader.Instance.GetComponentsInChildren<MonteverdiPropQuality>(true).Length==0,"no five-quality selectors in runtime "+quality);
            var loaded=loader.LoadedScene; int instance=loader.Instance.GetInstanceID();
            loader.Request(source,"probe/"+quality);
            Check(loader.Instance.GetInstanceID()==instance && loader.LoadedScene==loaded,"same arena request reuses scene "+quality);
            foreach(var step in Wait(loader.ShutdownAsync())) yield return step;
            Object.Destroy(owner); yield return true;
            Check(ArenaEnvironmentLoader.LiveSceneHandles==0 && !AnyArenaScene(),"unload releases "+quality+" and leaves no arena scene");
            Check(SceneManager.GetActiveScene()==identity,"restores owning scene after "+quality);
        }
        // The newest request wins while an older scene is still loading.
        {
            var loader=Loader(out var owner);
            QualitySettings.SetQualityLevel(QualityAssetResolver.IndexOf(QualitySettings.names,"Low"),true);
            loader.Request(source,"race/low");
            QualitySettings.SetQualityLevel(QualityAssetResolver.IndexOf(QualitySettings.names,"Ultra"),true);
            loader.Request(source,"race/ultra");
            foreach(var step in Wait(loader.LoadingTask)) yield return step;
            Check(loader.LoadedQuality=="Ultra" && ArenaEnvironmentLoader.LiveSceneHandles==1,"superseded load releases stale result and keeps only latest quality");
            foreach(var step in Wait(loader.ShutdownAsync())) yield return step;
            Object.Destroy(owner); yield return true;
        }
        {
            var loader=Loader(out var owner); loader.Request(source,"cancel/ultra");
            var shutdown=loader.ShutdownAsync(); Check(ReferenceEquals(shutdown,loader.ShutdownAsync()),"shutdown is idempotent during a pending scene load");
            foreach(var step in Wait(shutdown)) yield return step;
            Object.Destroy(owner); yield return true;
            Check(ArenaEnvironmentLoader.LiveSceneHandles==0 && !AnyArenaScene(),"exit during loading leaves no owned handles or orphan arena scenes");
        }
        {
            var legacy=new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID("Assets/_Project/Art/Arenas/Monteverdi/Monteverdi_Arena_02.prefab"));
            var loader=Loader(out var owner); loader.Request(legacy,"legacy/02");
            foreach(var step in Wait(loader.LoadingTask)) yield return step;
            Check(loader.IsReady && loader.Instance!=null && !loader.LoadedScene.IsValid(),"unconverted arenas still load through legacy prefab path");
            foreach(var step in Wait(loader.ShutdownAsync())) yield return step;
            Object.Destroy(owner); yield return true;
        }
        // Exercise the actual Fight bootstrap with production recipes, without loading the app/save service.
        QualitySettings.SetQualityLevel(QualityAssetResolver.IndexOf(QualitySettings.names,"Low"),true);
        var sessionObject=new GameObject("ProbeSession"); var session=sessionObject.AddComponent<GameSession>();
        var app=Resources.Load<AppConfigSO>("AppConfig");
        session.SelectedOpponent=app.opponentRoster.opponents.First(o=>o!=null && o.id=="monteverdi");
        session.SelectedOpponentLevelConfig=session.SelectedOpponent.GetConfigForTier(1);
        session.SelectedOpponentTier=1; Time.timeScale=0;
        var fightLoad=SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Fight.unity",LoadSceneMode.Additive);
        while(!fightLoad.isDone) yield return true;
        var boot=FightSceneBootstrap.Instance;
        while(boot==null) { yield return true; boot=FightSceneBootstrap.Instance; }
        bool premature=false;
        while(!boot.StageReady)
        {
            if(boot.ArenaLoader!=null && !boot.ArenaLoader.IsReady && (boot.PlayerActor!=null || boot.OpponentActor!=null)) premature=true;
            yield return true;
        }
        Check(!premature,"real Fight bootstrap never spawns fighters before arena readiness");
        Check(boot.PlayerActor.gameObject.scene==boot.ArenaLoader.LoadedScene && boot.OpponentActor.gameObject.scene==boot.ArenaLoader.LoadedScene,"both fighters belong to the loaded arena scene");
        int playerId=boot.PlayerActor.GetInstanceID(), opponentId=boot.OpponentActor.GetInstanceID();
        var retry=(Task)typeof(FightSceneBootstrap).GetMethod("PrepareStageAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(boot,null);
        foreach(var step in Wait(retry)) yield return step;
        Check(boot.StageReady && boot.PlayerActor.GetInstanceID()==playerId && boot.OpponentActor.GetInstanceID()==opponentId,"Fight Again reuses ready actors and arena without duplicating them");
        foreach(var step in Wait(boot.ShutdownStageAsync())) yield return step;
        Check(ArenaEnvironmentLoader.LiveSceneHandles==0 && !AnyArenaScene() && FightPreloader.HeldAssets==0,"real Fight exit releases arena scene, fighters and preload handles");
        var fight=SceneManager.GetSceneByPath("Assets/_Project/Scenes/Fight.unity");
        var unload=SceneManager.UnloadSceneAsync(fight); while(!unload.isDone) yield return true;
        Object.Destroy(sessionObject); Time.timeScale=1; QualitySettings.SetQualityLevel(originalQuality,true);
    }
    static void Finish(int code)
    {
        SessionState.SetBool(Active,false); EditorApplication.update-=Tick;
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        settings.ActivePlayModeDataBuilderIndex=SessionState.GetInt(OldBuilder,0); EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/ArenaSceneLifecycleProbe.txt",string.Join("\n",report));
        Debug.Log(string.Join("\n",report)); EditorApplication.Exit(code);
    }
}
#endif
