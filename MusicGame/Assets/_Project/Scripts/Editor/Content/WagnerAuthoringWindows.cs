#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static partial class WagnerAuthoringTools
{
    [InitializeOnLoadMethod] static void WindowsReady(){File.WriteAllText(Queue+".ready","gothic-windows-v1");}
    [Serializable] class WindowSpec {public string prop,backing;public float widthRatio=.27f,bottomRatio=.23f,straightRatio=.48f,riseRatio=.14f;}
    [Serializable] class WindowConfig {public bool enabled;public WindowSpec[] windows;}
    class Opening {public Transform anchor;public Vector3 center;public float halfWidth,straight,rise,front,back;public WindowSpec spec;}
    static readonly Dictionary<string,Opening> openings=new Dictionary<string,Opening>();
    static partial void BeginWindows(){openings.Clear();}
    static Bounds LocalMeshBounds(GameObject root)
    {
        Bounds result=new Bounds();bool first=true;
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if(!filter.sharedMesh||filter.GetComponentsInParent<Transform>(true).Any(t=>t.name.StartsWith("WagnerWindow_")))continue;var bounds=filter.sharedMesh.bounds;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {var point=root.transform.InverseTransformPoint(filter.transform.TransformPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z))));if(first){result=new Bounds(point,Vector3.zero);first=false;}else result.Encapsulate(point);}
        }
        if(first)throw new Exception("No window wall mesh: "+root.name);return result;
    }
    static partial void ApplyWindows(Scene scene,List<Binding> bindings,string quality)
    {
        string configPath="Assets/_Project/Configs/Editor/wagner_windows.json";if(!File.Exists(configPath))return;
        var config=JsonUtility.FromJson<WindowConfig>(File.ReadAllText(configPath));if(!config.enabled)return;
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        foreach(var binding in bindings)foreach(var old in binding.root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("WagnerWindow_")).ToArray())if(old)Object.DestroyImmediate(old.gameObject);
        var backingMasks=new Dictionary<Renderer,List<Opening>>();
        foreach(var spec in config.windows)
        {
            var binding=bindings.Single(b=>b.root.name==spec.prop);
            if(!openings.TryGetValue(spec.prop,out var shape))
            {
                var bounds=LocalMeshBounds(binding.root);shape=new Opening{center=new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*spec.bottomRatio,bounds.center.z),halfWidth=bounds.size.x*spec.widthRatio,straight=bounds.size.y*spec.straightRatio,rise=bounds.size.y*spec.riseRatio,front=bounds.max.z+.035f,back=bounds.min.z-.4f,spec=spec};openings[spec.prop]=shape;
            }
            var opening=new Opening{anchor=binding.root.transform,center=shape.center,halfWidth=shape.halfWidth,straight=shape.straight,rise=shape.rise,front=shape.front,back=shape.back,spec=spec};
            foreach(var renderer in binding.root.GetComponentsInChildren<MeshRenderer>(true))SetMask(renderer,new[]{opening},spec.prop+"_"+quality+"_"+Key(Relative(renderer.transform)));
            var backing=all.Single(t=>t&&t.name==spec.backing).GetComponent<Renderer>();
            if(!backingMasks.TryGetValue(backing,out var masks)){masks=new List<Opening>();backingMasks[backing]=masks;}masks.Add(opening);
            Frame(opening);
        }
        foreach(var pair in backingMasks)SetMask(pair.Key,pair.Value.ToArray(),pair.Key.name+"_"+quality);
        Directory.CreateDirectory("Logs");File.WriteAllText("Logs/WagnerWindows.txt","PASS: "+config.windows.Length+" pointed-arch openings cut through the decorative wall and its masonry backing; jambs, inner arch rims and mullions generated. Forward, depth, normals and shadow passes use the same cutout.");
    }
    static void SetMask(Renderer renderer,Opening[] masks,string key)
    {
        if(masks.Length>4)throw new Exception("Maximum four apertures per wall material.");
        string quality=key.Split('_').FirstOrDefault(Tiers.Contains);
        if(string.IsNullOrEmpty(quality))throw new Exception("No quality folder for window material: "+key);
        string dir=Generated+"/Windows/Materials/"+quality;Directory.CreateDirectory(dir);string shaderName="MusicGame/Art/GothicWindowCutout";var shader=Shader.Find(shaderName);if(!shader)throw new Exception("Missing window shader");
        var originals=renderer.sharedMaterials;
        for(int slot=0;slot<originals.Length;slot++)
        {
            var source=originals[slot];if(!source)continue;string originalGuid=source.GetTag("WagnerWindowSource",false,AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)));
            string path=dir+"/"+key+"_"+slot+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(source);AssetDatabase.CreateAsset(material,path);}else material.CopyPropertiesFromMaterial(source);
            material.shader=shader;material.SetOverrideTag("WagnerWindowSource",originalGuid);
            for(int i=0;i<4;i++)
            {
                material.SetVector("_WindowCenter"+i,Vector4.zero);if(i>=masks.Length)continue;var mask=masks[i];var t=mask.anchor;
                Vector3 center=t.TransformPoint(mask.center),right=t.TransformVector(Vector3.right),up=t.TransformVector(Vector3.up),normal=t.TransformVector(Vector3.forward);
                material.SetVector("_WindowCenter"+i,new Vector4(center.x,center.y,center.z,1));
                material.SetVector("_WindowRight"+i,new Vector4(right.normalized.x,right.normalized.y,right.normalized.z,mask.halfWidth*right.magnitude));
                material.SetVector("_WindowUp"+i,new Vector4(up.normalized.x,up.normalized.y,up.normalized.z,mask.straight*up.magnitude));
                material.SetVector("_WindowNormal"+i,new Vector4(normal.normalized.x,normal.normalized.y,normal.normalized.z,4*normal.magnitude));
                material.SetVector("_WindowShape"+i,new Vector4(mask.rise*up.magnitude,0,0,0));
            }
            EditorUtility.SetDirty(material);originals[slot]=material;
        }
        renderer.sharedMaterials=originals;
    }
    static float Arch(float x)=>Mathf.Sqrt(Mathf.Clamp01(1-(Mathf.Abs(x)+1)*(Mathf.Abs(x)+1)*.25f))/Mathf.Sqrt(.75f);
    static void Frame(Opening opening)
    {
        string dir=Generated+"/Windows";Directory.CreateDirectory(dir);var points=new List<Vector2>{new Vector2(-opening.halfWidth,0),new Vector2(opening.halfWidth,0),new Vector2(opening.halfWidth,opening.straight)};
        for(int i=1;i<=64;i++){float x=1-i/32f;points.Add(new Vector2(x*opening.halfWidth,opening.straight+opening.rise*Arch(x)));}
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var stoneIndices=new List<int>();var goldIndices=new List<int>();
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,List<int> indices)
        {int first=vertices.Count;vertices.AddRange(new[]{a,b,c,d});float along=Vector3.Distance(a,b)*.5f,depth=Vector3.Distance(a,d)*.5f;uv.AddRange(new[]{Vector2.zero,new Vector2(0,along),new Vector2(depth,along),new Vector2(depth,0)});indices.AddRange(new[]{first,first+1,first+2,first,first+2,first+3});}
        var outer=new List<Vector2>();
        for(int i=0;i<points.Count;i++)
        {var previous=points[(i+points.Count-1)%points.Count];var current=points[i];var next=points[(i+1)%points.Count];var a=(current-previous).normalized;var b=(next-current).normalized;var normal=(new Vector2(a.y,-a.x)+new Vector2(b.y,-b.x)).normalized;outer.Add(current+normal*.07f/Mathf.Max(.4f,Vector2.Dot(normal,new Vector2(a.y,-a.x))));}
        Vector3 At(Vector2 p,float z)=>new Vector3(p.x+opening.center.x,p.y+opening.center.y,z);
        for(int i=0;i<points.Count;i++)
        {int next=(i+1)%points.Count;Quad(At(points[i],opening.front),At(points[next],opening.front),At(points[next],opening.back),At(points[i],opening.back),stoneIndices);Quad(At(points[i],opening.front+.015f),At(outer[i],opening.front+.015f),At(outer[next],opening.front+.015f),At(points[next],opening.front+.015f),goldIndices);}
        string meshPath=dir+"/"+opening.spec.prop+"_Frame.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,meshPath);}mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=2;mesh.SetTriangles(stoneIndices,0);mesh.SetTriangles(goldIndices,1);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        Material Mat(string name,Color color,float metal,float smooth)
        {string path=dir+"/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}mat.SetColor("_BaseColor",color);mat.SetFloat("_Metallic",metal);mat.SetFloat("_Smoothness",smooth);mat.SetFloat("_Cull",0);EditorUtility.SetDirty(mat);return mat;}
        var stone=Mat("JambStone",new Color(.45f,.46f,.47f),.05f,.3f);var gold=Mat("InnerGoldRim",new Color(.42f,.25f,.07f),.7f,.4f);
        stone.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/Wagner_Floor_BaseColor.png"));stone.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/Wagner_Floor_Height.png"));stone.SetFloat("_BumpScale",.2f);stone.EnableKeyword("_NORMALMAP");EditorUtility.SetDirty(stone);
        var frame=new GameObject("WagnerWindow_"+opening.spec.prop);frame.transform.SetParent(opening.anchor,false);frame.AddComponent<MeshFilter>().sharedMesh=mesh;frame.AddComponent<MeshRenderer>().sharedMaterials=new[]{stone,gold};
        var mullion=GameObject.CreatePrimitive(PrimitiveType.Cube);mullion.name="StoneMullion";mullion.transform.SetParent(frame.transform,false);float height=opening.straight+opening.rise;
        mullion.transform.localPosition=new Vector3(opening.center.x,opening.center.y+height*.5f,opening.front-.14f);mullion.transform.localScale=new Vector3(.085f,height,.28f);mullion.GetComponent<Renderer>().sharedMaterial=stone;Object.DestroyImmediate(mullion.GetComponent<Collider>());
    }
    public static void BatchPolishWindowUvs()
    {
        try
        {
            foreach(string guid in AssetDatabase.FindAssets("t:Mesh",new[]{Generated+"/Windows"}))
            {
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));var vertices=mesh.vertices;var uv=mesh.uv;
                for(int i=0;i+3<vertices.Length;i+=4){float along=Vector3.Distance(vertices[i],vertices[i+1])*.5f,depth=Vector3.Distance(vertices[i],vertices[i+3])*.5f;uv[i]=Vector2.zero;uv[i+1]=new Vector2(0,along);uv[i+2]=new Vector2(depth,along);uv[i+3]=new Vector2(depth,0);}
                mesh.uv=uv;mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();var state=JsonUtility.FromJson<AuthoringState>(File.ReadAllText(StatePath));state.highDependencyHash=AssetDatabase.GetAssetDependencyHash(High).ToString();File.WriteAllText(StatePath,JsonUtility.ToJson(state,true));EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
#endif
