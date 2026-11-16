#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TerrainContinuityCheck
{
    const string Root="Assets/_Project/Art/RunnerSurfaces/MusicStyles";
    [MenuItem("MusicGame/Runner/Check Surface Continuity")]
    public static void Run()
    {
        if(EditorApplication.isPlaying) throw new Exception("Stop play mode before checking terrain");
        bool async=ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation=false;
        var scene=EditorSceneManager.NewPreviewScene();
        var cameraGo=new GameObject("Terrain continuity camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
        var camera=cameraGo.AddComponent<Camera>(); camera.scene=scene; camera.orthographic=true; camera.orthographicSize=8;
        camera.cullingMask=1<<31; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.04f,.05f,.07f);
        camera.transform.position=new Vector3(0,20,0); camera.transform.rotation=Quaternion.Euler(90,0,0);
        var go=new GameObject("Terrain continuity samples");go.layer=31;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
        var filter=go.AddComponent<MeshFilter>(); var renderer=go.AddComponent<MeshRenderer>();
        var quad=new Mesh(); quad.vertices=new[]{new Vector3(-8,0,-8),new Vector3(8,0,-8),new Vector3(-8,0,8),new Vector3(8,0,8)};
        quad.triangles=new[]{0,2,1,1,2,3};quad.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};quad.colors=new[]{Color.white,Color.white,Color.white,Color.white};quad.RecalculateNormals();
        filter.sharedMesh=quad;
        string output="../VisualReviews/TerrainContinuity";Directory.CreateDirectory(output);
        float worst=0;int probes=0;
        float playhead=Shader.GetGlobalFloat("_PlayheadEnabled");Shader.SetGlobalFloat("_PlayheadEnabled",0);
        try
        {
            FinishTerrainSetup.InstallAndCheck();
            foreach(string style in new[]{"Dance_1","Dance_2","Dance_3","ClassicalFinish"})
            {
                string folder=style.StartsWith("Dance")?"Dance":"Classical";
                var source=AssetDatabase.LoadAssetAtPath<Material>(Root+"/"+folder+"/"+style+".mat");
                if(source==null || ShaderUtil.ShaderHasError(source.shader))throw new Exception("Invalid terrain material: "+style);
                var mat=new Material(source);renderer.sharedMaterial=mat;
                try
                {
                    mat.SetFloat("_AmbientFloor",1);mat.SetFloat("_SurfaceEmissionStrength",0);mat.SetFloat("_TintStrength",0);mat.SetFloat("_FinishSurfaceEnabled",0);mat.SetFloat("_WeatherStrength",0);mat.SetFloat("_SurfaceMode",1);
                    float metres=mat.GetFloat("_Meters");var indices=source.GetVector("_LayerIndices");
                    for(int layer=0;layer<3;layer++)for(int axis=0;axis<2;axis++)for(int edge=-1;edge<=1;edge++)
                    {
                        mat.SetVector("_LayerIndices",new Vector4(indices[layer],0,0,0));
                        var coords=new Vector3[4];
                        for(int v=0;v<4;v++)
                        {
                            float across=edge+((v%2)-.5f)*.0002f;
                            float along=.17f+(v/2)*2.41f;
                            coords[v]=axis==0?new Vector3(across*metres,along*metres,.5f):new Vector3(along*metres,across*metres,.5f);
                        }
                        quad.SetUVs(1,coords);filter.sharedMesh=quad;
                        var image=Capture(camera,128,128);
                        float error=0;
                        for(int y=3;y<125;y++) {Color a=image.GetPixel(63,y),b=image.GetPixel(64,y);error+=Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b);}
                        error/=122*3;worst=Mathf.Max(worst,error);probes++;
                        UnityEngine.Object.DestroyImmediate(image);
                        if(error>.04f)throw new Exception("Texture seam remains: "+style+" layer "+layer+" axis "+axis+" edge "+edge+" error "+error);
                    }
                    UnityEngine.Object.DestroyImmediate(mat);mat=new Material(source);renderer.sharedMaterial=mat;
                    filter.sharedMesh=RunnerSurfaceFamilies.PathMesh(); camera.orthographicSize=12.5f;
                    camera.transform.position=new Vector3(12,19,-19);camera.transform.LookAt(Vector3.up*.5f);
                    Save(Capture(camera,1100,900),output+"/"+style+".png");
                    filter.sharedMesh=quad;camera.orthographicSize=8;camera.transform.position=new Vector3(0,20,0);camera.transform.rotation=Quaternion.Euler(90,0,0);
                }
                finally {UnityEngine.Object.DestroyImmediate(mat);}
            }
            // Exercise the real runtime binder and compare full finish coverage against its source.
            var host=new GameObject("Runtime finish binding check");host.SetActive(false);
            var config=ScriptableObject.CreateInstance<MusicRunnerGameplayConfig>();
            var core=ScriptableObject.CreateInstance<MusicRunnerCoreConfig>();var level=ScriptableObject.CreateInstance<MusicRunnerLevelConfig>();
            config.core=core;config.levelGeneration=level;core.playerSpeed=10;core.warmupTime=0;
            level.finishSurfaceRevealSeconds=2;level.finishSurfaceBlendMeters=20;level.terrainAmbientFloor=.78f;
            Material runtime=null;
            try
            {
                var finish=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Classical/ClassicalFinish.mat");
                var dance=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Dance/Dance_1.mat");
                var world=host.AddComponent<MusicWorldManager>();world.Initialize(config,dance,finish);
                var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                typeof(MusicWorldManager).GetField("_playRangeEnd",flags).SetValue(world,2.7f);
                runtime=(Material)typeof(MusicWorldManager).GetMethod("VertexColorMaterial",flags).Invoke(world,null);
                filter.sharedMesh=RunnerSurfaceFamilies.PathMesh();renderer.sharedMaterial=runtime;camera.orthographicSize=12.5f;
                camera.transform.position=new Vector3(12,19,-19);camera.transform.LookAt(Vector3.up*.5f);
                Save(Capture(camera,1100,900),output+"/Dance_to_Classical.png");
                runtime.SetFloat("_FinishDistance",0);
                var actual=Capture(camera,400,400);renderer.sharedMaterial=finish;var expected=Capture(camera,400,400);
                var a=actual.GetPixels();var b=expected.GetPixels();float difference=0;
                for(int i=0;i<a.Length;i++)difference+=Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b);
                difference/=a.Length*3;
                UnityEngine.Object.DestroyImmediate(actual);UnityEngine.Object.DestroyImmediate(expected);
                if(difference>.005f)throw new Exception("Runtime finish differs from classical height layers: "+difference);
            }
            finally
            {
                if(runtime!=null)UnityEngine.Object.DestroyImmediate(runtime);UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(config);UnityEngine.Object.DestroyImmediate(core);UnityEngine.Object.DestroyImmediate(level);
            }
            // Check every existing style material uses a compilable pass after the shared shader change.
            int materials=0;
            foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{Root}))
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if(m.shader.name!="MusicGame/VertexColorLit")continue;
                if(ShaderUtil.ShaderHasError(m.shader)||!m.SetPass(0))throw new Exception("Failed terrain pass: "+m.name);
                materials++;
            }
            File.WriteAllText(output+"/Validation.txt","PASS: "+probes+" GPU probes across both UV axes, positive/negative tile edges and all three layers of Dance's three variants plus ClassicalFinish. Worst mean edge difference: "+worst+". "+materials+" material passes compiled. Classical finish altitude parameters and finish-arch regression checks passed. "+DateTime.UtcNow.ToString("O"));
            Debug.Log(File.ReadAllText(output+"/Validation.txt"));
        }
        finally {Shader.SetGlobalFloat("_PlayheadEnabled",playhead);UnityEngine.Object.DestroyImmediate(quad);EditorSceneManager.ClosePreviewScene(scene);ShaderUtil.allowAsyncCompilation=async;}
    }
    static Texture2D Capture(Camera camera,int width,int height)
    {
        var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(width,height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();return tex;}
        finally{camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);}
    }
    static void Save(Texture2D image,string path){File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);}
}
#endif
