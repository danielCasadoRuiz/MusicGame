#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class MonteverdiScenerySetup
{
    const string Folder = "Assets/_Project/Art/Arenas/Monteverdi";
    const string Arena = Folder + "/Monteverdi_Arena_01.prefab";
    [Serializable] public class Manifest { public Asset[] assets; public Placement[] placements; }
    [Serializable] public class Asset { public string name; public float height; public float width; public Variant[] variants; }
    [Serializable] public class Variant { public string quality; public Mat[] materials; }
    [Serializable] public class Mat { public string name, baseColor, normal, metallicSmoothness, orm; public float metallic, roughness; }
    [Serializable] public class Placement { public string asset, name, wall; public float x,y,z,yaw,scale; }
    static readonly string[] Qualities = {"Low","Mid","HighMid","High","Ultra"};
    static Bounds BoundsOf(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new Exception("No renderer: " + root.name);
        var b = renderers[0].bounds;
        foreach(var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
        return b;
    }
    static Texture2D Texture(string folder, string file, bool normal=false, bool linear=false)
    {
        if(string.IsNullOrEmpty(file)) return null;
        string path=folder+"/"+file;
        var importer=AssetImporter.GetAtPath(path) as TextureImporter;
        if(importer==null) throw new Exception("Missing texture "+path);
        bool changed=importer.textureType!=(normal?TextureImporterType.NormalMap:TextureImporterType.Default)
            ||importer.sRGBTexture!=(!linear&&!normal)||!importer.mipmapEnabled||importer.maxTextureSize!=2048
            ||importer.alphaSource!=TextureImporterAlphaSource.FromInput;
        importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
        importer.sRGBTexture=!linear&&!normal; importer.mipmapEnabled=true; importer.maxTextureSize=2048;
        importer.alphaSource=TextureImporterAlphaSource.FromInput; if(changed) importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    public static void RunFromCommandLine()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch(Exception e) { File.WriteAllText("Logs/MonteverdiScenerySetup.txt",e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void PreviewFromCommandLine()
    {
        try
        {
            var report=new List<string>(); Preview(report);
            File.WriteAllText("Logs/MonteverdiPreviewCheck.txt",string.Join("\n",report));
            EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    [MenuItem("Tools/MusicGame/Content/Arenas/Build Monteverdi Scenery")]
    public static void Build()
    {
        var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder+"/scenery_manifest.json"));
        var report=new List<string>();
        var prefabs=new Dictionary<string,GameObject>();
        foreach(var asset in manifest.assets)
        {
            string folder=Folder+"/Props/"+asset.name;
            var master=new GameObject(asset.name);
            var selector=master.AddComponent<MonteverdiPropQuality>();
            for(int q=0;q<Qualities.Length;q++)
            {
                string quality=Qualities[q], dir=folder+"/"+quality;
                string modelPath=dir+"/"+asset.name+"_"+quality+".fbx";
                var imp=AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if(imp==null) throw new Exception("Missing model "+modelPath);
                bool modelChanged=imp.materialImportMode!=ModelImporterMaterialImportMode.None||imp.importCameras||imp.importLights||imp.importAnimation||imp.isReadable;
                imp.materialImportMode=ModelImporterMaterialImportMode.None;
                imp.importCameras=false; imp.importLights=false; imp.importAnimation=false;
                imp.isReadable=false; if(modelChanged) imp.SaveAndReimport();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var root=new GameObject(asset.name+"_"+quality);
                var mesh=(GameObject)PrefabUtility.InstantiatePrefab(model);
                mesh.transform.SetParent(root.transform,false);
                var bounds=BoundsOf(root);
                float scale=asset.width>0?asset.width/bounds.size.x:asset.height/bounds.size.y;
                mesh.transform.localScale*=scale;
                bounds=BoundsOf(root);
                mesh.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                var mats=new Dictionary<string,Material>();
                foreach(var m in asset.variants.First(v=>v.quality==quality).materials)
                {
                    string path=dir+"/"+m.name+".mat";
                    var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(mat==null) {mat=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat,path);}
                    mat.SetColor("_BaseColor",Color.white);
                    mat.SetTexture("_BaseMap",Texture(dir,m.baseColor));
                    mat.SetTexture("_BumpMap",Texture(dir,m.normal,true));
                    if(!string.IsNullOrEmpty(m.normal)) mat.EnableKeyword("_NORMALMAP");
                    mat.SetFloat("_BumpScale",0.65f);
                    mat.SetTexture("_MetallicGlossMap",Texture(dir,m.metallicSmoothness,false,true));
                    if(!string.IsNullOrEmpty(m.metallicSmoothness)) mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    bool fabric=asset.name.Contains("velvet")||asset.name.Contains("chair");
                    mat.SetFloat("_Smoothness",!string.IsNullOrEmpty(m.metallicSmoothness) ? (fabric ? .45f : .72f) : 1-m.roughness);
                    mat.SetFloat("_Metallic",m.metallic);
                    mat.SetTexture("_OcclusionMap",Texture(dir,m.orm,false,true));
                    if(!string.IsNullOrEmpty(m.orm)) mat.EnableKeyword("_OCCLUSIONMAP");
                    mat.SetFloat("_OcclusionStrength",1);
                    // All ten source GLBs declare doubleSided=true, including the thin cloth.
                    mat.SetFloat("_Cull",(float)CullMode.Off); mat.doubleSidedGI=true;
                    EditorUtility.SetDirty(mat); mats[m.name]=mat;
                }
                // Exported mesh material slots follow the stable Material_N names. With material importing
                // disabled, bind by the original FBX material slot order (one material in these ten models).
                foreach(var r in mesh.GetComponentsInChildren<Renderer>(true))
                {
                    var array=r.sharedMaterials;
                    for(int i=0;i<array.Length;i++) array[i]=array[i]!=null&&mats.ContainsKey(array[i].name)?mats[array[i].name]:mats.Values.ElementAt(Math.Min(i,mats.Count-1));
                    r.sharedMaterials=array;
                }
                string prefabPath=dir+"/"+asset.name+"_"+quality+".prefab";
                var saved=PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
                Object.DestroyImmediate(root);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(saved);
                instance.transform.SetParent(master.transform,false);
                instance.SetActive(q==4); selector.variants[q]=instance;
                bounds=BoundsOf(instance);
                report.Add($"PASS {asset.name} {quality}: {bounds.size}, base y={bounds.min.y:F4}");
            }
            prefabs[asset.name]=PrefabUtility.SaveAsPrefabAsset(master,folder+"/"+asset.name+".prefab");
            Object.DestroyImmediate(master);
        }
        var arena=PrefabUtility.LoadPrefabContents(Arena);
        try
        {
            var previous=arena.transform.Find("MonteverdiScenery");
            if(previous!=null) Object.DestroyImmediate(previous.gameObject);
            var scenery=new GameObject("MonteverdiScenery"); scenery.transform.SetParent(arena.transform,false);
            foreach(string wall in new[]{"North","South","East","West"}) new GameObject(wall).transform.SetParent(scenery.transform,false);
            // Broad marble piers support the paintings; the other bays remain open to the panorama.
            string pierMatPath=Folder+"/Materials/Monteverdi_GalleryPier.mat";
            var pierMat=AssetDatabase.LoadAssetAtPath<Material>(pierMatPath);
            if(pierMat==null) { pierMat=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(pierMat,pierMatPath); }
            pierMat.SetColor("_BaseColor",new Color(.72f,.64f,.51f)); pierMat.SetFloat("_Smoothness",.32f); EditorUtility.SetDirty(pierMat);
            foreach(string wall in new[]{"West"})
            {
                var pier=GameObject.CreatePrimitive(PrimitiveType.Cube); pier.name=wall+"_PaintingSupport";
                Object.DestroyImmediate(pier.GetComponent<Collider>()); pier.transform.SetParent(scenery.transform.Find(wall),false);
                pier.transform.localPosition=new Vector3(-9.15f,4.2f,0);
                pier.transform.localScale=new Vector3(.75f,8.4f,3.85f); pier.GetComponent<Renderer>().sharedMaterial=pierMat;
            }
            var architecture=new GameObject("PartialRoofAndTerrace"); architecture.transform.SetParent(scenery.transform,false);
            string roofMatPath=Folder+"/Materials/Monteverdi_Ceiling.mat";
            var roofMat=AssetDatabase.LoadAssetAtPath<Material>(roofMatPath);
            if(roofMat==null) { roofMat=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(roofMat,roofMatPath); }
            roofMat.SetColor("_BaseColor",new Color(.17f,.065f,.035f)); roofMat.SetFloat("_Smoothness",.24f); EditorUtility.SetDirty(roofMat);
            Box(architecture.transform,"RearTerrace",new Vector3(0,-.16f,13),new Vector3(18,.3f,6),pierMat);
            Box(architecture.transform,"WestPartialRoof",new Vector3(-7.65f,9.5f,-1.2f),new Vector3(3.7f,.48f,17.5f),roofMat);
            Box(architecture.transform,"EastPartialRoof",new Vector3(8.25f,9.5f,-1.2f),new Vector3(2.5f,.48f,17.5f),roofMat);
            foreach(float x in new[]{-9.35f,-5.8f,7.05f,9.45f})
            {
                Box(architecture.transform,"LongCornice",new Vector3(x,9.23f,-1.2f),new Vector3(.42f,.48f,17.7f),pierMat);
                Box(architecture.transform,"CorniceLip",new Vector3(x,9.02f,-1.2f),new Vector3(.57f,.13f,17.8f),pierMat);
            }
            foreach(float z in new[]{-8.6f,-3f,2.8f,6.8f})
                Box(architecture.transform,"WestCeilingBeam",new Vector3(-7.65f,9.13f,z),new Vector3(3.7f,.3f,.27f),pierMat);
            Box(architecture.transform,"RearEntablature",new Vector3(0,9.38f,6.8f),new Vector3(18.8f,.5f,1.8f),pierMat);
            Box(architecture.transform,"RearCorniceLip",new Vector3(0,9.1f,6.68f),new Vector3(19,.16f,1.9f),pierMat);
            foreach(var p in manifest.placements)
            {
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefabs[p.asset],arena.scene);
                instance.name=p.name; instance.transform.SetParent(scenery.transform.Find(p.wall),false);
                instance.transform.localPosition=new Vector3(p.x,p.y,p.z); instance.transform.localRotation=Quaternion.Euler(0,p.yaw,0);
                instance.transform.localScale=Vector3.one*(p.scale>0?p.scale:1);
                var b=BoundsOf(instance);
                foreach(var r in instance.GetComponentsInChildren<Renderer>(true))
                    if(r.sharedMaterials.Any(m=>m==null||m.shader==null||m.shader.name!="Universal Render Pipeline/Lit")) throw new Exception("Invalid material: "+p.name);
                bool intersects=b.min.x<4.5f&&b.max.x>-4.5f&&b.min.z<2.2f&&b.max.z>-2.2f&&b.min.y<3f;
                if(intersects) throw new Exception("Scenery overlaps fight clearance: "+p.name+" "+b);
            }
            if(arena.GetComponentsInChildren<Camera>(true).Length>0) throw new Exception("Unexpected camera");
            var env=arena.GetComponentInChildren<ArenaEnvironmentSettings>(true);
            env.ambientMode=AmbientMode.Trilight; env.ambientColor=new Color(.42f,.46f,.54f);
            env.ambientEquatorColor=new Color(.25f,.215f,.18f); env.ambientGroundColor=new Color(.13f,.11f,.09f);
            env.ambientIntensity=.5f; env.mainLightIntensity=1.2f; env.mainLightShadowStrength=.88f;
            env.mainLightEuler=new Vector3(32f,210f,0);
            var fill=arena.transform.Find("Lighting/CameraSideFill").GetComponent<Light>(); fill.intensity=.12f;
            var reflection=arena.GetComponentInChildren<ReflectionProbe>(true); reflection.size=new Vector3(24,14,32); reflection.center=new Vector3(0,3,2);
            PrefabUtility.SaveAsPrefabAsset(arena,Arena);
            report.Add($"PASS arena: {manifest.placements.Length} instances, four walls, combat centre clear. Ground and skybox retained; light contrast adjusted. Partial roof and receding gallery added.");
        }
        finally { PrefabUtility.UnloadPrefabContents(arena); }
        AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/MonteverdiScenerySetup.txt",string.Join("\n",report));
        Preview(report);
        File.WriteAllText("Logs/MonteverdiScenerySetup.txt",string.Join("\n",report));
        Debug.Log(string.Join("\n",report));
    }
    static void Box(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
    {
        var box=GameObject.CreatePrimitive(PrimitiveType.Cube); box.name=name;
        Object.DestroyImmediate(box.GetComponent<Collider>()); box.transform.SetParent(parent,false);
        box.transform.localPosition=position; box.transform.localScale=scale; box.GetComponent<Renderer>().sharedMaterial=material;
    }
    static void Preview(List<string> report)
    {
        var old=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(!Application.isBatchMode && string.IsNullOrEmpty(old.path))
        {
            report.Add("Preview skipped: save the current untitled scene before generating additive previews.");
            return;
        }
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,Application.isBatchMode?NewSceneMode.Single:NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        try
        {
            var arena=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Arena),scene);
            foreach(var q in arena.GetComponentsInChildren<MonteverdiPropQuality>()) for(int i=0;i<5;i++) q.variants[i].SetActive(i==3);
            var cam=new GameObject("PreviewCamera").AddComponent<Camera>(); cam.clearFlags=CameraClearFlags.Skybox; cam.fieldOfView=58; cam.nearClipPlane=.1f; cam.farClipPlane=500;
            var sun=new GameObject("PreviewSun").AddComponent<Light>(); sun.type=LightType.Directional;
            var env=arena.GetComponentInChildren<ArenaEnvironmentSettings>(true); var snapshot=env.Apply(sun,cam);
            RenderSettings.sun=sun;
            Directory.CreateDirectory("Logs/MonteverdiPreviews");
            var positions=new[]{new Vector3(0,2.4f,-6.8f),new Vector3(7,3.2f,-6.5f),new Vector3(-7,3.2f,5.5f),new Vector3(0,24,-.01f),new Vector3(0,1.9f,-4f)};
            var targets=new[]{new Vector3(0,3.1f,5),new Vector3(-3,3.1f,4),new Vector3(3,3.1f,-4),Vector3.zero,new Vector3(0,1.1f,0)};
            Color32[] shadowPixels=null;
            for(int k=0;k<=positions.Length;k++)
            {
                int view=Math.Min(k,positions.Length-1);
                if(k==positions.Length) sun.shadows=LightShadows.None;
                cam.transform.position=positions[view]; cam.transform.LookAt(targets[view]);
                var rt=new RenderTexture(1440,900,24); cam.targetTexture=rt; cam.Render();
                RenderTexture.active=rt; var tex=new Texture2D(1440,900,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,1440,900),0,0); tex.Apply();
                File.WriteAllBytes($"Logs/MonteverdiPreviews/view_{k}.png",tex.EncodeToPNG());
                if(k==positions.Length-1) shadowPixels=tex.GetPixels32();
                if(k==positions.Length)
                {
                    var control=tex.GetPixels32(); double difference=0;
                    for(int i=0;i<control.Length;i++) difference+=Math.Abs(control[i].r-shadowPixels[i].r)+Math.Abs(control[i].g-shadowPixels[i].g)+Math.Abs(control[i].b-shadowPixels[i].b);
                    difference/=control.Length*3;
                    report.Add($"Shadow on/off image difference: {difference:F3} RGB levels");
                }
                RenderTexture.active=null; cam.targetTexture=null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            ArenaEnvironmentSettings.Restore(snapshot);
            report.Add("PASS five preview images rendered from saved prefab, including combat framing");
        }
        finally
        {
            if(old.IsValid() && old.isLoaded) { UnityEngine.SceneManagement.SceneManager.SetActiveScene(old); EditorSceneManager.CloseScene(scene,true); }
        }
    }
}
#endif
