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
    const string ManifestPath=Root+"/composer_scenery_manifest.json";
    const string CatalogPath="Assets/_Project/Resources/ArenaSceneCatalog.asset";
    static readonly string[] Q={"Low","Mid","HighMid","High","Ultra"};
    static readonly List<string> Report=new List<string>();
    [Serializable] public class Manifest { public SceneSpec[] scenes; }
    [Serializable] public class SceneSpec { public string composer,reference,albedo,bump,sky,specular; public AssetSpec[] assets; public Placement[] placements; public BoxSpec[] architecture; public LookSpec look; public ViewSpec view; }
    [Serializable] public class AssetSpec { public string composer,name; public float height,width,depth,pitch,emission; public Variant[] variants; }
    [Serializable] public class ViewSpec { public Vector3 position,target; public float fov=48; }
    [Serializable] public class LookSpec
    {
        public bool hideSky,halfPanorama;
        public Color ambient,equator,ground,sunColor,fillColor,floorTint;
        public Vector3 sunEuler;
        public float sunIntensity,fillIntensity,skyRotation,skyExposure=1,floorSmoothness=.7f,floorTiling=2,fogDensity;
        public float panoramaHorizontalFov=100,panoramaVerticalFov=60;
        public LampSpec[] lamps;
    }
    [Serializable] public class LampSpec { public string name,type; public Vector3 position,target; public Color color; public float intensity,range,angle; public bool shadows; }
    [Serializable] public class Variant { public string quality,model,textures; }
    [Serializable] public class BoxSpec { public string name; public float x,y,z,sx,sy,sz,r,g,b; }
    [Serializable] public class Placement { public string asset,name,wall; public float x,y,z,yaw,scale; }
    static string Folder(SceneSpec s)=>Root+"/"+s.composer;
    static string Master(SceneSpec s)=>Folder(s)+"/"+s.composer+"_Arena_01.prefab";
    static string reviewComposer;
    public static void BuildOneForReview(string composer)
    { reviewComposer=composer; try { Build(); } finally { reviewComposer=null; } }
    static void Log(string line) { Report.Add(line); File.WriteAllLines("Logs/ComposerSceneryAssembly.txt",Report); Debug.Log(line); }
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
    [MenuItem("Tools/MusicGame/Content/Arenas/Build All First Composer Arenas")]
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
            if(s.assets.Any(a=>a.composer!=s.composer||!a.name.StartsWith(s.composer+"_",StringComparison.OrdinalIgnoreCase))) throw new Exception("Cross-composer asset in "+s.composer);
            if(s.placements.Any(p=>!s.assets.Any(a=>a.name==p.asset))) throw new Exception("Placement has no owned asset in "+s.composer);
            foreach(var asset in s.assets)
            {
                if(asset.variants==null||asset.variants.Length==0)throw new Exception("No exported model for "+asset.name+"; leave this object for the second pass.");
                foreach(var variant in asset.variants)
                {
                    if(!File.Exists(variant.model)||!string.Equals(Path.GetFileName(Path.GetDirectoryName(variant.model)),asset.name,StringComparison.OrdinalIgnoreCase))throw new Exception("Model source does not belong to "+asset.name);
                    if(!File.Exists(Path.Combine(variant.textures,"BaseColor.png"))||!string.Equals(Path.GetDirectoryName(variant.textures),Path.GetDirectoryName(variant.model),StringComparison.OrdinalIgnoreCase))throw new Exception("Missing or foreign textures for "+asset.name);
                }
            }
            Log("START "+s.composer);
            ImportSources(s);
            var props=BuildProps(s);
            BuildMaster(s,props);
            CompileScenes(s);
            Log($"PASS {s.composer}: {s.assets.Length} asset types, {s.placements.Length} placements; five independent quality scenes.");
        }
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        var catalog=AssetDatabase.LoadAssetAtPath<ArenaSceneCatalog>(CatalogPath);
        foreach(var s in manifest.scenes) foreach(string quality in Q)
        {
            string guid=AssetDatabase.AssetPathToGUID(Master(s));
            if(catalog.Resolve(guid,Q,Array.IndexOf(Q,quality),out string chosen)==null||chosen!=quality)
                throw new Exception("Catalog resolution: "+s.composer+" "+quality);
            if(settings.FindAssetEntry(guid)!=null) throw new Exception("Master still Addressable: "+s.composer);
        }
        Log("PASS catalog quality resolution for all twelve composers. No bake, bundle build or publication performed.");
        Log("Source gaps: Bach is an enclosed church interior without a skybox. Mozart, Tchaikovsky, Vivaldi, Wagner have no exported scenario props yet. Unfinished assets remain listed in source folders; available tiers use the existing nearest-lower / nearest-higher fallback rule.");
        Log("Specular maps deferred by user request. Bump sources are imported as generated normals.");
        AssetDatabase.SaveAssets();
    }
    static void Copy(string source,string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        if(!File.Exists(target)||new FileInfo(source).Length!=new FileInfo(target).Length||File.GetLastWriteTimeUtc(source)>File.GetLastWriteTimeUtc(target)) File.Copy(source,target,true);
    }
    static void ImportSources(SceneSpec s)
    {
        string folder=Folder(s);
        Directory.CreateDirectory(folder+"/Materials"); Directory.CreateDirectory(folder+"/Textures");
        // Copy only runtime FBX and the maps consumed by Lit; never copy intermediate pipeline work.
        foreach(var a in s.assets) foreach(var v in a.variants)
        {
            string dir=folder+"/Props/"+a.name+"/"+v.quality;
            Copy(v.model,dir+"/"+a.name+"_"+v.quality+".fbx");
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
        int max=sky?4096:2048;
        bool changed=imp.textureType!=type||imp.sRGBTexture!=(!normal&&!linear)||imp.convertToNormalmap!=height||imp.maxTextureSize!=max||imp.mipmapEnabled==sky||imp.alphaSource!=TextureImporterAlphaSource.FromInput;
        imp.textureType=type; imp.sRGBTexture=!normal&&!linear; imp.convertToNormalmap=height;
        imp.heightmapScale=.035f; imp.maxTextureSize=max; imp.mipmapEnabled=!sky;
        imp.alphaSource=TextureImporterAlphaSource.FromInput;
        imp.wrapMode=sky?TextureWrapMode.Clamp:TextureWrapMode.Repeat;
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
                string dir=Folder(s)+"/Props/"+a.name+"/"+v.quality;
                string modelPath=dir+"/"+a.name+"_"+v.quality+".fbx";
                var imp=(ModelImporter)AssetImporter.GetAtPath(modelPath);
                bool changed=imp.materialImportMode!=ModelImporterMaterialImportMode.None||imp.importCameras||imp.importLights||imp.importAnimation||imp.isReadable;
                imp.materialImportMode=ModelImporterMaterialImportMode.None; imp.importCameras=false; imp.importLights=false; imp.importAnimation=false; imp.isReadable=false;
                if(changed) imp.SaveAndReimport();
                var mat=Mat(dir+"/"+a.name+"_"+v.quality+".mat",a.name.Equals("handel_scenario_finestra",StringComparison.OrdinalIgnoreCase)?"MusicGame/Art/WindowGlassCorrection":"Universal Render Pipeline/Lit");
                if(mat.HasProperty("_GlassColor")){mat.SetColor("_GlassColor",new Color(.70f,.81f,.91f,1));mat.SetFloat("_GlassEmission",.6f);}
                mat.SetColor("_BaseColor",Color.white); mat.SetTexture("_BaseMap",Tex(dir+"/BaseColor.png"));
                if(a.emission>0) {mat.SetTexture("_EmissionMap",Tex(dir+"/BaseColor.png"));mat.SetColor("_EmissionColor",Color.white*a.emission);mat.EnableKeyword("_EMISSION");}
                else {mat.SetColor("_EmissionColor",Color.black);mat.DisableKeyword("_EMISSION");}
                mat.SetTexture("_BumpMap",Tex(dir+"/Normal.png",true)); mat.SetFloat("_BumpScale",.65f); mat.EnableKeyword("_NORMALMAP");
                var metal=Tex(dir+"/MetallicSmoothness.png",false,true);
                mat.SetTexture("_MetallicGlossMap",metal); if(metal) mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness",.68f); mat.SetFloat("_Cull",(float)CullMode.Off); mat.doubleSidedGI=true;
                // Painted stained glass is dielectric. The generated metal map incorrectly marks
                // large coloured areas as metal, washing out the image with sky reflections.
                bool stone=a.name.Contains("_marble_")||a.name.EndsWith("_garden_statue")||a.name.EndsWith("_bust");
                if(stone||a.name.Contains("_vitirine_")||a.name.Contains("_curtain")||a.name.Contains("_cortinatges"))
                {
                    mat.SetTexture("_MetallicGlossMap",null); mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetFloat("_Metallic",0); mat.SetFloat("_Smoothness",stone?.4f:a.name.Contains("_vitirine_")?.28f:.35f);
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
                    if(a.width>0&&a.height>0) adjusted.y*=a.height/b.size.y;
                    if(a.depth>0) adjusted.z*=a.depth/b.size.z;
                    dimensions.localScale=adjusted; b=BoundsOf(root);
                    dimensions.localPosition-=new Vector3(b.center.x,b.min.y,b.center.z);
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
                props[a.name]=PrefabUtility.SaveAsPrefabAsset(master,Folder(s)+"/Props/"+a.name+"/"+a.name+".prefab");
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
                PrefabUtility.SaveAsPrefabAsset(existing,Master(s));
            }
            finally { PrefabUtility.UnloadPrefabContents(existing); }
            AssetDatabase.SaveAssets(); return;
        }
        string folder=Folder(s), prefix=folder+"/Textures/"+s.composer;
        var floor=Mat(folder+"/Materials/"+s.composer+"_Floor.mat");
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
            floor.SetTextureScale("_BaseMap",Vector2.one*s.look.floorTiling);floor.SetFloat("_BumpScale",.1f);EditorUtility.SetDirty(floor);
            if(sky) {sky.SetFloat("_Exposure",s.look.skyExposure);sky.SetFloat("_Rotation",s.look.skyRotation);sky.SetFloat("_ImageType",s.look.halfPanorama?1:0);if(sky.HasProperty("_MirrorOnBack"))sky.SetFloat("_MirrorOnBack",1);}
            if(sky&&sky.HasProperty("_HorizontalFov")){sky.SetFloat("_HorizontalFov",s.look.panoramaHorizontalFov>0?s.look.panoramaHorizontalFov:100);sky.SetFloat("_VerticalFov",s.look.panoramaVerticalFov>0?s.look.panoramaVerticalFov:60);}
            if(s.look.hideSky)sky=null;
        }
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
                    terrace.transform.SetParent(scenery.transform,false); terrace.transform.localPosition=new Vector3(0,-.2f,13);
                    terrace.transform.localScale=new Vector3(18,.4f,10); terrace.GetComponent<Renderer>().sharedMaterial=floor;
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
                    }
                }
                var oldProbe=lighting.Find("SceneryReflection"); if(oldProbe) Object.DestroyImmediate(oldProbe.gameObject);
                var probe=new GameObject("SceneryReflection").AddComponent<ReflectionProbe>(); probe.transform.SetParent(lighting,false); probe.transform.localPosition=new Vector3(0,3,0);
                probe.mode=ReflectionProbeMode.Realtime; probe.refreshMode=ReflectionProbeRefreshMode.ViaScripting; probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
                probe.resolution=128; probe.size=new Vector3(24,15,24); probe.boxProjection=true;
            }
            if(arena.GetComponentsInChildren<Camera>(true).Length!=0) throw new Exception("Unexpected camera "+s.composer);
            PrefabUtility.SaveAsPrefabAsset(arena,Master(s));
        }
        finally { PrefabUtility.UnloadPrefabContents(arena); }
        AssetDatabase.SaveAssets();
    }
    static void CompileScenes(SceneSpec s)
    {
        string folder=Folder(s)+"/Scenes"; Directory.CreateDirectory(folder); AssetDatabase.Refresh();
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        var group=settings.FindGroup(ArenaContentSetup.GroupName); if(group==null) throw new Exception("Missing Remote_Arenas");
        var catalog=AssetDatabase.LoadAssetAtPath<ArenaSceneCatalog>(CatalogPath);
        if(!catalog) throw new Exception("Missing catalog");
        catalog.hideFlags|=HideFlags.DontUnloadUnusedAsset;
        string guid=AssetDatabase.AssetPathToGUID(Master(s)); var entry=catalog.Find(guid);
        if(entry==null) {entry=new ArenaSceneCatalog.Entry{sourceArenaGuid=guid};catalog.entries.Add(entry);} entry.variants.Clear();
        string signature=null;
        foreach(string quality in Q)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Master(s)),scene);
            PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.OutermostRoot,InteractionMode.AutomatedAction);
            var selectors=root.GetComponentsInChildren<MonteverdiPropQuality>(true);
            var expected=new HashSet<string>();
            foreach(var sel in selectors)
            {
                var chosen=sel.variants[Array.IndexOf(Q,quality)]; string path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(chosen);
                if(string.IsNullOrEmpty(path)) throw new Exception("Missing quality prefab "+sel.name);
                foreach(string dep in AssetDatabase.GetDependencies(path,true).Where(p=>p.EndsWith(".fbx"))) expected.Add(dep);
                var inst=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
                inst.name=sel.name; inst.transform.SetParent(sel.transform.parent,false);
                inst.transform.localPosition=sel.transform.localPosition; inst.transform.localRotation=sel.transform.localRotation; inst.transform.localScale=sel.transform.localScale;
                inst.SetActive(true); Object.DestroyImmediate(sel.gameObject);
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
            RenderSettings.ambientEquatorColor=env.ambientEquatorColor; RenderSettings.ambientGroundColor=env.ambientGroundColor; RenderSettings.ambientIntensity=env.ambientIntensity; RenderSettings.fog=env.fog;
            string scenePath=folder+"/"+s.composer+"_Arena_01_"+quality+".unity";
            EditorSceneManager.SaveScene(scene,scenePath);
            string sceneGuid=AssetDatabase.AssetPathToGUID(scenePath); var address=settings.CreateOrMoveEntry(sceneGuid,group);
            string id=s.composer.ToLowerInvariant(); address.address=$"Arena/{id}/01/{quality}";
            foreach(string label in new[]{ArenaContentSetup.CategoryLabel,"Arena_"+id,QualityAssetResolver.LabelFor(quality)}) {settings.AddLabel(label);address.SetLabel(label,true);}
            entry.variants.Add(new ArenaSceneCatalog.Variant{qualityName=quality,scene=new AssetReference(sceneGuid)});
            var deps=AssetDatabase.GetDependencies(scenePath,true);
            if(deps.Contains(Master(s))||deps.Any(p=>p.EndsWith("MonteverdiPropQuality.cs"))) throw new Exception("Master/selector leaked "+scenePath);
            var actual=new HashSet<string>(deps.Where(p=>p.EndsWith(".fbx")&&p.Contains("/Props/")));
            if(!actual.SetEquals(expected)) throw new Exception("Unexpected quality dependencies "+scenePath);
            if(actual.Any(p=>!p.StartsWith(Folder(s)+"/Props/",StringComparison.Ordinal)))throw new Exception("Foreign composer FBX: "+scenePath);
            if(root.GetComponentsInChildren<MonteverdiPropQuality>(true).Length!=0) throw new Exception("Unstripped selector");
            if(quality=="High") Preview(s,root);
            Log($"SCENE {s.composer} {quality}: {selectors.Length} placements, {actual.Count} FBX, composition/dependencies verified");
        }
        settings.RemoveAssetEntry(guid); catalog.hideFlags=HideFlags.None; EditorUtility.SetDirty(catalog); EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
    }
    static void Preview(SceneSpec s,GameObject root)
    {
        string folder="Logs/ComposerSceneryPreviews/"+s.composer; Directory.CreateDirectory(folder);
        var go=new GameObject("PreviewCamera"); var camera=go.AddComponent<Camera>(); camera.clearFlags=CameraClearFlags.Skybox; camera.fieldOfView=62; camera.nearClipPlane=.1f; camera.farClipPlane=400;
        var positions=new[]{new Vector3(0,2.6f,-7.6f),new Vector3(7.4f,4,-6.5f),new Vector3(-7.4f,4,6.5f)};
        var targets=new[]{new Vector3(0,3.6f,6),new Vector3(-2,3.5f,4),new Vector3(2,3.5f,-3)};
        if(s.view!=null){positions[0]=s.view.position;targets[0]=s.view.target;camera.fieldOfView=s.view.fov;}
        try
        {
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
