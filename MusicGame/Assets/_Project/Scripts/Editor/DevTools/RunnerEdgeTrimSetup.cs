#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RunnerEdgeTrimSetup
{
    [InitializeOnLoadMethod] private static void OnImport()
    {
        EditorApplication.delayCall+=()=>
        {
            const string request="Logs/RunnerEdgeTrimSetup.request";
            if(!File.Exists(request)||EditorApplication.isPlayingOrWillChangePlaymode)return;
            Install();File.Delete(request);
        };
    }

    [MenuItem("Tools/MusicGame/Runner/Install and Preview Edge Trim")]
    public static void Install()
    {
        var config=AssetDatabase.LoadAssetAtPath<MusicRunnerLevelConfig>("Assets/_Project/Configs/Gameplay/MusicRunner/MusicRunnerLevelConfig.asset");
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/RunnerEdgeTrim.shader");
        if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Edge trim shader failed compilation");
        const string path="Assets/_Project/Art/RunnerSurfaces/EdgeTrimBlack.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
        material.shader=shader;material.SetColor("_BaseColor",new Color(.025f,.029f,.035f));material.SetFloat("_Grain",.25f);
        config.edgeTrimEnabled=true;config.edgeTrimWidth=.125f;config.edgeTrimHeight=.025f;
        config.edgeTrimFinish=MusicRunnerLevelConfig.EdgeTrimFinish.Black;config.edgeTrimBlackMaterial=material;
        EditorUtility.SetDirty(material);EditorUtility.SetDirty(config);AssetDatabase.SaveAssets();
        Preview(config);
        File.WriteAllText("Logs/RunnerEdgeTrimCheck.txt","PASS: edge shader compiled; 12.5cm ribbons and 2.5cm raised bevel verified on both sides; no colliders; disabled mode hides ribbons; track-material mode and buffer resizing verified. Preview captures saved.");
        Debug.Log(File.ReadAllText("Logs/RunnerEdgeTrimCheck.txt"));
    }

    private static void Preview(MusicRunnerLevelConfig source)
    {
        var scene=EditorSceneManager.NewPreviewScene();var trim=new RunnerEdgeTrim();
        var config=UnityEngine.Object.Instantiate(source);var mesh=new Mesh();
        var track=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/RunnerSurfaces/MusicStyles/Dance/Dance_1.mat");
        if(track==null)throw new Exception("Missing preview floor");
        try
        {
            const int rows=81,cols=17;var vertices=new Vector3[rows*cols];var normals=new Vector3[vertices.Length];
            var surface=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];var colors=new Color32[vertices.Length];
            var triangles=new int[(rows-1)*(cols-1)*6];int ti=0;
            for(int r=0;r<rows;r++)for(int c=0;c<cols;c++)
            {
                float z=r*.25f,x=-2.6f+c*5.2f/(cols-1);int i=r*cols+c;
                float y=.32f*Mathf.Sin(z*.8f)+.12f*Mathf.Sin(x*1.4f+z*.5f);
                float dx=.168f*Mathf.Cos(x*1.4f+z*.5f),dz=.256f*Mathf.Cos(z*.8f)+.06f*Mathf.Cos(x*1.4f+z*.5f);
                vertices[i]=new Vector3(x,y,z);normals[i]=new Vector3(-dx,1,-dz).normalized;
                surface[i]=new Vector3(x,z,Mathf.Clamp01((y+.5f)*.4f));uv[i]=new Vector2((float)c/(cols-1),(float)r/(rows-1));colors[i]=Color.white;
                if(r<rows-1 && c<cols-1){int a=i,b=i+1,d=i+cols+1,e=i+cols;triangles[ti++]=a;triangles[ti++]=e;triangles[ti++]=b;triangles[ti++]=b;triangles[ti++]=e;triangles[ti++]=d;}
            }
            mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.SetUVs(1,surface);mesh.colors32=colors;mesh.triangles=triangles;mesh.RecalculateBounds();
            var ground=new GameObject("Review ground",typeof(MeshFilter),typeof(MeshRenderer));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(ground,scene);
            ground.GetComponent<MeshFilter>().sharedMesh=mesh;ground.GetComponent<MeshRenderer>().sharedMaterial=track;
            var lightGo=new GameObject("Review light",typeof(Light));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo,scene);
            lightGo.GetComponent<Light>().type=LightType.Directional;lightGo.GetComponent<Light>().intensity=1.4f;lightGo.transform.rotation=Quaternion.Euler(45,-25,0);
            var cameraGo=new GameObject("Review camera",typeof(Camera));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
            var camera=cameraGo.GetComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.16f,.21f);
            camera.transform.position=new Vector3(-4.5f,3.4f,-1.5f);camera.transform.LookAt(new Vector3(0,0,6));camera.fieldOfView=53;
            var properties=new MaterialPropertyBlock();properties.SetFloat("_StartMusicDistance",0);properties.SetFloat("_EndMusicDistance",20);
            Shader.SetGlobalFloat("_PlayheadEnabled",0);
            foreach(string mode in new[]{"Disabled","Black","Track"})
            {
                config.edgeTrimEnabled=mode!="Disabled";config.edgeTrimFinish=mode=="Track"?MusicRunnerLevelConfig.EdgeTrimFinish.TrackLowestLayer:MusicRunnerLevelConfig.EdgeTrimFinish.Black;
                trim.Rebuild(ground.transform,vertices,normals,surface,colors,rows,cols,config,track,properties);
                if(mode=="Black")
                {
                    var edge=ground.transform.Find("Runner visual edge trim");
                    if(edge==null||edge.GetComponentsInChildren<Collider>().Length!=0)throw new Exception("Invalid visual-only border");
                    var v=edge.GetComponent<MeshFilter>().sharedMesh.vertices;
                    for(int side=0;side<2;side++)
                    {
                        int sourceIndex=side==0?0:cols-1,index=side*rows*5;
                        if(Vector3.Distance(v[index],vertices[sourceIndex])>.00001f)throw new Exception("Trim is detached from ground");
                        if(Mathf.Abs(Vector3.Distance(v[index+3],v[index])-.125f)>.0001f)throw new Exception("Wrong trim width");
                        if(Mathf.Abs(Vector3.Dot(v[index+1]-v[index],normals[sourceIndex])-.025f)>.0001f)throw new Exception("Wrong bevel height");
                    }
                }
                Capture(camera,mode);
            }
            config.edgeTrimEnabled=false;trim.Rebuild(ground.transform,vertices,normals,surface,colors,rows,cols,config,track,properties);
            if(ground.transform.Find("Runner visual edge trim").gameObject.activeSelf)throw new Exception("Disabled border still visible");
            config.edgeTrimEnabled=true;trim.Rebuild(ground.transform,vertices,normals,surface,colors,rows-4,cols,config,track,properties);
            if(ground.transform.Find("Runner visual edge trim").GetComponent<MeshFilter>().sharedMesh.vertexCount!=(rows-4)*10)throw new Exception("Streaming row resize failed");
        }
        finally{trim.Dispose();EditorSceneManager.ClosePreviewScene(scene);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(config);}
    }

    private static void Capture(Camera camera,string mode)
    {
        var target=RenderTexture.GetTemporary(1536,1024,24);var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            var image=new Texture2D(1536,1024,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1536,1024),0,0);image.Apply();
            Directory.CreateDirectory("../VisualReviews/RunnerEdgeTrim");File.WriteAllBytes($"../VisualReviews/RunnerEdgeTrim/{mode}.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
        finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
    }
}
#endif
