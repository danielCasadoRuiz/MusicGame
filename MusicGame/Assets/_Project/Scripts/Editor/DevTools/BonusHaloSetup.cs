#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class BonusHaloSetup
{
    const string Request="Logs/BonusHaloSetup.request";
    [InitializeOnLoadMethod] static void OnImport(){EditorApplication.delayCall+=()=>{if(File.Exists(Request)&&!EditorApplication.isPlayingOrWillChangePlaymode){Install();File.Delete(Request);}};}
    [MenuItem("Tools/MusicGame/Bonuses/Install Ivory Halo")]
    public static void Install()
    {
        var endingTest=new FinishArchStreamingTests();endingTest.SetUp();
        try{endingTest.ClosingHillsStartAtTheArchWithoutTheLegacyFlatHold();}finally{endingTest.TearDown();}
        var config=AssetDatabase.LoadAssetAtPath<MusicRunnerCollectiblesConfig>("Assets/_Project/Configs/Gameplay/MusicRunner/MusicRunnerCollectiblesConfig.asset");
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/BonusIvoryHalo.shader");
        if(config==null || shader==null || ShaderUtil.ShaderHasError(shader))throw new Exception("Halo assets failed compilation");
        config.bonusHaloShader=shader;config.bonusHaloEnabled=true;
        config.bonusHaloSize=3f;config.bonusHaloColor=new Color(1f,1f,1f,.55f);
        BakeAuthoredColors(config);
        EditorUtility.SetDirty(config);AssetDatabase.SaveAssets();
        var material=new Material(shader);material.SetColor("_HaloColor",config.bonusHaloColor);
        var mesh=BonusHalo.CreateMesh();
        var scene=EditorSceneManager.NewPreviewScene();var owned=new List<UnityEngine.Object>();
        try
        {
            if(!material.SetPass(0))throw new Exception("Halo pass failed");
            var cameraGo=new GameObject("Bonus halo preview camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
            var camera=cameraGo.AddComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=3.2f;
            camera.transform.position=new Vector3(0,0,-12);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.10f,.12f);
            var backing=new GameObject("Terrain backdrop",typeof(MeshFilter),typeof(MeshRenderer));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(backing,scene);
            var bgMesh=UnityEngine.Object.Instantiate(mesh);owned.Add(bgMesh);
            bgMesh.SetUVs(1,new[]{new Vector3(-8,0,.6f),new Vector3(8,0,.6f),new Vector3(-8,10,.6f),new Vector3(8,10,.6f)});bgMesh.RecalculateNormals();
            backing.transform.position=new Vector3(0,0,3);backing.transform.localScale=new Vector3(12,7,1);backing.GetComponent<MeshFilter>().sharedMesh=bgMesh;
            backing.GetComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/RunnerSurfaces/MusicStyles/Dance/Dance_1.mat");
            int tested=0;
            var types=new[]{RingType.Kick,RingType.HiHat,RingType.Impact};
            for(int i=0;i<types.Length;i++)for(int row=0;row<2;row++)
            {
                var prefab=config.ResolvePrefab(types[i]);if(prefab==null)throw new Exception("Missing bonus prefab");
                var go=UnityEngine.Object.Instantiate(prefab);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
                foreach(var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
                foreach(var collider in go.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                foreach(var renderer in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var glow=BonusVisibilityMaterial.Create(renderer.sharedMaterial,config,Color.white);owned.Add(glow);renderer.sharedMaterial=glow;
                }
                var renderers=go.GetComponentsInChildren<MeshRenderer>(true);var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                go.transform.localScale*=1.35f/Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
                bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                go.transform.position+=new Vector3((i-1)*3.3f,row==0?1.5f:-1.5f,0)-bounds.center;
                if(row==1)
                {
                    var tint=BonusHalo.LightenedColor(config.HaloSourceColor(types[i],config.RingColor(types[i])),config.bonusHaloColor);
                    var halo=BonusHalo.Attach(go,mesh,material,config.bonusHaloSize,tint);
                    if(halo==null||halo.shadowCastingMode!=UnityEngine.Rendering.ShadowCastingMode.Off||halo.GetComponent<Collider>()!=null)throw new Exception("Invalid halo setup");
                    var properties=new MaterialPropertyBlock();halo.GetPropertyBlock(properties);
                    if(properties.GetColor("_HaloColor")!=tint)throw new Exception("Missing per-bonus halo tint");
                    go.SetActive(false);if(halo.gameObject.activeInHierarchy)throw new Exception("Pooled halo stays visible");go.SetActive(true);tested++;
                }
            }
            var rt=RenderTexture.GetTemporary(1440,900,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(1440,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1440,900),0,0);image.Apply();Directory.CreateDirectory("../VisualReviews/BonusHalo");File.WriteAllBytes("../VisualReviews/BonusHalo/BeforeAfter.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);}
            finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);}
            File.WriteAllText("Logs/BonusHaloCheck.txt","PASS: halo shader compiled; direct shader reference saved for builds; "+tested+" real bonus prefabs rendered before/after; per-bonus lightened tints verified; pooled roots hide the halo; no added collider or shadows. Top row: original. Bottom row: tinted halo. "+DateTime.UtcNow.ToString("O"));
            Debug.Log(File.ReadAllText("Logs/BonusHaloCheck.txt"));
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);foreach(var item in owned)UnityEngine.Object.DestroyImmediate(item);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(mesh);}
    }

    // Sample authored albedo in the editor only: no runtime texture reads or per-pickup materials.
    static void BakeAuthoredColors(MusicRunnerCollectiblesConfig config)
    {
        var types=(RingType[])Enum.GetValues(typeof(RingType));
        config.authoredBonusHaloColors=new Color[types.Length];
        var previous=RenderTexture.active;
        var rt=RenderTexture.GetTemporary(32,32,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var sample=new Texture2D(32,32,TextureFormat.RGBA32,false);
        try
        {
            foreach(var type in types)
            {
                var prefab=config.ResolvePrefab(type);if(prefab==null)continue;
                Color sum=Color.clear;float weight=0;
                foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                foreach(var material in renderer.sharedMaterials)
                {
                    if(material==null)continue;
                    var map=material.HasProperty("_BaseMap")?"_BaseMap":"_MainTex";
                    var texture=material.HasProperty(map)?material.GetTexture(map):null;
                    if(texture==null)continue;
                    Graphics.Blit(texture,rt);RenderTexture.active=rt;
                    sample.ReadPixels(new Rect(0,0,32,32),0,0);sample.Apply();
                    var tint=material.HasProperty("_BaseColor")?material.GetColor("_BaseColor"):material.HasProperty("_Color")?material.GetColor("_Color"):Color.white;
                    foreach(var pixel in sample.GetPixels())
                    {
                        Color color=pixel*tint;float w=color.maxColorComponent*color.a;
                        if(w<.05f)continue;
                        sum+=color*w;weight+=w;
                    }
                }
                if(weight>0)
                {
                    var color=sum/weight;float peak=Mathf.Max(.001f,color.maxColorComponent);
                    color=new Color(color.r/peak,color.g/peak,color.b/peak,1);
                    config.authoredBonusHaloColors[(int)type]=color;
                }
            }
        }
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(sample);}
    }
}
#endif
