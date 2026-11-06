#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Authoring uses the same five-variant contract as Monteverdi. Runtime scenes contain only
// the chosen tier, and the existing ArenaSceneCatalog and Remote_Arenas group own loading.
public static class ComposerSceneryAssembly
{
    const string Root="Assets/_Project/Art/Arenas";
    const string ManifestPath="Assets/_Project/Configs/Editor/composer_scenery_manifest.json";
    const string CatalogPath="Assets/_Project/Resources/ArenaSceneCatalog.asset";
    static readonly string[] Q={"Low","Mid","HighMid","High","Ultra"};
    static readonly List<string> Report=new List<string>();
    [Serializable] public class Manifest { public SceneSpec[] scenes; }
    [Serializable] public class SceneSpec { public string composer,reference,albedo,bump,sky,specular; public AssetSpec[] assets; public Placement[] placements; public BoxSpec[] architecture; public LookSpec look; public ViewSpec view; }
    [Serializable] public class AssetSpec { public string composer,name,shader; public bool shared,velvetCorrection,preserveAspect; public float height,width,depth,pitch,emission; public Variant[] variants; }
    [Serializable] public class ViewSpec { public Vector3 position,target; public float fov=48; }
    [Serializable] public class LookSpec
    {
        public bool hideSky,halfPanorama;
        public Color ambient,equator,ground,sunColor,fillColor,floorTint;
        public Vector3 sunEuler;
        public float sunIntensity,fillIntensity,skyRotation,skyExposure=1,floorSmoothness=.7f,floorTiling=2,fogDensity;
        public float floorNormalStrength=.1f,skyVerticalOffset;
        public float panoramaHorizontalFov=100,panoramaVerticalFov=60;
        public LampSpec[] lamps;
    }
    [Serializable] public class LampSpec { public string name,type; public Vector3 position,target; public Color color; public float intensity,range,angle; public bool shadows,flame; }
    [Serializable] public class Variant { public string quality,model,textures,authoredModel; }
    [Serializable] public class BoxSpec { public string name; public float x,y,z,sx,sy,sz,r,g,b; }
    [Serializable] public class Placement { public string asset,name,wall; public float x,y,z,yaw,scale; }
    static string Folder(SceneSpec s)=>Root+"/"+s.composer;
    static string Master(SceneSpec s,int version=1)=>Folder(s)+"/"+s.composer+"_Arena_"+version.ToString("00")+".prefab";
    static string reviewComposer;
    public static void BuildOneForReview(string composer)
    { reviewComposer=composer; try { Build(); } finally { reviewComposer=null; } }
    static void Log(string line) { Report.Add(line); File.WriteAllLines("Logs/ComposerSceneryAssembly.txt",Report); Debug.Log(line); }
    public static void FinishVisualReviewFromCommandLine()
    {
        try {foreach(string composer in new[]{"Tchaikovsky","Vivaldi","Wagner","Mozart","Haydn"})BuildOneForReview(composer);ComposerArenaLifecycleProbe.RunFromCommandLine();}
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void RunFromCommandLine()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch(Exception e) { Log("FAIL "+e); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void RefineTextilesAndProbeFromCommandLine()
    {
        try
        {
            foreach(string composer in new[]{"Beethoven","Chopin","Handel","Haydn"})
            foreach(string path in AssetDatabase.FindAssets("t:Material",new[]{Root+"/"+composer+"/Props"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                if(!path.Contains("_curtain")&&!path.Contains("_cortinatges")) continue;
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                material.SetTexture("_MetallicGlossMap",null); material.DisableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_Metallic",0); material.SetFloat("_Smoothness",.35f); EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets(); ComposerArenaLifecycleProbe.RunFromCommandLine();
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void BuildSelectedAndProbeFromCommandLine()
    {
        try { Build(); ComposerArenaLifecycleProbe.RunFromCommandLine(); }
        catch(Exception e) { Log("FAIL "+e); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    [MenuItem("Tools/MusicGame/Content/Arenas/Build All Composer Arenas (01-03)")]
    public static void Build()
    {
        if(!Application.isBatchMode && UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new Exception("Save the current scene first.");
        Directory.CreateDirectory("Logs"); Report.Clear();
        var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        var args=Environment.GetCommandLineArgs();
        int onlyIndex=Array.IndexOf(args,"-sceneryComposer");
        string only=reviewComposer??(onlyIndex>=0&&onlyIndex+1<args.Length?args[onlyIndex+1]:null);
        if(only!=null && File.Exists("Logs/ComposerSceneryAssembly.txt")) Report.AddRange(File.ReadAllLines("Logs/ComposerSceneryAssembly.txt"));
        foreach(var s in manifest.scenes.Where(s=>only==null||s.composer==only))
        {
            if(s.composer=="Wagner"&&WagnerAuthoringTools.HasCanonical){WagnerAuthoringTools.BuildFromCanonical();continue;}
            if(s.assets.Any(a=>!a.shared&&(a.composer!=s.composer||!a.name.StartsWith((s.composer=="Tchaikovsky"?"Txaikovski":s.composer)+"_",StringComparison.OrdinalIgnoreCase)))) throw new Exception("Cross-composer asset in "+s.composer);
            if(s.placements.Any(p=>!s.assets.Any(a=>a.name==p.asset))) throw new Exception("Placement has no owned asset in "+s.composer);
            foreach(var asset in s.assets)
            {
                if(asset.variants==null||asset.variants.Length==0)throw new Exception("No exported model for "+asset.name+"; leave this object for the second pass.");
                foreach(var variant in asset.variants)
                {
                    string[] canonicalNames={"LOW","MEDIUM","MID-HIGH","HIGH","ULTRA"};
                    string canonical=Path.Combine(Path.GetDirectoryName(variant.model),"Textures",canonicalNames[Array.IndexOf(Q,variant.quality)]);
                    if(File.Exists(Path.Combine(canonical,"BaseColor.png")))variant.textures=canonical;

                    if((!File.Exists(variant.model)&&!File.Exists(Root+"/"+asset.composer+"/Props/"+asset.name+"/"+variant.quality+"/"+asset.name+"_"+variant.quality+".fbx"))||!string.Equals(Path.GetFileName(Path.GetDirectoryName(variant.model)),asset.name,StringComparison.OrdinalIgnoreCase))throw new Exception("Model source does not belong to "+asset.name);
                    if((!File.Exists(Path.Combine(variant.textures,"BaseColor.png"))&&!File.Exists(Root+"/"+asset.composer+"/Props/"+asset.name+"/"+variant.quality+"/BaseColor.png"))||!(string.Equals(Path.GetFullPath(variant.textures),Path.GetFullPath(Path.GetDirectoryName(variant.model)),StringComparison.OrdinalIgnoreCase)||Path.GetFullPath(variant.textures).StartsWith(Path.GetFullPath(Path.GetDirectoryName(variant.model))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new Exception("Missing or foreign textures for "+asset.name);
                }
            }
            ArenaDistanceQuality.Backup(s.composer);
            foreach(string shared in s.assets.Where(a=>a.shared).Select(a=>a.composer).Distinct()) ArenaDistanceQuality.Backup(shared);
            Log("START "+s.composer);
            ImportSources(s);
            var props=BuildProps(s);
            BuildMaster(s,props);
            CompileScenes(s);
            for(int version=2;version<=ArenaContentSetup.ArenasPerComposer;version++) { CompleteEmptyVersion(s,version); CompileScenes(s,version); }
            Log($"PASS {s.composer}: {s.assets.Length} asset types, {s.placements.Length} placements; five independent quality scenes.");
        }
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        var catalog=AssetDatabase.LoadAssetAtPath<ArenaSceneCatalog>(CatalogPath);
        foreach(var s in manifest.scenes.Where(s=>only==null||s.composer==only)) for(int version=1;version<=ArenaContentSetup.ArenasPerComposer;version++) foreach(string quality in Q)
        {
            string guid=AssetDatabase.AssetPathToGUID(Master(s,version));
            if(catalog.Resolve(guid,Q,Array.IndexOf(Q,quality),out string chosen)==null||chosen!=quality)
                throw new Exception("Catalog resolution: "+s.composer+" "+quality);
            if(settings.FindAssetEntry(guid)!=null) throw new Exception("Master still Addressable: "+s.composer);
        }
        Log("PASS catalog quality resolution for the processed composers and all three arena identities. No bake, bundle build or publication performed.");
        Log("Review the versioned manifest for current assets; inferred perimeter compositions require visual review. Variant fallback remains nearest-lower / nearest-higher.");
        Log("Bump sources are imported as generated normals; Wagner wet stone uses spatial roughness variation and controlled environment reflections.");
        ComposerReferenceFinishing.EnablePropInstancing();
        AssetDatabase.SaveAssets();
    }
    static void Copy(string source,string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        if(!File.Exists(source))
        {
            if(File.Exists(target)) {Log("RETAIN imported asset; source absent: "+source);return;}
            throw new FileNotFoundException("Missing source and imported asset",source);
        }
        if(!File.Exists(target)||new FileInfo(source).Length!=new FileInfo(target).Length||File.GetLastWriteTimeUtc(source)>File.GetLastWriteTimeUtc(target)) File.Copy(source,target,true);
    }
    static void ImportSources(SceneSpec s)
    {
        string folder=Folder(s);
        Directory.CreateDirectory(folder+"/Materials"); Directory.CreateDirectory(folder+"/Textures");
        // Copy only runtime FBX and the maps consumed by Lit; never copy intermediate pipeline work.
        foreach(var a in s.assets) foreach(var v in a.variants)
        {
            string dir=Root+"/"+a.composer+"/Props/"+a.name+"/"+v.quality;
            if((!File.Exists(v.model)||!File.Exists(Path.Combine(v.textures,"BaseColor.png")))&&File.Exists(dir+"/"+a.name+"_"+v.quality+".fbx")&&File.Exists(dir+"/BaseColor.png"))
            {Log("RETAIN complete imported variant; source export incomplete: "+a.name+" "+v.quality);continue;}

            Copy(string.IsNullOrEmpty(v.authoredModel)?v.model:v.authoredModel,dir+"/"+a.name+"_"+v.quality+".fbx");
            foreach(string map in new[]{"BaseColor","Normal","MetallicSmoothness"})
            {
                string src=Path.Combine(v.textures,map+".png");
                if(File.Exists(src)) Copy(src,dir+"/"+map+".png");
            }
        }
        if(s.composer!="Monteverdi")
        {
        if(!string.IsNullOrEmpty(s.albedo)) Copy(s.albedo,folder+"/Textures/"+s.composer+"_Floor_BaseColor.png");
        if(!string.IsNullOrEmpty(s.bump)) Copy(s.bump,folder+"/Textures/"+s.composer+"_Floor_Height.png");

        if(!string.IsNullOrEmpty(s.sky)) Copy(s.sky,folder+"/Textures/"+s.composer+"_Skybox.png");
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Log("Imported source files: "+s.composer);
    }
    static Texture2D Tex(string path,bool normal=false,bool linear=false,bool height=false,bool sky=false)
    {
        if(!File.Exists(path)) return null;
        var imp=AssetImporter.GetAtPath(path) as TextureImporter;
        if(imp==null) throw new Exception("Missing importer "+path);
        var type=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
        int max=sky?4096:ArenaDistanceQuality.TextureSize(path);
        bool changed=imp.textureType!=type||imp.sRGBTexture!=(!normal&&!linear)||imp.convertToNormalmap!=height||imp.maxTextureSize!=max||imp.mipmapEnabled==sky||imp.alphaSource!=TextureImporterAlphaSource.FromInput;
        imp.textureType=type; imp.sRGBTexture=!normal&&!linear; imp.convertToNormalmap=height;
        imp.heightmapScale=.035f; imp.maxTextureSize=max; imp.mipmapEnabled=!sky;
        imp.alphaSource=TextureImporterAlphaSource.FromInput;
        imp.wrapMode=sky?TextureWrapMode.Clamp:TextureWrapMode.Repeat;
        changed|=ArenaDistanceQuality.ApplyPlatformCaps(imp,max);
        if(changed) imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Material Mat(string path,string shader="Universal Render Pipeline/Lit")
    {
        var sh=Shader.Find(shader); if(!sh) throw new Exception("Missing shader "+shader);
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m) { m=new Material(sh); AssetDatabase.CreateAsset(m,path); } else m.shader=sh;
        return m;
    }
    static Bounds BoundsOf(GameObject root)
    {
        var rs=root.GetComponentsInChildren<Renderer>(true);
        if(rs.Length==0) throw new Exception("No renderers "+root.name);
        Bounds b=rs[0].bounds; foreach(var r in rs.Skip(1)) b.Encapsulate(r.bounds); return b;
    }
    static Dictionary<string,GameObject> BuildProps(SceneSpec s)
    {
        var props=new Dictionary<string,GameObject>();
        foreach(var a in s.assets)
        {
            var tiers=new Dictionary<string,GameObject>();
            foreach(var v in a.variants)
            {
                string dir=Root+"/"+a.composer+"/Props/"+a.name+"/"+v.quality;
                string modelPath=dir+"/"+a.name+"_"+v.quality+".fbx";
                var imp=(ModelImporter)AssetImporter.GetAtPath(modelPath);
                bool changed=imp.materialImportMode!=ModelImporterMaterialImportMode.None||imp.importCameras||imp.importLights||imp.importAnimation||imp.isReadable;
                imp.materialImportMode=ModelImporterMaterialImportMode.None; imp.importCameras=false; imp.importLights=false; imp.importAnimation=false; imp.isReadable=false;
                if(changed) imp.SaveAndReimport();
                var mat=Mat(dir+"/"+a.name+"_"+v.quality+".mat",!string.IsNullOrEmpty(a.shader)?a.shader:a.velvetCorrection?"MusicGame/Art/ScenarioVelvet":a.name.Equals("handel_scenario_finestra",StringComparison.OrdinalIgnoreCase)?"MusicGame/Art/WindowGlassCorrection":"Universal Render Pipeline/Lit");
                if(mat.HasProperty("_GlassColor")){mat.SetColor("_GlassColor",a.name.StartsWith("Chopin_")?new Color(1,.92f,.76f,1):new Color(.84f,.88f,.92f,1));mat.SetFloat("_GlassEmission",a.name.StartsWith("Chopin_")?1.1f:.8f);}
                mat.SetColor("_BaseColor",Color.white); mat.SetTexture("_BaseMap",Tex(dir+"/BaseColor.png"));
                if(a.emission>0) {mat.SetTexture("_EmissionMap",Tex(dir+"/BaseColor.png"));mat.SetColor("_EmissionColor",Color.white*a.emission);mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;mat.EnableKeyword("_EMISSION");}
                else {mat.SetColor("_EmissionColor",Color.black);mat.DisableKeyword("_EMISSION");}
                mat.SetTexture("_BumpMap",Tex(dir+"/Normal.png",true)); mat.SetFloat("_BumpScale",.65f); mat.EnableKeyword("_NORMALMAP");
                var metal=Tex(dir+"/MetallicSmoothness.png",false,true);
                mat.SetTexture("_MetallicGlossMap",metal); if(metal) mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness",.68f); mat.SetFloat("_Cull",(float)CullMode.Off); mat.doubleSidedGI=true;
                // Painted stained glass is dielectric. The generated metal map incorrectly marks
                // large coloured areas as metal, washing out the image with sky reflections.
                bool stone=a.name.Contains("_marble_")||a.name.Contains("_statue")||a.name.EndsWith("_bust");
                if(stone||a.name.Contains("_vitirine_")||(a.name.Contains("_curtain")&&!a.velvetCorrection)||a.name.Contains("_cortinatges"))
                {
                    mat.SetTexture("_MetallicGlossMap",null); mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetFloat("_Metallic",0); mat.SetFloat("_Smoothness",stone?.4f:a.name.Contains("_vitirine_")?.28f:.35f);
                }
                if(a.composer=="Wagner"&&(a.name.Contains("_curtain")||a.name.Contains("_valance"))) {mat.SetFloat("_Smoothness",.22f);mat.SetFloat("_BumpScale",.35f);}
                // Ivy cards share one cutout atlas and retain the standard URP lightmap/shadow passes.
                if(a.name.Equals("Pachelbel_scenario_HangingIvy",StringComparison.OrdinalIgnoreCase))
                {
                    mat.shader=Shader.Find("Universal Render Pipeline/Lit");
                    mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Shared/ReferenceFlora/ReferenceFlora_AlbedoAlpha.png"));
                    mat.SetTexture("_BumpMap",null);mat.DisableKeyword("_NORMALMAP");
                    mat.SetTexture("_MetallicGlossMap",null);mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetFloat("_Surface",0);mat.SetFloat("_AlphaClip",1);mat.SetFloat("_Cutoff",.42f);mat.SetFloat("_AlphaToMask",1);
                    mat.SetFloat("_Metallic",0);mat.SetFloat("_Smoothness",.08f);mat.SetFloat("_SpecularHighlights",0);mat.SetFloat("_EnvironmentReflections",0);
                    mat.EnableKeyword("_ALPHATEST_ON");mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.SetOverrideTag("RenderType","TransparentCutout");mat.renderQueue=2450;
                    var commonFlora=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Shared/ReferenceFlora/ReferenceFlora_Lit.mat");if(commonFlora)mat=commonFlora;
                }
                EditorUtility.SetDirty(mat);
                var root=new GameObject(a.name+"_"+v.quality);
                try
                {
                    // Keep dimension adjustment aligned to the arena axes. Scaling the rotated
                    // FBX itself exchanges height and depth on ceiling meshes pitched by 90 degrees.
                    var dimensions=new GameObject("ModelDimensions").transform;dimensions.SetParent(root.transform,false);
                    var mesh=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath)); mesh.transform.SetParent(dimensions,false); mesh.transform.localRotation=Quaternion.Euler(a.pitch,0,0);
                    var b=BoundsOf(root); float extent=a.width>0?b.size.x:b.size.y;
                    if(extent<.0001f) throw new Exception("Degenerate model "+modelPath);
                    dimensions.localScale*= (a.width>0?a.width:a.height)/extent;
                    b=BoundsOf(root);
                    var adjusted=dimensions.localScale;
                    if(!a.preserveAspect&&a.width>0&&a.height>0) adjusted.y*=a.height/b.size.y;
                    if(!a.preserveAspect&&a.depth>0) adjusted.z*=a.depth/b.size.z;
                    dimensions.localScale=adjusted; b=BoundsOf(root);
                    dimensions.localPosition-=new Vector3(b.center.x,b.min.y,b.center.z);
                    if(a.name.Equals("Pachelbel_scenario_HangingIvy",StringComparison.OrdinalIgnoreCase))dimensions.localPosition=new Vector3(-b.center.x,.84f-b.max.y,-1.616f*dimensions.localScale.z);
                    foreach(var r in mesh.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials=Enumerable.Repeat(mat,r.sharedMaterials.Length).ToArray();
                    tiers[v.quality]=PrefabUtility.SaveAsPrefabAsset(root,dir+"/"+a.name+"_"+v.quality+".prefab");
                }
                finally { Object.DestroyImmediate(root); }
            }
            var master=new GameObject(a.name);
            try
            {
                var selector=master.AddComponent<MonteverdiPropQuality>();
                for(int i=0;i<Q.Length;i++)
                {
                    int selected=Enumerable.Range(0,Q.Length).Where(k=>k<=i&&tiers.ContainsKey(Q[k])).DefaultIfEmpty(-1).Max();
                    if(selected<0) selected=Enumerable.Range(0,Q.Length).First(k=>tiers.ContainsKey(Q[k]));
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(tiers[Q[selected]]); instance.transform.SetParent(master.transform,false);
                    instance.SetActive(i==4); selector.variants[i]=instance;
                    if(selected!=i) Log($"FALLBACK {a.name}: {Q[i]} -> {Q[selected]}");
                }
                props[a.name]=PrefabUtility.SaveAsPrefabAsset(master,Root+"/"+a.composer+"/Props/"+a.name+"/"+a.name+".prefab");
            }
            finally { Object.DestroyImmediate(master); }
            Log("PROP "+a.name);
        }
        AssetDatabase.SaveAssets(); return props;
    }
    static void BuildMaster(SceneSpec s,Dictionary<string,GameObject> props)
    {
        if(s.composer=="Monteverdi")
        {
            var existing=PrefabUtility.LoadPrefabContents(Master(s));
            try
            {
                var scenery=existing.transform.Find("MonteverdiScenery");
                foreach(var p in s.placements)
                {
                    string name="Additional_"+p.name; var parent=scenery.Find(p.wall);
                    var previous=parent.Find(name); if(previous) Object.DestroyImmediate(previous.gameObject);
                    var inst=(GameObject)PrefabUtility.InstantiatePrefab(props[p.asset],existing.scene); inst.name=name;
                    inst.transform.SetParent(parent,false); inst.transform.localPosition=new Vector3(p.x,p.y,p.z);
                    inst.transform.localRotation=Quaternion.Euler(0,p.yaw,0); inst.transform.localScale=Vector3.one*p.scale;
                }
                ComposerReferenceFinishing.ApplyMonteverdi(existing,s);
                PrefabUtility.SaveAsPrefabAsset(existing,Master(s));
            }
            finally { PrefabUtility.UnloadPrefabContents(existing); }
            AssetDatabase.SaveAssets(); return;
        }
        string folder=Folder(s), prefix=folder+"/Textures/"+s.composer;
        var floor=Mat(folder+"/Materials/"+s.composer+"_Floor.mat",s.composer=="Wagner"?"MusicGame/Art/WagnerWetStone":"MusicGame/Art/ScenarioPolishedFloor");
        floor.SetTexture("_BaseMap",Tex(prefix+"_Floor_BaseColor.png")); floor.SetColor("_BaseColor",Color.white);
        floor.SetTexture("_BumpMap",Tex(prefix+"_Floor_Height.png",true,false,true)); floor.SetFloat("_BumpScale",.15f); floor.EnableKeyword("_NORMALMAP");
        floor.SetFloat("_WorkflowMode",1); floor.DisableKeyword("_SPECULAR_SETUP");
        floor.SetTexture("_SpecGlossMap",null); floor.SetTexture("_MetallicGlossMap",null); floor.DisableKeyword("_METALLICSPECGLOSSMAP");
        floor.SetFloat("_Metallic",0); floor.SetFloat("_Smoothness",s.composer=="Brahms"?.35f:.72f);
        // Monteverdi's central mosaic spans the plane once; tiled paving uses 2x2.
        floor.SetTextureScale("_BaseMap",s.composer=="Monteverdi"?Vector2.one:new Vector2(2,2));
        EditorUtility.SetDirty(floor);
        Material sky;
        if(!string.IsNullOrEmpty(s.sky))
        {
            sky=Mat(folder+"/Materials/"+s.composer+"_Skybox.mat",s.look!=null?"MusicGame/Art/SceneryBackdrop":"Skybox/Panoramic");
            sky.SetTexture("_MainTex",Tex(prefix+"_Skybox.png",sky:true)); sky.SetFloat("_Mapping",1); sky.SetFloat("_ImageType",0); sky.SetFloat("_Layout",0);
            sky.SetFloat("_Exposure",1); sky.SetFloat("_Rotation",0); sky.SetColor("_Tint",new Color(.5f,.5f,.5f,1));
        }
        else if(s.composer=="Bach") sky=null;
        else
        {
            sky=Mat(folder+"/Materials/"+s.composer+"_Skybox.mat","Skybox/Procedural");
            sky.SetFloat("_Exposure",.25f); sky.SetColor("_SkyTint",new Color(.15f,.18f,.23f)); sky.SetColor("_GroundColor",new Color(.08f,.07f,.06f));
        }
        if(s.look!=null)
        {
            floor.SetColor("_BaseColor",s.look.floorTint);floor.SetFloat("_Smoothness",s.look.floorSmoothness);
            floor.SetTextureScale("_BaseMap",Vector2.one*s.look.floorTiling);floor.SetFloat("_BumpScale",s.look.floorNormalStrength);EditorUtility.SetDirty(floor);
            if(sky) {sky.SetFloat("_Exposure",s.look.skyExposure);sky.SetFloat("_Rotation",s.look.skyRotation);sky.SetFloat("_ImageType",s.look.halfPanorama?1:0);if(sky.HasProperty("_MirrorOnBack"))sky.SetFloat("_MirrorOnBack",1);}
            if(sky&&sky.HasProperty("_VerticalOffset"))sky.SetFloat("_VerticalOffset",s.look.skyVerticalOffset);
            if(sky&&sky.HasProperty("_HorizontalFov")){sky.SetFloat("_HorizontalFov",s.look.panoramaHorizontalFov>0?s.look.panoramaHorizontalFov:100);sky.SetFloat("_VerticalFov",s.look.panoramaVerticalFov>0?s.look.panoramaVerticalFov:60);}
            if(s.look.hideSky)sky=null;
        }
        if(s.composer!="Wagner") floor.SetFloat("_EnvironmentReflectionStrength",.32f);
        if(s.composer=="Wagner") {floor.SetFloat("_EnvironmentReflectionStrength",.22f);floor.SetFloat("_Wetness",.8f);EditorUtility.SetDirty(floor);}
        if(sky) EditorUtility.SetDirty(sky);
        var arena=PrefabUtility.LoadPrefabContents(Master(s));
        try
        {
            var renderer=arena.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Floor"); renderer.sharedMaterial=floor;
            var env=arena.GetComponentInChildren<ArenaEnvironmentSettings>(true); if(!env) env=arena.AddComponent<ArenaEnvironmentSettings>();
            env.overrideSkybox=true; env.skybox=sky; env.cameraClearsToSkybox=sky!=null;
            if(s.composer!="Monteverdi")
            {
                env.overrideAmbient=true; env.ambientMode=AmbientMode.Trilight;
                env.ambientColor=new Color(.43f,.46f,.53f); env.ambientEquatorColor=new Color(.29f,.24f,.18f); env.ambientGroundColor=new Color(.13f,.11f,.09f); env.ambientIntensity=.7f;
                env.overrideMainLight=true; env.mainLightColor=new Color(1,.88f,.72f); env.mainLightIntensity=s.composer=="Bach"?.85f:1.25f;
                env.mainLightEuler=new Vector3(35,210,0); env.mainLightShadows=LightShadows.Soft; env.mainLightShadowStrength=.8f;
                env.overrideFog=true; env.fog=false;
                var old=arena.transform.Find("ComposerScenery"); if(old) Object.DestroyImmediate(old.gameObject);
                var scenery=new GameObject("ComposerScenery"); scenery.transform.SetParent(arena.transform,false);
                foreach(string wall in new[]{"North","South","East","West"}) new GameObject(wall).transform.SetParent(scenery.transform,false);
                if(s.architecture!=null) foreach(var box in s.architecture)
                {
                    var piece=GameObject.CreatePrimitive(PrimitiveType.Cube); piece.name=box.name;
                    piece.transform.SetParent(scenery.transform,false); piece.transform.localPosition=new Vector3(box.x,box.y,box.z);
                    piece.transform.localScale=new Vector3(box.sx,box.sy,box.sz); Object.DestroyImmediate(piece.GetComponent<Collider>());
                    var infill=Mat(folder+"/Materials/"+box.name+".mat"); infill.SetColor("_BaseColor",new Color(box.r,box.g,box.b)); infill.SetFloat("_Smoothness",.25f); EditorUtility.SetDirty(infill); piece.GetComponent<Renderer>().sharedMaterial=infill;
                }
                if(s.composer=="Beethoven")
                {
                    var terrace=GameObject.CreatePrimitive(PrimitiveType.Cube); terrace.name="RearStairLanding";
                    terrace.transform.SetParent(scenery.transform,false); terrace.transform.localPosition=new Vector3(0,.75f,13);
                    terrace.transform.localScale=new Vector3(18,1.9f,6); terrace.GetComponent<Renderer>().sharedMaterial=floor;
                    Object.DestroyImmediate(terrace.GetComponent<Collider>());
                }
                foreach(var p in s.placements)
                {
                    var inst=(GameObject)PrefabUtility.InstantiatePrefab(props[p.asset],arena.scene); inst.name=p.name;
                    inst.transform.SetParent(scenery.transform.Find(p.wall),false); inst.transform.localPosition=new Vector3(p.x,p.y,p.z);
                    inst.transform.localRotation=Quaternion.Euler(0,p.yaw,0); inst.transform.localScale=Vector3.one*p.scale;
                    // Asset bounds can differ between generated tiers. Audit every tier separately.
                    var sel=inst.GetComponent<MonteverdiPropQuality>();
                    foreach(var variant in sel.variants)
                    {
                        var b=BoundsOf(variant);
                        if(b.min.x<4.5f&&b.max.x> -4.5f&&b.min.z<2.2f&&b.max.z> -2.2f&&b.min.y<3)
                            throw new Exception("Fight clearance overlap "+s.composer+" "+p.name+" "+b);
                    }
                }
                var lighting=arena.transform.Find("Lighting");
                var oldFill=lighting.Find("SceneryFill"); if(oldFill) Object.DestroyImmediate(oldFill.gameObject);
                var fill=new GameObject("SceneryFill").AddComponent<Light>(); fill.transform.SetParent(lighting,false);
                fill.type=LightType.Directional; fill.transform.localRotation=Quaternion.Euler(25,15,0); fill.intensity=.18f; fill.color=new Color(.7f,.79f,1); fill.shadows=LightShadows.None;
                if(s.composer=="Bach")
                {
                    env.ambientColor=new Color(.32f,.29f,.24f); env.ambientEquatorColor=new Color(.24f,.21f,.17f);
                    env.ambientGroundColor=new Color(.1f,.09f,.08f); fill.intensity=.4f; fill.color=new Color(1,.82f,.59f);
                    foreach(var oldLamp in lighting.GetComponentsInChildren<Light>(true).Where(l=>l.name.StartsWith("BachInteriorLamp")).ToArray()) Object.DestroyImmediate(oldLamp.gameObject);
                    foreach(float x in new[]{-7f,7f}) foreach(float z in new[]{-4f,4f})
                    {
                        var lamp=new GameObject("BachInteriorLamp").AddComponent<Light>(); lamp.transform.SetParent(lighting,false);
                        lamp.transform.localPosition=new Vector3(x,4,z); lamp.type=LightType.Point; lamp.range=12; lamp.intensity=5;
                        lamp.color=new Color(1,.66f,.32f); lamp.shadows=LightShadows.None;
                    }
                }
                if(s.look!=null)
                {
                    env.ambientColor=s.look.ambient;env.ambientEquatorColor=s.look.equator;env.ambientGroundColor=s.look.ground;
                    env.mainLightIntensity=s.look.sunIntensity;env.mainLightColor=s.look.sunColor;env.mainLightEuler=s.look.sunEuler;
                    env.fog=s.look.fogDensity>0;env.fogDensity=s.look.fogDensity;env.fogColor=s.look.ground;
                    fill.intensity=s.look.fillIntensity;fill.color=s.look.fillColor;
                    foreach(var lamp in lighting.GetComponentsInChildren<Light>(true).Where(l=>l.name.StartsWith("Reference_")||l.name.StartsWith("BachInteriorLamp")).ToArray())Object.DestroyImmediate(lamp.gameObject);
                    if(s.look.lamps!=null)foreach(var spec in s.look.lamps)
                    {
                        var lamp=new GameObject("Reference_"+spec.name).AddComponent<Light>();lamp.transform.SetParent(lighting,false);
                        lamp.transform.localPosition=spec.position;lamp.type=spec.type=="spot"?LightType.Spot:LightType.Point;
                        if(lamp.type==LightType.Spot)lamp.transform.LookAt(spec.target);lamp.color=spec.color;lamp.intensity=spec.intensity;lamp.range=spec.range;lamp.spotAngle=spec.angle>0?spec.angle:65;
                        lamp.shadows=spec.shadows?LightShadows.Soft:LightShadows.None;
                        if(s.composer=="Wagner"&&lamp.shadows!=LightShadows.None)
                        {
                            var additional=lamp.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                            var serializedLight=new SerializedObject(additional);serializedLight.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue=0;serializedLight.ApplyModifiedPropertiesWithoutUndo();
                        }
                        if(spec.flame)AddBrazierFlame(lamp.transform,s.composer=="Wagner");
                    }
                }
                var oldProbe=lighting.Find("SceneryReflection"); if(oldProbe) Object.DestroyImmediate(oldProbe.gameObject);
                var probe=new GameObject("SceneryReflection").AddComponent<ReflectionProbe>(); probe.transform.SetParent(lighting,false); probe.transform.localPosition=new Vector3(0,3,0);
                probe.mode=ReflectionProbeMode.Realtime; probe.refreshMode=ReflectionProbeRefreshMode.ViaScripting; probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
                probe.resolution=s.composer=="Wagner"?256:128; probe.size=new Vector3(24,15,24); probe.boxProjection=true;
            }
            if(arena.GetComponentsInChildren<Camera>(true).Length!=0) throw new Exception("Unexpected camera "+s.composer);
            ComposerAuditoriumFinish.Apply(arena,s.composer);
            PrefabUtility.SaveAsPrefabAsset(arena,Master(s));
        }
        finally { PrefabUtility.UnloadPrefabContents(arena); }
        AssetDatabase.SaveAssets();
    }
    static void AddBrazierFlame(Transform parent,bool refined=false)
    {
        string folder=Root+"/_Shared/Fire";Directory.CreateDirectory(folder);
        string texturePath=folder+"/SoftFlame.png";
        if(!File.Exists(texturePath))
        {
            var image=new Texture2D(32,32,TextureFormat.RGBA32,false);
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                float dx=(x-15.5f)/15.5f,dy=(y-15.5f)/15.5f;
                float alpha=Mathf.Pow(Mathf.Clamp01(1-dx*dx-dy*dy),2);
                image.SetPixel(x,y,new Color(1,1,1,alpha));
            }
            image.Apply();File.WriteAllBytes(texturePath,image.EncodeToPNG());Object.DestroyImmediate(image);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);importer.maxTextureSize=64;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
        var material=Mat(folder+(refined?"/WagnerFlame.mat":"/BrazierFlame.mat"),refined?"MusicGame/Art/WagnerFlame":"Universal Render Pipeline/Particles/Unlit");
        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));material.SetColor("_BaseColor",new Color(2,2,2,1));
        material.SetFloat("_Surface",1);material.SetFloat("_Blend",1);material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.One);material.SetFloat("_ZWrite",0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.SetOverrideTag("RenderType","Transparent");material.renderQueue=(int)RenderQueue.Transparent;EditorUtility.SetDirty(material);
        var particles=new GameObject("BrazierFlame").AddComponent<ParticleSystem>();particles.transform.SetParent(parent,false);particles.transform.localRotation=Quaternion.Euler(-90,0,0);
        var main=particles.main;main.loop=true;main.duration=1;main.startLifetime=new ParticleSystem.MinMaxCurve(.35f,.75f);main.startSpeed=new ParticleSystem.MinMaxCurve(.65f,1.25f);main.startSize=new ParticleSystem.MinMaxCurve(.18f,.38f);main.maxParticles=32;
        var emission=particles.emission;emission.rateOverTime=36;
        var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.radius=.12f;shape.angle=8;
        var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(1,1,.6f),0),new GradientColorKey(new Color(1,.35f,.025f),.5f),new GradientColorKey(new Color(.8f,.05f,.005f),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});
        var color=particles.colorOverLifetime;color.enabled=true;color.color=gradient;
        var size=particles.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,1,1,0));
        var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.renderMode=ParticleSystemRenderMode.Stretch;renderer.lengthScale=1;renderer.velocityScale=.4f;
        if(refined) { main.startLifetime=new ParticleSystem.MinMaxCurve(.45f,.8f);main.startSpeed=new ParticleSystem.MinMaxCurve(.35f,.65f);main.startSize=new ParticleSystem.MinMaxCurve(.35f,.65f);main.startColor=Color.white;emission.rateOverTime=22;shape.radius=.16f;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.lengthScale=1;var refinedColor=particles.colorOverLifetime;refinedColor.enabled=false; }
    }
    public static void FinishReferenceDetailsFromCommandLine()
    {
        try{foreach(string composer in new[]{"Vivaldi","Wagner"})BuildOneForReview(composer);ComposerArenaLifecycleProbe.RunFromCommandLine();}
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void CompleteEmptyVersion(SceneSpec s,int version)
    {
        const string marker="GeneratedFrom_Arena_01";
        string target=Master(s,version);
        var existing=PrefabUtility.LoadPrefabContents(target);
        bool derived=false;
        try
        {
            var renderers=existing.GetComponentsInChildren<Renderer>(true);
            derived=existing.transform.Find(marker)!=null;
            bool placeholder=renderers.Length==1&&renderers[0].name=="Floor"
                &&existing.GetComponentsInChildren<Light>(true).Length==0
                &&existing.GetComponentsInChildren<MonteverdiPropQuality>(true).Length==0
                &&existing.transform.Find("Interactables").childCount==0;
            if(!derived&&!placeholder) {Log("PRESERVE authored arena "+target);return;}
        }
        finally {PrefabUtility.UnloadPrefabContents(existing);}
        string guid=AssetDatabase.AssetPathToGUID(target);
        var source=PrefabUtility.LoadPrefabContents(Master(s));
        try
        {
            source.name=s.composer+"_Arena_"+version.ToString("00");
            new GameObject(marker).transform.SetParent(source.transform,false);
            PrefabUtility.SaveAsPrefabAsset(source,target);
        }
        finally {PrefabUtility.UnloadPrefabContents(source);}
        if(AssetDatabase.AssetPathToGUID(target)!=guid)throw new Exception("Arena identity changed: "+target);
        Log("DERIVED "+target+" from the reference composition; GUID retained.");
    }
    static void CompileScenes(SceneSpec s,int version=1)
    {
        string folder=Folder(s)+"/Scenes"; Directory.CreateDirectory(folder); AssetDatabase.Refresh();
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        var group=settings.FindGroup(ArenaContentSetup.GroupName); if(group==null) throw new Exception("Missing Remote_Arenas");
        var catalog=AssetDatabase.LoadAssetAtPath<ArenaSceneCatalog>(CatalogPath);
        if(!catalog) throw new Exception("Missing catalog");
        catalog.hideFlags|=HideFlags.DontUnloadUnusedAsset;
        string guid=AssetDatabase.AssetPathToGUID(Master(s,version)); var entry=catalog.Find(guid);
        if(entry==null) {entry=new ArenaSceneCatalog.Entry{sourceArenaGuid=guid};catalog.entries.Add(entry);} entry.variants.Clear();
        string signature=null;
        foreach(string quality in Q)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Master(s,version)),scene);
            PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.OutermostRoot,InteractionMode.AutomatedAction);
            var selectors=root.GetComponentsInChildren<MonteverdiPropQuality>(true);
            var expected=new HashSet<string>();
            long uniformProps=selectors.Sum(sel=>ArenaDistanceQuality.PropTriangles(sel.variants[Array.IndexOf(Q,quality)]));
            long selectedProps=0;
            foreach(var sel in selectors)
            {
                var chosen=sel.variants[ArenaDistanceQuality.Index(s.composer,sel,Array.IndexOf(Q,quality))]; string path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(chosen);
                if(string.IsNullOrEmpty(path)) throw new Exception("Missing quality prefab "+sel.name);
                foreach(string dep in AssetDatabase.GetDependencies(path,true).Where(p=>p.EndsWith(".fbx"))) expected.Add(dep);
                var inst=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
                inst.name=sel.name; inst.transform.SetParent(sel.transform.parent,false);
                inst.transform.localPosition=sel.transform.localPosition; inst.transform.localRotation=sel.transform.localRotation; inst.transform.localScale=sel.transform.localScale;
                inst.SetActive(true); selectedProps+=ArenaDistanceQuality.PropTriangles(inst); Object.DestroyImmediate(sel.gameObject);
            }
            string sceneryName=s.composer=="Monteverdi"?"MonteverdiScenery":"ComposerScenery";
            var scenery=root.transform.Find(sceneryName);
            string shape=scenery==null?"":string.Join("\n",scenery.GetComponentsInChildren<Transform>(true).Where(t=>t.parent!=null&&new[]{"North","South","East","West"}.Contains(t.parent.name)).Select(t=>$"{t.parent.name}/{t.name}|{t.localPosition:F5}|{t.localRotation:F5}|{t.localScale:F5}").OrderBy(x=>x));
            if(signature==null) signature=shape; else if(shape!=signature) throw new Exception("Composition drift "+s.composer+quality);
            var env=root.GetComponentInChildren<ArenaEnvironmentSettings>(true);
            var sun=new GameObject("ArenaSun").AddComponent<Light>(); sun.transform.SetParent(root.transform.Find("Lighting"),false);
            sun.type=LightType.Directional; sun.color=env.mainLightColor; sun.intensity=env.mainLightIntensity; sun.transform.rotation=Quaternion.Euler(env.mainLightEuler); sun.shadows=env.mainLightShadows; sun.shadowStrength=env.mainLightShadowStrength;
            var sceneLighting=root.GetComponent<ArenaSceneLighting>(); if(!sceneLighting) sceneLighting=root.AddComponent<ArenaSceneLighting>(); sceneLighting.mainLight=sun;
            RenderSettings.sun=sun; RenderSettings.skybox=env.skybox; RenderSettings.ambientMode=env.ambientMode; RenderSettings.ambientSkyColor=env.ambientColor;
            RenderSettings.ambientEquatorColor=env.ambientEquatorColor; RenderSettings.ambientGroundColor=env.ambientGroundColor; RenderSettings.ambientIntensity=env.ambientIntensity; RenderSettings.fog=env.fog; RenderSettings.fogColor=env.fogColor; RenderSettings.fogDensity=env.fogDensity;
            if(s.composer=="Wagner")foreach(var light in root.GetComponentsInChildren<Light>(true).Where(l=>l.type==LightType.Point&&l.name.StartsWith("Reference_")))
                light.shadows=Array.IndexOf(Q,quality)>=3?LightShadows.Soft:LightShadows.None;
            string scenePath=folder+"/"+s.composer+"_Arena_"+version.ToString("00")+"_"+quality+".unity";
            EditorSceneManager.SaveScene(scene,scenePath);
            ReferenceFloraUpgrade.ApplyForCurrentScene(s.composer);ScenarioBotanicalUpgrade.ApplyForCurrentScene(s.composer);MonteverdiCombatLookSetup.ApplyForCurrentScene(s.composer);MonteverdiShadowSetup.ApplyForCurrentScene(s.composer);MonteverdiSky360Setup.ApplyForCurrentScene(s.composer);EditorSceneManager.SaveScene(scene,scenePath);
            string sceneGuid=AssetDatabase.AssetPathToGUID(scenePath); var address=settings.CreateOrMoveEntry(sceneGuid,group);
            string id=s.composer.ToLowerInvariant(); address.address=$"Arena/{id}/{version:00}/{quality}";
            foreach(string label in new[]{ArenaContentSetup.CategoryLabel,"Arena_"+id,QualityAssetResolver.LabelFor(quality)}) {settings.AddLabel(label);address.SetLabel(label,true);}
            entry.variants.Add(new ArenaSceneCatalog.Variant{qualityName=quality,scene=new AssetReference(sceneGuid)});
            var deps=AssetDatabase.GetDependencies(scenePath,true);
            if(deps.Contains(Master(s,version))||deps.Any(p=>p.EndsWith("MonteverdiPropQuality.cs"))) throw new Exception("Master/selector leaked "+scenePath);
            var actual=new HashSet<string>(deps.Where(p=>p.EndsWith(".fbx")&&p.Contains("/Props/")));
            if(!actual.Where(p=>!ScenarioBotanicalUpgrade.BotanicalSourceDependency(p)&&!MonteverdiCombatLookSetup.ReplacedCandleSourceDependency(p)).ToHashSet().SetEquals(expected.Where(p=>!ScenarioBotanicalUpgrade.BotanicalSourceDependency(p)&&!MonteverdiCombatLookSetup.ReplacedCandleSourceDependency(p)))) throw new Exception("Unexpected quality dependencies "+scenePath);
            if(actual.Any(p=>!ScenarioBotanicalUpgrade.ReusedContainerDependency(p)&&!s.assets.Select(a=>Root+"/"+a.composer+"/Props/").Append(Folder(s)+"/Props/").Any(prefix=>p.StartsWith(prefix,StringComparison.Ordinal))))throw new Exception("Foreign composer FBX: "+scenePath);
            if(root.GetComponentsInChildren<MonteverdiPropQuality>(true).Length!=0) throw new Exception("Unstripped selector");
            ArenaDistanceQuality.Measure(root,s.composer+"_"+version.ToString("00"),quality,uniformProps,selectedProps);
            if(version==1) Preview(s,root,quality);
            Log($"SCENE {s.composer} {quality}: {selectors.Length} placements, {actual.Count} FBX, composition/dependencies verified");
        }
        settings.RemoveAssetEntry(guid); catalog.hideFlags=HideFlags.None; EditorUtility.SetDirty(catalog); EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
    }
    static void Preview(SceneSpec s,GameObject root,string quality)
    {
        string folder="Logs/ComposerSceneryPreviews/"+s.composer+"/"+quality; Directory.CreateDirectory(folder);
        var go=new GameObject("PreviewCamera"); var camera=go.AddComponent<Camera>(); camera.clearFlags=CameraClearFlags.Skybox; camera.fieldOfView=62; camera.nearClipPlane=.1f; camera.farClipPlane=400;
        var positions=new[]{new Vector3(0,2.6f,-7.6f),new Vector3(7.4f,4,-6.5f),new Vector3(-7.4f,4,6.5f)};
        var targets=new[]{new Vector3(0,3.6f,6),new Vector3(-2,3.5f,4),new Vector3(2,3.5f,-3)};
        if(s.view!=null){positions[0]=s.view.position;targets[0]=s.view.target;camera.fieldOfView=s.view.fov;}
        try
        {
            foreach(var particles in root.GetComponentsInChildren<ParticleSystem>(true))particles.Simulate(.6f,true,true);
            for(int i=0;i<positions.Length;i++)
            {
                camera.transform.position=positions[i]; camera.transform.LookAt(targets[i]);
                var rt=new RenderTexture(1200,800,24); var tex=new Texture2D(1200,800,TextureFormat.RGB24,false);
                var previous=RenderTexture.active;
                try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; tex.ReadPixels(new Rect(0,0,1200,800),0,0); tex.Apply(); File.WriteAllBytes(folder+"/view_"+i+".png",tex.EncodeToPNG()); }
                finally { camera.targetTexture=null; RenderTexture.active=previous; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
            }
        }
        finally { Object.DestroyImmediate(go); }
    }
}
#endif
