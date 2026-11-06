#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class WagnerAuthoringTools
{
    [InitializeOnLoadMethod] static void AuditReady(){File.WriteAllText(Queue+".auditReady","preservation-audit-v1");}
    class Pose {public Vector3 position,scale;public Quaternion rotation;public bool active;}
    class LightState {public Vector3 position,scale;public Quaternion rotation;public Color color;public float intensity,range;}
    class SourceSnapshot
    {
        public Dictionary<string,Pose> poses=new Dictionary<string,Pose>();public Dictionary<string,LightState> lights=new Dictionary<string,LightState>();public Dictionary<string,Color> colors=new Dictionary<string,Color>();public int particles;
    }
    static SourceSnapshot Snapshot(Scene scene)
    {
        var snapshot=new SourceSnapshot();var bindings=Bindings(scene);
        foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))
        {
            if(t.name.StartsWith("WagnerWindow_")||t.GetComponentsInParent<Transform>(true).Any(p=>p.name.StartsWith("WagnerWindow_")))continue;
            string path=Relative(t);if(string.IsNullOrEmpty(path))path="ROOT";
            bool effect=t.GetComponentsInParent<Transform>(true).Any(p=>p.GetComponent<Light>()||p.GetComponent<ParticleSystem>());
            if(!effect)
            {
                var pose=new Pose{position=t.localPosition,rotation=t.localRotation,scale=t.localScale,active=t.gameObject.activeSelf};
                var binding=bindings.FirstOrDefault(b=>t.IsChildOf(b.root.transform));
                if(binding!=null)
                {
                    var baseline=AssetDatabase.LoadAssetAtPath<GameObject>(Standard(binding.asset,binding.tier)).transform.Find("ModelDimensions");Transform normalized=null;
                    if(t.parent==binding.root.transform&&t.name.StartsWith("ModelDimensions"))normalized=baseline;
                    else if(t.parent&&t.parent.parent==binding.root.transform&&t.parent.name.StartsWith("ModelDimensions")&&t.GetSiblingIndex()==0)normalized=baseline.GetChild(0);
                    if(normalized){pose.position-=normalized.localPosition;pose.rotation*=Quaternion.Inverse(normalized.localRotation);pose.scale=new Vector3(pose.scale.x/normalized.localScale.x,pose.scale.y/normalized.localScale.y,pose.scale.z/normalized.localScale.z);}
                }
                snapshot.poses[path]=pose;
            }
            var light=t.GetComponent<Light>();if(light)snapshot.lights[path]=new LightState{position=t.position,rotation=t.rotation,scale=t.lossyScale,color=light.color,intensity=light.intensity,range=light.range};
            if(t.GetComponent<ParticleSystem>())snapshot.particles++;
            var renderer=t.GetComponent<Renderer>();if(renderer)for(int i=0;i<renderer.sharedMaterials.Length;i++){var material=renderer.sharedMaterials[i];if(material&&material.HasProperty("_BaseColor"))snapshot.colors[path+"|"+i]=material.GetColor("_BaseColor");}
        }
        return snapshot;
    }
    static void AuditPreservation(string id)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Finish Play Mode first.");
        var source=EditorSceneManager.OpenScene(Canonical,OpenSceneMode.Additive);SourceSnapshot expected;
        try{expected=Snapshot(source);}finally{EditorSceneManager.CloseScene(source,true);}
        var report=new List<string>();
        foreach(string quality in Tiers)for(int version=1;version<=3;version++)
        {
            string path=Folder+"/Scenes/Wagner_Arena_"+version.ToString("00")+"_"+quality+".unity";var scene=SceneManager.GetSceneByPath(path);bool temporary=!scene.IsValid();if(temporary)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                var actual=Snapshot(scene);
                foreach(var pair in expected.poses)
                {
                    if(!actual.poses.TryGetValue(pair.Key,out var pose))throw new Exception("Lost authored object: "+path+" "+pair.Key);var a=pair.Value;
                    if(Vector3.Distance(a.position,pose.position)>.0005f||Vector3.Distance(a.scale,pose.scale)>.0005f||Quaternion.Angle(a.rotation,pose.rotation)>.1f||a.active!=pose.active)throw new Exception("Authored transform changed: "+path+" "+pair.Key);
                }
                foreach(var pair in expected.lights)
                {
                    if(!actual.lights.TryGetValue(pair.Key,out var light))throw new Exception("Lost authored light: "+pair.Key);var a=pair.Value;
                    if(Vector3.Distance(a.position,light.position)>.0005f||Vector3.Distance(a.scale,light.scale)>.0005f||Quaternion.Angle(a.rotation,light.rotation)>.1f||a.color!=light.color||Mathf.Abs(a.intensity-light.intensity)>.0001f||Mathf.Abs(a.range-light.range)>.0001f)throw new Exception("Authored light changed: "+path+" "+pair.Key);
                }
                foreach(var pair in expected.colors)if(!actual.colors.TryGetValue(pair.Key,out var color)||Vector4.Distance((Vector4)pair.Value,(Vector4)color)>.0001f)throw new Exception("Authored material tint changed: "+path+" "+pair.Key);
                if(actual.particles!=expected.particles)throw new Exception("Authored fire count changed: "+path);
                report.Add("PASS "+path+": "+expected.poses.Count+" authored transforms, "+expected.lights.Count+" lights, "+expected.colors.Count+" material tints and "+expected.particles+" particle systems preserved.");
            }
            finally{if(temporary)EditorSceneManager.CloseScene(scene,true);}
        }
        File.WriteAllLines("Logs/WagnerAuthoredPreservation.txt",report);File.WriteAllText(Queue+".result.json",JsonUtility.ToJson(new Result{id=id,ok=true,scenes=15,source=Canonical},true));
    }
    public static void BatchCompileAndAudit()
    {
        try{Backup();BuildFromCanonical(false);AuditPreservation("batch-authored-wagner");EditorApplication.Exit(0);}
        catch(Exception error){File.WriteAllText("Logs/WagnerAuthoredBatch.error.txt",error.ToString());Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
#endif
