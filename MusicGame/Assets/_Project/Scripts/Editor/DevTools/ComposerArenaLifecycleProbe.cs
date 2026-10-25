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
public static class ComposerArenaLifecycleProbe
{
    const string Active="ComposerArenaProbe.Active", OldBuilder="ComposerArenaProbe.Builder";
    static IEnumerator<bool> routine;
    static readonly List<string> report=new();
    static double deadline;
    [Serializable] class SavedScene { public string path;public bool loaded,active; }
    [Serializable] class SavedSetup {public SavedScene[] scenes;}
    static ComposerArenaLifecycleProbe() { if(SessionState.GetBool(Active,false)) EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=RestoreInteractiveScene; }
    static void RestoreInteractiveScene(PlayModeStateChange change)
    {
        if(change!=PlayModeStateChange.EnteredEditMode||!SessionState.GetBool(Active+".Restore",false))return;
        SessionState.SetBool(Active+".Restore",false);
        var setup=JsonUtility.FromJson<SavedSetup>(SessionState.GetString(Active+".Scenes",""));
        EditorSceneManager.RestoreSceneManagerSetup(setup.scenes.Select(s=>new SceneSetup{path=s.path,isLoaded=s.loaded,isActive=s.active}).ToArray());
    }
    public static void RunFromCommandLine()
    {
        if(!Application.isBatchMode)
        {
            var before=EditorSceneManager.GetSceneManagerSetup();
            if(before.Any(s=>string.IsNullOrEmpty(s.path))||Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty))throw new Exception("Save the current scenes before the interactive probe.");
            SessionState.SetString(Active+".Scenes",JsonUtility.ToJson(new SavedSetup{scenes=before.Select(s=>new SavedScene{path=s.path,loaded=s.isLoaded,active=s.isActive}).ToArray()}));
            SessionState.SetBool(Active+".Restore",true);
        }
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        SessionState.SetInt(OldBuilder,settings.ActivePlayModeDataBuilderIndex);
        int fast=settings.DataBuilders.FindIndex(b=>b!=null && b.GetType().Name=="BuildScriptFastMode");
        if(fast<0) throw new Exception("Addressables fast-mode provider not available");
        settings.ActivePlayModeDataBuilderIndex=fast; EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Assets/_Project/Scenes/Debug");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene,"Assets/_Project/Scenes/Debug/ComposerArenaLifecycleProbe.unity");
        SessionState.SetBool(Active,true); EditorApplication.update+=Tick; EditorApplication.isPlaying=true;
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying) return;
        try
        {
            if(routine==null) { deadline=EditorApplication.timeSinceStartup+600; routine=Run(); }
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
            if(quality=="Ultra") { for(int frame=0;frame<8;frame++) yield return true; Capture("Monteverdi",loader.LoadedScene); }
            foreach(var step in Wait(loader.ShutdownAsync())) yield return step;
            Object.Destroy(owner); yield return true;
            Check(ArenaEnvironmentLoader.LiveSceneHandles==0 && !AnyArenaScene(),"unload releases "+quality+" and leaves no arena scene");
            Check(SceneManager.GetActiveScene()==identity,"restores owning scene after "+quality);
        }
        // Validate newly migrated first arenas through the production scene loader.
        foreach(string composer in new[]{"Bach","Beethoven","Brahms","Chopin","Handel","Haydn","Mozart","Pachelbel","Tchaikovsky","Vivaldi","Wagner"})
        foreach(string quality in new[]{"Low","Ultra"})
        {
            QualitySettings.SetQualityLevel(QualityAssetResolver.IndexOf(QualitySettings.names,quality),true);
            var arenaRef=new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID($"Assets/_Project/Art/Arenas/{composer}/{composer}_Arena_01.prefab"));
            var loader=Loader(out var owner); loader.Request(arenaRef,"composer/"+composer+"/"+quality);
            foreach(var step in Wait(loader.LoadingTask)) yield return step;
            Check(loader.IsReady && loader.LoadedQuality==quality && loader.LoadedScene.isLoaded,"production loader: "+composer+" "+quality);
            var root=loader.Instance;
            var env=root.GetComponentInChildren<ArenaEnvironmentSettings>(true);
            var scenario=JsonUtility.FromJson<ComposerSceneryAssembly.Manifest>(File.ReadAllText("Assets/_Project/Art/Arenas/composer_scenery_manifest.json")).scenes.Single(s=>s.composer==composer);
            if(scenario.look!=null && scenario.look.hideSky)
                Check(env!=null && env.skybox==null && RenderSettings.skybox==null && !env.cameraClearsToSkybox && root.transform.Find("ComposerScenery/"+composer+"_Ceiling")!=null,"enclosed interior without skybox: "+composer+" "+quality);
            else Check(env!=null && env.skybox!=null && RenderSettings.skybox==env.skybox,"active skybox: "+composer+" "+quality);
            var floor=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Floor");
            Check(floor.sharedMaterial!=null && floor.sharedMaterial.GetTexture("_BaseMap")!=null && floor.sharedMaterial.GetTexture("_BumpMap")!=null,"floor albedo/normal: "+composer+" "+quality);
            Check(root.GetComponentsInChildren<MonteverdiPropQuality>(true).Length==0,"runtime has only selected tier: "+composer+" "+quality);
            Check(root.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader!=null&&m.shader.isSupported)),"valid materials: "+composer+" "+quality);
            if(quality=="Ultra") { for(int frame=0;frame<8;frame++) yield return true; Capture(composer,loader.LoadedScene); }
            foreach(var step in Wait(loader.ShutdownAsync())) yield return step;
            Object.Destroy(owner); yield return true;
            Check(ArenaEnvironmentLoader.LiveSceneHandles==0 && SceneManager.GetActiveScene()==identity,"clean unload: "+composer+" "+quality);
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
    static void Capture(string composer,Scene scene)
    {
        Directory.CreateDirectory("Logs/ComposerArenaRuntimePreviews");
        var go=new GameObject("RuntimePreviewCamera"); SceneManager.MoveGameObjectToScene(go,scene);
        var spec=JsonUtility.FromJson<ComposerSceneryAssembly.Manifest>(File.ReadAllText("Assets/_Project/Art/Arenas/composer_scenery_manifest.json")).scenes.Single(s=>s.composer==composer);
        var camera=go.AddComponent<Camera>(); camera.clearFlags=RenderSettings.skybox==null?CameraClearFlags.SolidColor:CameraClearFlags.Skybox; camera.backgroundColor=new Color(.035f,.03f,.025f); camera.fieldOfView=spec.view!=null?spec.view.fov:62; camera.nearClipPlane=.1f; camera.farClipPlane=400;
        camera.transform.position=spec.view!=null?spec.view.position:new Vector3(0,2.6f,-7.6f); camera.transform.LookAt(spec.view!=null?spec.view.target:new Vector3(0,3.6f,6));
        var rt=new RenderTexture(1200,800,24); var tex=new Texture2D(1200,800,TextureFormat.RGB24,false); var before=RenderTexture.active;
        try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; tex.ReadPixels(new Rect(0,0,1200,800),0,0); tex.Apply(); File.WriteAllBytes("Logs/ComposerArenaRuntimePreviews/"+composer+".png",tex.EncodeToPNG()); }
        finally { camera.targetTexture=null; RenderTexture.active=before; rt.Release(); Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go); }
    }
    static void Finish(int code)
    {
        SessionState.SetBool(Active,false); EditorApplication.update-=Tick;
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        settings.ActivePlayModeDataBuilderIndex=SessionState.GetInt(OldBuilder,0); EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/ComposerArenaLifecycleProbe.txt",string.Join("\n",report));
        Debug.Log(string.Join("\n",report));if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.isPlaying=false;
    }
}
#endif
