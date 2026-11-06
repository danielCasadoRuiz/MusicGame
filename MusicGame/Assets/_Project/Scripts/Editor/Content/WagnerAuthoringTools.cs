#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static partial class WagnerAuthoringTools
{
    const string Folder="Assets/_Project/Art/Arenas/Wagner";
    const string High=Folder+"/Scenes/Wagner_Arena_01_High.unity";
    const string Canonical=Folder+"/Authoring/Wagner_Layout.unity";
    const string Queue=@"C:\Users\VISYON\.codex\.chatgpt-projects\g-p-6a438d8b08888191a78acab089f6e907\wagner_authoring\request.json";
    static readonly string[] Tiers={"Low","Mid","HighMid","High","Ultra"};
    static double nextPoll;
    [Serializable] class Request {public string id,op;}
    [Serializable] class SceneInfo {public string path;public bool dirty;}
    [Serializable] class PropInfo {public string path,prefab;public Vector3 position,scale;public Bounds bounds;public string[] added;}
    [Serializable] class Inspection {public string id,error;public bool ok,playing;public SceneInfo[] scenes;public PropInfo[] props;}
    static WagnerAuthoringTools(){if(!Application.isBatchMode)EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<nextPoll)return;
        nextPoll=EditorApplication.timeSinceStartup+.5;
        if(!File.Exists(Queue))return;
        Request request;
        try{request=JsonUtility.FromJson<Request>(File.ReadAllText(Queue));}catch{return;}
        if(request==null||string.IsNullOrEmpty(request.id)||SessionState.GetString("WagnerAuthoring.Last","")==request.id)return;
        SessionState.SetString("WagnerAuthoring.Last",request.id);
        try
        {
            if(request.op=="inspect")Inspect(request.id);
            else Execute(request.op,request.id);
        }
        catch(Exception error){File.WriteAllText(Queue+".result.json",JsonUtility.ToJson(new Inspection{id=request.id,ok=false,error=error.ToString()},true));Debug.LogException(error);}
    }
    static string PathOf(Transform t){return t.parent==null?t.name:PathOf(t.parent)+"/"+t.name+"["+t.GetSiblingIndex()+"]";}
    static bool IsTierPrefab(string path)=>path.EndsWith(".prefab",StringComparison.Ordinal)&&Parse(path,out _,out _);
    static List<GameObject> Props(Scene scene)
    {
        var found=new HashSet<GameObject>();
        foreach(var t in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)))
        {
            var instance=PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
            if(instance&&IsTierPrefab(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance)))found.Add(instance);
        }
        return found.Where(g=>!found.Any(other=>other!=g&&g.transform.IsChildOf(other.transform))).ToList();
    }
    static Bounds BoundsOf(GameObject root)
    {
        var renderers=root.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)return new Bounds(root.transform.position,Vector3.zero);
        var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);return bounds;
    }
    static void Inspect(string id)
    {
        var scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        var scene=scenes.FirstOrDefault(s=>s.path==High);
        bool temporary=false;
        if(!scene.IsValid()&&!EditorApplication.isPlayingOrWillChangePlaymode){scene=EditorSceneManager.OpenScene(High,OpenSceneMode.Additive);temporary=true;}
        var props=scene.IsValid()?Props(scene):new List<GameObject>();
        var result=new Inspection{id=id,ok=true,playing=EditorApplication.isPlayingOrWillChangePlaymode,scenes=scenes.Select(s=>new SceneInfo{path=s.path,dirty=s.isDirty}).ToArray(),props=props.Select(g=>new PropInfo{path=PathOf(g.transform),prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g),position=g.transform.position,scale=g.transform.localScale,bounds=BoundsOf(g),added=PrefabUtility.GetAddedGameObjects(g).Select(a=>PathOf(a.instanceGameObject.transform)).ToArray()}).ToArray()};
        File.WriteAllText(Queue+".result.json",JsonUtility.ToJson(result,true));
        if(temporary)EditorSceneManager.CloseScene(scene,true);
    }
    static partial void Execute(string op,string id);
}
#endif
