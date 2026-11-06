#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AddressableAssets;
using Object=UnityEngine.Object;

public static partial class WagnerAuthoringTools
{
    const string StatePath=Folder+"/Authoring/State.json";
    const string Generated=Folder+"/AuthoredGenerated";
    [Serializable] class AuthoringState {public string highHash,highDependencyHash,sourceHash,backup;}
    [Serializable] class Result {public string id,error,backup,source;public bool ok;public int scenes,props;}
    class Binding {public GameObject root;public string asset,tier,path;}
    static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    static string Key(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").Substring(0,16);}
    static string Relative(Transform t){return t.parent==null?"":(t.parent.parent==null?"":Relative(t.parent)+"/")+t.name;}
    static bool Parse(string path,out string asset,out string tier)
    {
        asset=tier=null;var match=Regex.Match(path,@"/Props/([^/]+)/(?:[^/]+/)?(Low|Mid|HighMid|High|Ultra)/");
        if(!match.Success||!path.StartsWith(Folder+"/",StringComparison.Ordinal))return false;asset=match.Groups[1].Value;tier=match.Groups[2].Value;return true;
    }
    static List<Binding> Bindings(Scene scene)
    {
        var roots=new HashSet<GameObject>();
        foreach(var t in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)))
        {var r=PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);if(r&&Parse(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r),out _,out _))roots.Add(r);}
        return roots.Where(g=>!roots.Any(other=>other!=g&&g.transform.IsChildOf(other.transform))).Select(g=>{Parse(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g),out var a,out var q);return new Binding{root=g,asset=a,tier=q,path=Relative(g.transform)};}).OrderBy(b=>b.path).ToList();
    }
    static string Standard(string asset,string tier)=>Folder+"/Props/"+asset+"/"+tier+"/"+asset+"_"+tier+".prefab";
    static string Backup()
    {
        string backup=Path.GetFullPath("../tools/scenario-backups/user-wagner-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        foreach(string path in Directory.GetFiles(Folder,"*",SearchOption.AllDirectories).Where(p=>new[]{".unity",".prefab",".meta",".json",".mat",".asset"}.Any(p.EndsWith)))
        {string dest=Path.Combine(backup,path.Substring(Folder.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(path,dest,true);}
        return backup;
    }
    static partial void Execute(string op,string id)
    {
        if(op=="audit"){AuditPreservation(id);return;}
        if(op!="propagate"&&op!="compile")throw new ArgumentException("Unknown authoring operation: "+op);
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Unity is in Play Mode. The current test was not interrupted.");
        string backup=Backup();
        if(op=="propagate")Capture();
        BuildFromCanonical(false);
        File.WriteAllText(Queue+".result.json",JsonUtility.ToJson(new Result{id=id,ok=true,backup=backup,source=Canonical,scenes=15},true));
    }
    static void Capture()
    {
        var open=SceneManager.GetSceneByPath(High);
        if(open.IsValid()&&open.isLoaded&&open.isDirty&&!EditorSceneManager.SaveScene(open))throw new Exception("Could not save the edited Wagner scene.");
        Directory.CreateDirectory(Path.GetDirectoryName(Canonical));File.Copy(High,Canonical,true);AssetDatabase.ImportAsset(Canonical,ImportAssetOptions.ForceSynchronousImport);
    }
    public static bool HasCanonical=>File.Exists(Canonical);
    public static void BuildFromCanonical(bool adoptNewHigh=true)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Cannot propagate Wagner while a game test is running.");
        var state=File.Exists(StatePath)?JsonUtility.FromJson<AuthoringState>(File.ReadAllText(StatePath)):new AuthoringState();
        var openHigh=SceneManager.GetSceneByPath(High);
        bool materialOrMeshEdited=!string.IsNullOrEmpty(state.highDependencyHash)&&state.highDependencyHash!=AssetDatabase.GetAssetDependencyHash(High).ToString();
        if(adoptNewHigh&&(!HasCanonical||state.highHash!=Hash(High)||materialOrMeshEdited||(openHigh.IsValid()&&openHigh.isDirty))) {state.backup=Backup();Capture();}
        if(!HasCanonical)throw new Exception("No saved Wagner authoring scene.");
        var originalActive=SceneManager.GetActiveScene();var report=new List<string>{"version,quality,prop,asset,selectedTier"};string expectedSignature=null;
        BeginWindows();
        try
        {
            // High first: when it is open in the user's editor, keep that scene open and update it in place.
            foreach(string quality in new[]{"High","Low","Mid","HighMid","Ultra"})for(int version=1;version<=(File.Exists(ScenarioVariantBuilder.Config)?1:3);version++)
            {
                string output=Folder+"/Scenes/Wagner_Arena_"+version.ToString("00")+"_"+quality+".unity";
                var loaded=SceneManager.GetSceneByPath(output);bool existing=loaded.IsValid()&&loaded.isLoaded;
                if(existing&&loaded.isDirty&&output!=High)throw new Exception("Unsaved changes in "+output+"; left untouched.");
                Scene scene=existing?loaded:EditorSceneManager.OpenScene(Canonical,OpenSceneMode.Additive);
                if(existing&&output!=High)throw new Exception("Another target Wagner scene is open; close it before propagation: "+output);
                try
                {
                    var bindings=Bindings(scene);if(bindings.Count==0)throw new Exception("Could not identify the edited scene's quality props.");
                    var arena=scene.GetRootGameObjects().Single(g=>g.GetComponentInChildren<ArenaEnvironmentSettings>(true));
                    string signature=Signature(arena,bindings);
                    if(expectedSignature==null)expectedSignature=signature;else if(expectedSignature!=signature)throw new Exception("Source composition changed during propagation.");
                    var expectedModels=new HashSet<string>();
                    foreach(var b in bindings)
                    {
                        string selected=Select(b,Array.IndexOf(Tiers,quality));
                        string components=ComponentSignature(b.root);
                        Retarget(b,selected);
                        string key=Key(b.path);string dir=Generated+"/Props/"+b.asset+"/"+key+"/"+selected;Directory.CreateDirectory(dir);
                        var parent=b.root.transform.parent;var pos=b.root.transform.localPosition;var rot=b.root.transform.localRotation;var scale=b.root.transform.localScale;string name=b.root.name;int sibling=b.root.transform.GetSiblingIndex();
                        string path=dir+"/"+b.asset+".prefab";var prefab=PrefabUtility.SaveAsPrefabAsset(b.root,path);
                        var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);instance.name=name;instance.transform.SetParent(parent,false);instance.transform.SetLocalPositionAndRotation(pos,rot);instance.transform.localScale=scale;instance.transform.SetSiblingIndex(sibling);Object.DestroyImmediate(b.root);b.root=instance;b.tier=selected;
                        if(ComponentSignature(instance)!=components)throw new Exception("An authored child/component was lost: "+b.path);
                        foreach(string model in AssetDatabase.GetDependencies(path,true).Where(p=>p.EndsWith(".fbx")&&p.Contains("/Props/")))expectedModels.Add(model);
                        report.Add(version+","+quality+",\""+b.path+"\","+b.asset+","+selected);
                    }
                    if(Signature(arena,bindings)!=expectedSignature)throw new Exception("An authored placement or light changed during retargeting.");
                    ApplyWindows(scene,bindings,quality);
                    foreach(var light in arena.GetComponentsInChildren<Light>(true).Where(l=>l.type==LightType.Point&&l.name.StartsWith("Reference_")))if(Array.IndexOf(Tiers,quality)<3)light.shadows=LightShadows.None;
                    arena.name="Wagner_Arena_"+version.ToString("00");
                    string oldGuid=AssetDatabase.AssetPathToGUID(output);
                    if(!EditorSceneManager.SaveScene(scene,output,!existing))throw new Exception("Could not save "+output);
                    if(!string.IsNullOrEmpty(oldGuid)&&AssetDatabase.AssetPathToGUID(output)!=oldGuid)throw new Exception("Scene identity changed: "+output);
                    var actual=new HashSet<string>(AssetDatabase.GetDependencies(output,true).Where(p=>p.EndsWith(".fbx")&&p.Contains("/Props/")));
                    if(!actual.SetEquals(expectedModels))throw new Exception("Wrong quality model dependencies: "+output);
                    if(arena.GetComponentsInChildren<MonteverdiPropQuality>(true).Length!=0)throw new Exception("Unexpected runtime quality selector.");
                    if(quality=="High")PrefabUtility.SaveAsPrefabAsset(arena,Folder+"/Wagner_Arena_"+version.ToString("00")+".prefab");
                    Register(version,quality,output);
                }
                finally{if(!existing&&scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);}
            }
            if(File.Exists(ScenarioVariantBuilder.Config))BuildIndependentVariants();
            AssetDatabase.SaveAssets();state.highHash=Hash(High);state.highDependencyHash=AssetDatabase.GetAssetDependencyHash(High).ToString();state.sourceHash=Hash(Canonical);File.WriteAllText(StatePath,JsonUtility.ToJson(state,true));
            Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/WagnerAuthoredPropagation.csv",report);File.WriteAllText("Logs/WagnerAuthoredPropagation.txt","PASS: 15 scenes propagated from the user-authored layout; placements, lights, object counts, quality model dependencies and existing scene GUIDs preserved.\nAuthoring source: "+Canonical);
        }
        finally{if(originalActive.IsValid()&&originalActive.isLoaded)SceneManager.SetActiveScene(originalActive);}
    }
    static string Signature(GameObject arena,List<Binding> bindings)
    {
        var excluded=new HashSet<Transform>(bindings.SelectMany(b=>b.root.GetComponentsInChildren<Transform>(true)).Where(t=>bindings.All(b=>t!=b.root.transform)));
        var lines=new List<string>();
        foreach(var t in arena.GetComponentsInChildren<Transform>(true))
        {
            if(excluded.Contains(t)||t.name.StartsWith("WagnerWindow_"))continue;
            string path=t==arena.transform?"ROOT":Relative(t);
            lines.Add(path+"|"+t.localPosition.ToString("F5")+"|"+t.localRotation.ToString("F5")+"|"+t.localScale.ToString("F5")+"|"+t.gameObject.activeSelf);
            foreach(var l in t.GetComponents<Light>())lines.Add(path+"|LIGHT|"+l.type+"|"+l.color.ToString("F5")+"|"+l.intensity.ToString("F5")+"|"+l.range.ToString("F5")+"|"+l.spotAngle.ToString("F5")+"|"+l.enabled);
        }
        return string.Join("\n",lines.OrderBy(l=>l));
    }
    static int Band(Bounds bounds,string name,string asset,int quality)
    {
        var rule=JsonUtility.FromJson<ArenaDistanceQuality.Policy>(File.ReadAllText("Assets/_Project/Configs/Editor/arena_distance_quality.json")).scenes.Single(s=>s.composer=="Wagner");
        float dx=Mathf.Max(0,Mathf.Abs(bounds.center.x-rule.center.x)-bounds.extents.x-rule.halfWidth),dz=Mathf.Max(0,Mathf.Abs(bounds.center.z-rule.center.z)-bounds.extents.z-rule.halfDepth);
        float distance=Mathf.Sqrt(dx*dx+dz*dz);int band=distance<=rule.near?0:distance<=rule.middle?1:distance<=rule.far?2:3;
        var exception=rule.exceptions?.SingleOrDefault(e=>!string.IsNullOrEmpty(e.objectName)&&e.objectName==name)??rule.exceptions?.SingleOrDefault(e=>string.IsNullOrEmpty(e.objectName)&&e.prefabName==asset);
        if(exception!=null&&exception.forcedBand>=0)band=Mathf.Clamp(exception.forcedBand,0,3);
        return Mathf.Clamp(Mathf.Max(quality-band,exception?.minimumQuality??0),0,quality);
    }
    static string Select(Binding source,int quality)
    {
        var clone=Object.Instantiate(source.root);clone.hideFlags=HideFlags.HideAndDontSave;
        clone.transform.SetParent(source.root.transform.parent,false);clone.transform.SetLocalPositionAndRotation(source.root.transform.localPosition,source.root.transform.localRotation);clone.transform.localScale=source.root.transform.localScale;
        try{var probe=new Binding{root=clone,asset=source.asset,tier=source.tier};Retarget(probe,"Ultra");var renderers=clone.GetComponentsInChildren<Renderer>(true).Where(r=>r is MeshRenderer||r is SkinnedMeshRenderer).ToArray();if(renderers.Length==0)throw new Exception("No quality geometry: "+source.asset);var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);return Tiers[Band(bounds,source.root.name,source.asset,quality)];}
        finally{Object.DestroyImmediate(clone);}
    }
    static string TierPath(string path,string target)
    {
        var match=Regex.Match(path,@"/Props/([^/]+)/(Low|Mid|HighMid|High|Ultra)/");if(!match.Success)return path;
        string source=match.Groups[2].Value;return path.Substring(0,match.Groups[2].Index)+target+path.Substring(match.Groups[2].Index+source.Length).Replace("_"+source+".","_"+target+".");
    }
    static void Retarget(Binding b,string target)
    {
        var effects=b.root.GetComponentsInChildren<Transform>(true).Where(t=>t.GetComponent<Light>()||t.GetComponent<ParticleSystem>()).Select(t=>(transform:t,position:t.position,rotation:t.rotation,scale:t.lossyScale)).ToArray();
        var sourcePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Standard(b.asset,b.tier));var targetPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Standard(b.asset,target));
        if(!sourcePrefab||!targetPrefab)throw new Exception("Missing authored quality: "+b.asset+" "+target);
        if(PrefabUtility.IsAnyPrefabInstanceRoot(b.root))PrefabUtility.UnpackPrefabInstance(b.root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        var sourceDims=sourcePrefab.transform.Find("ModelDimensions");var targetDims=targetPrefab.transform.Find("ModelDimensions");
        foreach(var t in b.root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("ModelDimensions")&&t.parent==b.root.transform).ToArray())
        {
            t.localPosition+=targetDims.localPosition-sourceDims.localPosition;t.localScale=Vector3.Scale(t.localScale,new Vector3(targetDims.localScale.x/sourceDims.localScale.x,targetDims.localScale.y/sourceDims.localScale.y,targetDims.localScale.z/sourceDims.localScale.z));
            if(t.childCount>0&&sourceDims.childCount>0&&targetDims.childCount>0)
            {var s=sourceDims.GetChild(0);var d=targetDims.GetChild(0);var c=t.GetChild(0);c.localPosition+=d.localPosition-s.localPosition;c.localRotation=c.localRotation*Quaternion.Inverse(s.localRotation)*d.localRotation;c.localScale=Vector3.Scale(c.localScale,new Vector3(d.localScale.x/s.localScale.x,d.localScale.y/s.localScale.y,d.localScale.z/s.localScale.z));}
        }
        foreach(var filter in b.root.GetComponentsInChildren<MeshFilter>(true))
        {
            if(!filter.sharedMesh)continue;string old=AssetDatabase.GetAssetPath(filter.sharedMesh),path=TierPath(old,target);if(path==old)continue;
            var meshes=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();var replacement=meshes.FirstOrDefault(m=>m.name==filter.sharedMesh.name)??(meshes.Length==1?meshes[0]:null);if(!replacement)throw new Exception("Cannot map mesh to "+target+": "+old);filter.sharedMesh=replacement;
        }
        foreach(var collider in b.root.GetComponentsInChildren<MeshCollider>(true))if(collider.sharedMesh)
        {string old=AssetDatabase.GetAssetPath(collider.sharedMesh),path=TierPath(old,target);if(path!=old)collider.sharedMesh=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().First();}
        foreach(var renderer in b.root.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>MaterialFor(m,target)).ToArray();
        foreach(var effect in effects){effect.transform.SetPositionAndRotation(effect.position,effect.rotation);var parent=effect.transform.parent?effect.transform.parent.lossyScale:Vector3.one;effect.transform.localScale=new Vector3(effect.scale.x/parent.x,effect.scale.y/parent.y,effect.scale.z/parent.z);}
    }
    static string ComponentSignature(GameObject root)=>string.Join("|",root.GetComponentsInChildren<Component>(true).Select(c=>c?c.GetType().FullName:"MISSING_SCRIPT").GroupBy(t=>t).OrderBy(g=>g.Key).Select(g=>g.Key+":"+g.Count()));
    static Material MaterialFor(Material material,string target)
    {
        if(!material)return material;
        Material temporary=null;
        string original=material.GetTag("WagnerWindowSource",false,"");if(!string.IsNullOrEmpty(original))
        {
            var basis=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(original));temporary=new Material(material);temporary.shader=basis.shader;temporary.SetOverrideTag("WagnerWindowSource","");temporary.SetOverrideTag("WagnerMaterialOrigin",basis.GetTag("WagnerMaterialOrigin",false,original));material=temporary;
        }
        string source=AssetDatabase.GetAssetPath(material);
        if(!source.Contains("/Props/")&&string.IsNullOrEmpty(material.GetTag("WagnerMaterialOrigin",false,"")))return material;
        string origin=material.GetTag("WagnerMaterialOrigin",false,AssetDatabase.AssetPathToGUID(source));string dir=Generated+"/Materials/"+origin+"/"+target;Directory.CreateDirectory(dir);string path=dir+"/Material.mat";
        var output=AssetDatabase.LoadAssetAtPath<Material>(path);if(!output){output=new Material(material);AssetDatabase.CreateAsset(output,path);}else if(output!=material)output.CopyPropertiesFromMaterial(material);
        output.shader=material.shader;
        foreach(string property in material.GetTexturePropertyNames())
        {var texture=material.GetTexture(property);if(!texture)continue;string old=AssetDatabase.GetAssetPath(texture),mapped=TierPath(old,target);if(mapped!=old){var replacement=AssetDatabase.LoadAssetAtPath<Texture>(mapped);if(!replacement)throw new Exception("Missing tier texture: "+mapped);output.SetTexture(property,replacement);}}
        output.SetOverrideTag("WagnerMaterialOrigin",origin);EditorUtility.SetDirty(output);if(temporary)Object.DestroyImmediate(temporary);return output;
    }
    static void Register(int version,string quality,string scene)
    {
        var settings=AddressableAssetSettingsDefaultObject.Settings;var group=settings.FindGroup("Remote_Arenas");
        string master=Folder+"/Wagner_Arena_"+version.ToString("00")+".prefab",guid=AssetDatabase.AssetPathToGUID(master);
        var catalog=AssetDatabase.LoadAssetAtPath<ArenaSceneCatalog>("Assets/_Project/Resources/ArenaSceneCatalog.asset");var entry=catalog.Find(guid);
        if(entry==null){entry=new ArenaSceneCatalog.Entry{sourceArenaGuid=guid};catalog.entries.Add(entry);}entry.variants.RemoveAll(v=>v.qualityName==quality);entry.variants.Add(new ArenaSceneCatalog.Variant{qualityName=quality,scene=new AssetReference(AssetDatabase.AssetPathToGUID(scene))});
        var address=settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(scene),group);address.address="Arena/wagner/"+version.ToString("00")+"/"+quality;
        foreach(string label in new[]{ArenaContentSetup.CategoryLabel,"Arena_wagner",QualityAssetResolver.LabelFor(quality)}){settings.AddLabel(label);address.SetLabel(label,true);}
        settings.RemoveAssetEntry(guid);EditorUtility.SetDirty(catalog);EditorUtility.SetDirty(settings);
    }
    static partial void ApplyWindows(Scene scene,List<Binding> bindings,string quality);
    static partial void BeginWindows();
}
#endif
