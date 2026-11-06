#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Editor-only policy: no asset references are introduced into runtime bundles.
public static class ArenaDistanceQuality
{
    const string PathName="Assets/_Project/Configs/Editor/arena_distance_quality.json";
    [Serializable] public class Policy { public SceneRule[] scenes; public int[] textureSizes; public PlatformCap[] platformTextureCaps; }
    [Serializable] public class PlatformCap {public string platform;public int maximum;}
    [Serializable] public class SceneRule {
        public string composer; public Vector3 center;
        public float halfWidth,halfDepth,near,middle,far;
        public ExceptionRule[] exceptions;
    }
    [Serializable] public class ExceptionRule { public string objectName,prefabName; public int forcedBand=-1; public int minimumQuality; }
    public static bool ApplyPlatformCaps(TextureImporter importer,int maximum)
    {
        var policy=JsonUtility.FromJson<Policy>(File.ReadAllText(PathName));
        bool changed=false;
        foreach(var cap in policy.platformTextureCaps??Array.Empty<PlatformCap>())
        {
            var settings=importer.GetPlatformTextureSettings(cap.platform);
            int allowed=Mathf.Min(maximum,cap.maximum);
            if(!settings.overridden&&allowed>=maximum)continue;
            if(settings.overridden&&settings.maxTextureSize==allowed)continue;
            if(!settings.overridden){settings.format=TextureImporterFormat.Automatic;settings.textureCompression=TextureImporterCompression.Compressed;}
            settings.overridden=true;settings.maxTextureSize=allowed;
            importer.SetPlatformTextureSettings(settings);changed=true;
        }
        return changed;
    }
    [MenuItem("Tools/MusicGame/Content/Arenas/Apply Platform Texture Caps")]
    public static void ApplyAllTexturePlatformCaps()
    {
        var policy=JsonUtility.FromJson<Policy>(File.ReadAllText(PathName));
        foreach(var rule in policy.scenes)Backup(rule.composer);
        var report=new System.Collections.Generic.List<string>();
        foreach(string path in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/_Project/Art/Arenas"}).Select(AssetDatabase.GUIDToAssetPath))
        {
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer!=null)
            {
                int maximum=path.Contains("/Props/")?TextureSize(path):importer.maxTextureSize;
                bool changed=importer.maxTextureSize!=maximum;importer.maxTextureSize=maximum;
                changed|=ApplyPlatformCaps(importer,maximum);
                if(changed){importer.SaveAndReimport();report.Add(path);}
            }
        }
        Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/ArenaPlatformTextureChanges.txt",report);
    }
    public static int TextureSize(string path)
    {
        var policy=JsonUtility.FromJson<Policy>(File.ReadAllText(PathName));
        var names=new[]{"Low","Mid","HighMid","High","Ultra"};
        for(int i=0;i<names.Length;i++)
            if(path.Contains("/Props/") && path.Contains("/"+names[i]+"/"))
                return policy.textureSizes[i];
        return 2048;
    }
    public static void Backup(string composer)
    {
        string destination=System.IO.Path.GetFullPath("../tools/scenario-backups/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+composer);
        foreach(string folder in new[]{"Assets/_Project/Art/Arenas/"+composer,"Assets/_Project/Resources","Assets/AddressableAssetsData","Assets/_Project/Configs/Editor"})
        foreach(string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories))
        {
            string extension=System.IO.Path.GetExtension(file).ToLowerInvariant();
            if(!new[]{".unity",".prefab",".asset",".mat",".meta",".json"}.Contains(extension))continue;
            string target=System.IO.Path.Combine(destination,file);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));File.Copy(file,target,false);
        }
        Debug.Log("Recoverable scene/importer backup: "+destination);
    }
    [MenuItem("Tools/MusicGame/Content/Arenas/Audit Distance Quality (No Scene Changes)")]
    public static void Audit()
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/ArenaDistanceQuality.csv","composer,quality,object,distance,band,target,selectedSlot,actualQuality,prefabPath\n");
        var policy=JsonUtility.FromJson<Policy>(File.ReadAllText(PathName));
        foreach(var rule in policy.scenes)
        {
            string path="Assets/_Project/Art/Arenas/"+rule.composer+"/"+rule.composer+"_Arena_01.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try { foreach(var selector in root.GetComponentsInChildren<MonteverdiPropQuality>(true))for(int q=0;q<5;q++)Index(rule.composer,selector,q); }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        Debug.Log("Distance quality preview: Logs/ArenaDistanceQuality.csv. No scenes modified.");
    }
    [MenuItem("Tools/MusicGame/Content/Arenas/Refresh Generated Scene Metrics")]
    public static void RefreshGeneratedSceneMetrics()
    {
        var prior=new System.Collections.Generic.Dictionary<string,long[]>();
        const string path="Logs/ArenaSceneMetrics.csv";
        if(File.Exists(path))foreach(string line in File.ReadAllLines(path))
        {
            var row=line.Split(',');
            if(row.Length==9&&long.TryParse(row[7],out long uniform)&&long.TryParse(row[8],out long selected))prior[row[0]+"/"+row[1]]=new[]{uniform,selected};
        }
        if(File.Exists(path))File.Copy(path,path+".history",true);
        File.WriteAllText(path,"composer,quality,renderedTriangles,uniqueMeshes,uniqueMaterials,uniqueTextures,editorTextureBytes,uniformQualityPropTriangles,selectedPropTriangles\n");
        var setup=UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var policy=JsonUtility.FromJson<Policy>(File.ReadAllText(PathName));
            foreach(var rule in policy.scenes)for(int version=1;version<=3;version++)foreach(string quality in new[]{"Low","Mid","HighMid","High","Ultra"})
            {
                string id=rule.composer+"_"+version.ToString("00");
                string scenePath="Assets/_Project/Art/Arenas/"+rule.composer+"/Scenes/"+rule.composer+"_Arena_"+version.ToString("00")+"_"+quality+".unity";
                var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,UnityEditor.SceneManagement.OpenSceneMode.Single);
                var root=scene.GetRootGameObjects().Single(g=>g.GetComponentInChildren<ArenaEnvironmentSettings>(true)!=null);
                if(!prior.TryGetValue(id+"/"+quality,out long[] baseline))throw new Exception("Missing uniform geometry baseline: "+id+" "+quality);
                Measure(root,id,quality,baseline[0],baseline[1]);
            }
        }
        finally{if(setup.Any(x=>x.isLoaded)&&setup.Any(x=>x.isActive))UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(setup);else UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);}
    }
    public static long PropTriangles(GameObject root)
    {
        long triangles=0;
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh=filter.sharedMesh;if(!mesh)continue;
            for(int i=0;i<mesh.subMeshCount;i++)if(mesh.GetTopology(i)==MeshTopology.Triangles)triangles+=(long)mesh.GetIndexCount(i)/3;
        }
        return triangles;
    }
    public static void Measure(GameObject root,string composer,string quality,long uniformProps=0,long selectedProps=0)
    {
        if(root.GetComponentsInChildren<LODGroup>(true).Length>0)throw new Exception("Runtime LODGroup: "+composer);
        foreach(var t in root.GetComponentsInChildren<Transform>(true))
            if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script: "+t.name);
        var meshes=root.GetComponentsInChildren<MeshFilter>(false).Select(f=>f.sharedMesh)
            .Concat(root.GetComponentsInChildren<SkinnedMeshRenderer>(false).Select(r=>r.sharedMesh)).Where(m=>m!=null).ToArray();
        long triangles=0;
        foreach(var mesh in meshes)for(int i=0;i<mesh.subMeshCount;i++)
            if(mesh.GetTopology(i)==MeshTopology.Triangles)triangles+=(long)mesh.GetIndexCount(i)/3;
        var materials=root.GetComponentsInChildren<Renderer>(false).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
        var textures=materials.SelectMany(m=>m.GetTexturePropertyNames().Select(n=>m.GetTexture(n))).Where(t=>t!=null).Distinct().ToArray();
        long bytes=textures.Sum(t=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
        string path="Logs/ArenaSceneMetrics.csv";
        if(!File.Exists(path))File.WriteAllText(path,"composer,quality,renderedTriangles,uniqueMeshes,uniqueMaterials,uniqueTextures,editorTextureBytes,uniformQualityPropTriangles,selectedPropTriangles\n");
        File.AppendAllText(path,$"{composer},{quality},{triangles},{meshes.Distinct().Count()},{materials.Length},{textures.Length},{bytes},{uniformProps},{selectedProps}\n");
    }
    public static int Index(string composer, MonteverdiPropQuality selector, int quality)
    {
        var policy=JsonUtility.FromJson<Policy>(File.ReadAllText(PathName));
        var rule=policy.scenes.Single(s=>s.composer==composer);
        if(rule.halfWidth<=0||rule.halfDepth<=0||rule.near<0||rule.middle<=rule.near||rule.far<=rule.middle)
            throw new Exception("Invalid distance policy: "+composer);
        // Use the same highest authored tier for bounds at every output quality.
        var reference=selector.variants.LastOrDefault(v=>v!=null);
        if(!reference) throw new Exception("No variants: "+selector.name);
        var renderers=reference.GetComponentsInChildren<Renderer>(true);
        if(renderers.Length==0) throw new Exception("No bounds: "+selector.name);
        var bounds=renderers[0].bounds;
        foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        float dx=Mathf.Max(0,Mathf.Abs(bounds.center.x-rule.center.x)-bounds.extents.x-rule.halfWidth);
        float dz=Mathf.Max(0,Mathf.Abs(bounds.center.z-rule.center.z)-bounds.extents.z-rule.halfDepth);
        float distance=Mathf.Sqrt(dx*dx+dz*dz);
        int band=distance<=rule.near?0:distance<=rule.middle?1:distance<=rule.far?2:3;
        string sourcePath=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(reference);
        string prefabKey=string.IsNullOrEmpty(sourcePath)?"":System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(sourcePath)));
        var exception=rule.exceptions?.SingleOrDefault(x=>!string.IsNullOrEmpty(x.objectName)&&x.objectName==selector.name)
            ??rule.exceptions?.SingleOrDefault(x=>string.IsNullOrEmpty(x.objectName)&&!string.IsNullOrEmpty(x.prefabName)&&x.prefabName==prefabKey);
        if(exception!=null && exception.forcedBand>=0) band=Mathf.Clamp(exception.forcedBand,0,3);
        int target=Mathf.Clamp(Mathf.Max(quality-band,exception?.minimumQuality??0),0,quality);
        int selected=target;
        while(selected>=0 && !selector.variants[selected]) selected--;
        if(selected<0) selected=Array.FindIndex(selector.variants,v=>v!=null);
        if(selected<0) throw new Exception("No safe variant: "+selector.name);
        Directory.CreateDirectory("Logs");
        string selectedPath=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selector.variants[selected]);
        string actualQuality=System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(selectedPath));
        File.AppendAllText("Logs/ArenaDistanceQuality.csv",$"{composer},{quality},{selector.name},{distance.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)},{band},{target},{selected},{actualQuality},{selectedPath}\n");
        return selected;
    }
}
#endif
