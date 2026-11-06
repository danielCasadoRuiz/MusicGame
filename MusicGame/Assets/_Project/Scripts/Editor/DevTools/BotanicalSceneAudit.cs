#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;
public static class BotanicalSceneAudit {
 const string Work=@"C:\Users\VISYON\.codex\.chatgpt-projects\g-p-6a438d8b08888191a78acab089f6e907\flora_all";
 [Serializable] class Report{public Room[] rooms;}
 [Serializable] class Room{public string composer;public Row[] renderers;}
 [Serializable] class Row{public string name,path,mesh;public Vector3 position,size,center,scale,localSize;public int triangles;}
 public static void Run(){int code=0;try{var rooms=new List<Room>();foreach(var dir in Directory.GetDirectories("Assets/_Project/Art/Arenas").Where(d=>!Path.GetFileName(d).StartsWith("_"))){string c=Path.GetFileName(dir),path=dir+"/Scenes/"+c+"_Arena_01_High.unity";if(!File.Exists(path))continue;var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);var rows=new List<Row>();foreach(var f in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f.sharedMesh)){var rr=f.GetComponent<Renderer>();rows.Add(new Row{name=Hierarchy(f.transform),path=AssetDatabase.GetAssetPath(f.sharedMesh),mesh=f.sharedMesh.name,position=f.transform.position,size=rr.bounds.size,center=rr.bounds.center,scale=f.transform.lossyScale,localSize=f.sharedMesh.bounds.size,triangles=f.sharedMesh.triangles.Length/3});}rooms.Add(new Room{composer=c,renderers=rows.ToArray()});}Directory.CreateDirectory(Work);File.WriteAllText(Work+"/scene_inventory.json",JsonUtility.ToJson(new Report{rooms=rooms.ToArray()},true));}catch(Exception e){code=1;File.WriteAllText(Work+"/audit_error.txt",e.ToString());Debug.LogException(e);}EditorApplication.Exit(code);}
 static string Hierarchy(Transform t)=>t.parent?Hierarchy(t.parent)+"/"+t.name:t.name;
}
#endif
