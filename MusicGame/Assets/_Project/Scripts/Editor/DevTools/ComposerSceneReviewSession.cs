#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class ComposerSceneReviewSession
{
    const string Key="ComposerReviewSession.Active";
    static string commandPath,resultPath,lastId;
    static Camera camera;
    static int frames;
    static Command pending;
    static SceneSetup[] previousSetup;
    static int previousQuality=-1;
    static Material figureMaterial;
    static (ReflectionProbe probe,int id)[] renderedProbes;
    static double probeDeadline;
    static double nextPoll;
    [Serializable] class Command { public string id,op,composer,quality; public bool scaleFigures; }
    static ComposerSceneReviewSession()
    {
        string localCommand=@"C:\Users\VISYON\.codex\.chatgpt-projects\g-p-6a438d8b08888191a78acab089f6e907\scenery_staging\review_command.json";
        if(!Application.isBatchMode&&File.Exists(localCommand)){commandPath=localCommand;resultPath=commandPath+".result.json";SessionState.SetBool(Key,true);SessionState.SetString(Key+".Command",commandPath);SessionState.SetString(Key+".Result",resultPath);}
        if(SessionState.GetBool(Key,false)){commandPath=SessionState.GetString(Key+".Command","");resultPath=SessionState.GetString(Key+".Result","");lastId=SessionState.GetString(Key+".LastId","");EditorApplication.update+=Tick;}
    }
    public static void RunFromCommandLine()
    {
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-reviewCommand");
        commandPath=args[i+1];resultPath=commandPath+".result.json";SessionState.SetString(Key+".Command",commandPath);SessionState.SetString(Key+".Result",resultPath);SessionState.SetBool(Key,true);
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;File.WriteAllText(resultPath,"{\"id\":\"ready\",\"ok\":true}");
    }
    static void Tick()
    {
        try
        {
            if(pending!=null)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                if(--frames>0)return;
                if(renderedProbes!=null&&EditorApplication.timeSinceStartup<probeDeadline&&renderedProbes.Any(p=>p.probe!=null&&!p.probe.IsFinishedRendering(p.id)))return;
                Render();return;
            }
            if(EditorApplication.timeSinceStartup<nextPoll)return;nextPoll=EditorApplication.timeSinceStartup+.3;
            if(!File.Exists(commandPath))return;
            string json;
            try {using(var stream=new FileStream(commandPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream))json=reader.ReadToEnd();}
            catch(IOException){return;}
            if(string.IsNullOrWhiteSpace(json))return;
            Command command;try {command=JsonUtility.FromJson<Command>(json);}catch(ArgumentException){return;}
            if(command==null||string.IsNullOrEmpty(command.id)||command.id==lastId)return;lastId=command.id;SessionState.SetString(Key+".LastId",lastId);
            if(command.op=="stop"||command.op=="quit"){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;File.WriteAllText(resultPath,"{\"id\":\""+lastId+"\",\"ok\":true}");if(Application.isBatchMode&&command.op=="quit")EditorApplication.Exit(0);return;}
            if(!Application.isBatchMode)
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Review waiting: Unity is in Play Mode.");
                if(UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()!=null)throw new Exception("Review waiting: a prefab is open for editing.");
                for(int i=0;i<SceneManager.sceneCount;i++)
                {
                    var original=SceneManager.GetSceneAt(i);
                    if(original.isDirty)throw new Exception("Review waiting: save the open scenes first; no scene was replaced.");
                    if(string.IsNullOrEmpty(original.path))
                    {
                        Directory.CreateDirectory("Assets/_Project/Scenes/Debug");
                        if(!EditorSceneManager.SaveScene(original,"Assets/_Project/Scenes/Debug/SceneBeforeComposerReview.unity"))throw new Exception("Could not preserve the untitled scene.");
                    }
                }
                previousSetup=EditorSceneManager.GetSceneManagerSetup();
            }
            if(command.op=="probe"){previousSetup=null;ComposerArenaLifecycleProbe.RunFromCommandLine();return;}
            if(command.op=="build")ComposerSceneryAssembly.BuildOneForReview(command.composer);
            var manifest=JsonUtility.FromJson<ComposerSceneryAssembly.Manifest>(File.ReadAllText("Assets/_Project/Art/Arenas/composer_scenery_manifest.json"));var spec=manifest.scenes.Single(s=>s.composer==command.composer);
            string quality=string.IsNullOrEmpty(command.quality)?"High":command.quality;
            if(!new[]{"Low","Mid","HighMid","High","Ultra"}.Contains(quality))throw new ArgumentException("Unknown quality: "+quality);
            previousQuality=QualitySettings.GetQualityLevel();int qualityIndex=Array.IndexOf(QualitySettings.names,quality);
            if(qualityIndex<0)throw new ArgumentException("Quality not configured: "+quality);QualitySettings.SetQualityLevel(qualityIndex,true);
            var scene=EditorSceneManager.OpenScene($"Assets/_Project/Art/Arenas/{command.composer}/Scenes/{command.composer}_Arena_01_{quality}.unity",OpenSceneMode.Single);
            var root=scene.GetRootGameObjects().Single(g=>g.GetComponentInChildren<ArenaEnvironmentSettings>(true)!=null);var env=root.GetComponentInChildren<ArenaEnvironmentSettings>(true);
            camera=new GameObject("ReviewCamera").AddComponent<Camera>();camera.nearClipPlane=.1f;camera.farClipPlane=250;
            camera.clearFlags=env.skybox!=null?CameraClearFlags.Skybox:CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.025f,.03f);
            camera.fieldOfView=spec.view!=null?spec.view.fov:48;camera.transform.position=spec.view!=null?spec.view.position:new Vector3(0,2,-7);camera.transform.LookAt(spec.view!=null?spec.view.target:new Vector3(0,2.6f,4));
            if(command.scaleFigures)
            {
                figureMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));figureMaterial.SetColor("_BaseColor",new Color(.85f,.80f,.72f));figureMaterial.SetFloat("_Smoothness",.25f);
                foreach(float x in new[]{-2.4f,2.4f}){var capsule=GameObject.CreatePrimitive(PrimitiveType.Capsule);capsule.name="ReviewScaleFigure_1.8m";capsule.transform.position=new Vector3(x,.9f,0);capsule.transform.localScale=new Vector3(.45f,.9f,.45f);capsule.GetComponent<Renderer>().sharedMaterial=figureMaterial;}
            }
            env.Apply(root.GetComponentInChildren<ArenaSceneLighting>(true).mainLight,camera);
            renderedProbes=root.GetComponentsInChildren<ReflectionProbe>(true).Where(p=>p.mode==ReflectionProbeMode.Realtime).Select(p=>(p,p.RenderProbe())).ToArray();probeDeadline=EditorApplication.timeSinceStartup+5;
            pending=command;frames=8;EditorApplication.QueuePlayerLoopUpdate();
        }
        catch(Exception e){File.WriteAllText(resultPath,JsonUtility.ToJson(new Result{id=lastId,ok=false,error=e.ToString()}));Debug.LogException(e);pending=null;Restore();}
    }
    [Serializable]class Result{public string id,path,error;public bool ok;}
    static void Render()
    {
        string folder="Logs/ComposerSceneReview/"+pending.composer;Directory.CreateDirectory(folder);string path=folder+"/reference_view.png";
        var rt=new RenderTexture(1440,1080,24);var tex=new Texture2D(1440,1080,TextureFormat.RGB24,false);var before=RenderTexture.active;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1440,1080),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());File.WriteAllText(resultPath,JsonUtility.ToJson(new Result{id=pending.id,ok=true,path=Path.GetFullPath(path)}));}
        finally{camera.targetTexture=null;RenderTexture.active=before;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);pending=null;Restore();}
    }
    static void Restore(){renderedProbes=null;if(figureMaterial!=null){Object.DestroyImmediate(figureMaterial);figureMaterial=null;}if(previousQuality>=0){QualitySettings.SetQualityLevel(previousQuality,true);previousQuality=-1;}if(previousSetup==null)return;EditorSceneManager.RestoreSceneManagerSetup(previousSetup);previousSetup=null;}
}
#endif
