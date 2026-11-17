#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FightScorePagesSetup
{
    [InitializeOnLoadMethod]private static void OnImport()
    {
        EditorApplication.delayCall+=()=>
        {
            const string request="Logs/FightScorePagesSetup.request";
            if(!File.Exists(request)||EditorApplication.isPlayingOrWillChangePlaymode)return;
            Install();File.Delete(request);
        };
    }

    [MenuItem("Tools/MusicGame/Fight/Install and Preview Burning Scores")]
    public static void Install()
    {
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/FightScorePage.shader");
        if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Burning score shader failed compilation");
        const string folder="Assets/_Project/Resources/VFX";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        const string texturePath="Assets/_Project/Art/VFX/ScorePage/AntiqueScorePage.png";
        var importer=AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if(importer==null)throw new Exception("Missing antique score-page texture");
        importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;
        importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;
        importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=2048;
        importer.npotScale=TextureImporterNPOTScale.None;
        importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if(texture==null||texture.width>=texture.height)throw new Exception("Score texture must be one portrait page");
        const string path=folder+"/ScorePages.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
        material.shader=shader;material.SetTexture("_BaseMap",texture);
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
        if(!material.SetPass(0))throw new Exception("Score page shader pass failed");
        CheckHandoff();
        foreach(string stage in new[]{"Orbit","Gather","Flight","PageDetail"})Render(stage,material);
        File.WriteAllText("Logs/FightScorePagesCheck.txt","PASS: shader compiled; antique portrait-page texture assigned with mipmaps and trilinear filtering; 12 curved score sheets; opponent-only; same sheet instance transfers from charge to projectile in either listener order; gathering moves sheets to hands; interruption removes sheets; no colliders or rigidbodies. Four rendered reviews saved.");
        Debug.Log(File.ReadAllText("Logs/FightScorePagesCheck.txt"));
    }

    private static void CheckHandoff()
    {
        var rivalGo=new GameObject("Score rival test");rivalGo.SetActive(false);
        var playerGo=new GameObject("Score player test");playerGo.SetActive(false);
        var projectile=new GameObject("Score projectile test");
        try
        {
            var rival=rivalGo.AddComponent<FighterActor>();var player=playerGo.AddComponent<FighterActor>();
            typeof(FighterActor).GetProperty("Side").SetValue(rival,FighterSide.Opponent);
            if(FightScorePages.Begin(player)!=null)throw new Exception("Player unexpectedly got composer scores");
            var pages=FightScorePages.Begin(rival);
            if(pages==null||pages.PageCount!=12)throw new Exception("Missing score pages");
            Vector3 body=Vector3.up,hands=new Vector3(.5f,1.1f,0);
            pages.PoseCharge(body,hands,.15f,.1f);float before=MeanDistance(pages,hands);
            pages.PoseCharge(body,hands,.98f,.8f);float after=MeanDistance(pages,hands);
            if(after>=before*.5f)throw new Exception("Pages did not accumulate at the hands");
            pages.EndCharge(true);
            var launched=FightScorePages.Launch(rival,projectile.transform,.4f,Vector3.right);
            if(launched!=pages||!launched.IsFlying)throw new Exception("Launch did not preserve the charge sheets");
            launched.EndCharge(true); // reverse listener order must not destroy an already-launched bundle
            if(!launched.IsFlying)throw new Exception("Late charge cleanup removed the projectile sheets");
            if(launched.GetComponentsInChildren<Collider>().Length!=0||launched.GetComponentsInChildren<Rigidbody>().Length!=0)
                throw new Exception("Score sheets changed physics");
            var interrupted=FightScorePages.Begin(rival);interrupted.EndCharge(false);
            if(interrupted!=null)throw new Exception("Interrupted charge left pages alive");
        }
        finally{UnityEngine.Object.DestroyImmediate(projectile);UnityEngine.Object.DestroyImmediate(rivalGo);UnityEngine.Object.DestroyImmediate(playerGo);}
    }

    private static float MeanDistance(FightScorePages pages,Vector3 point)
    {
        float total=0;foreach(Transform child in pages.transform)total+=Vector3.Distance(child.position,point);return total/pages.PageCount;
    }

    private static void Render(string stage,Material material)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Score effect review");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        var cameraGo=new GameObject("Score review camera",typeof(Camera));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
        var camera=cameraGo.GetComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color(.022f,.028f,.048f);camera.transform.position=new Vector3(2.4f,2,-4);
        camera.transform.LookAt(new Vector3(0,1,0));camera.fieldOfView=38;
        try
        {
            var pages=FightScorePages.Create(root.transform);
            if(stage=="Flight")pages.PoseFlight(new Vector3(0,1,0),Vector3.right,.55f,.25f);
            else pages.PoseCharge(Vector3.up,new Vector3(.45f,1.15f,0),stage=="Gather"?.91f:.18f,stage=="Gather"?.8f:.2f);
            if(stage=="PageDetail")
            {
                for(int i=1;i<pages.transform.childCount;i++)pages.transform.GetChild(i).gameObject.SetActive(false);
                var first=pages.transform.GetChild(0);first.position=Vector3.up;first.rotation=Quaternion.identity;first.localScale=Vector3.one*.9f;
                var block=new MaterialPropertyBlock();block.SetFloat("_Heat",.43f);block.SetFloat("_Opacity",1);block.SetFloat("_AnimationTime",.5f);first.GetComponent<Renderer>().SetPropertyBlock(block);
                camera.transform.position=new Vector3(0,1,-2.6f);camera.transform.LookAt(Vector3.up);
            }
            var lightGo=new GameObject("Score warm light",typeof(Light));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo,scene);
            var light=lightGo.GetComponent<Light>();light.type=LightType.Directional;light.color=new Color(1,.85f,.6f);light.intensity=1.4f;light.transform.rotation=Quaternion.Euler(25,-35,0);
            var rt=RenderTexture.GetTemporary(1440,1080,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
            try
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                var image=new Texture2D(1440,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1440,1080),0,0);image.Apply();
                Directory.CreateDirectory("../VisualReviews/FightScorePages");File.WriteAllBytes($"../VisualReviews/FightScorePages/{stage}.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
            }
            finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);}
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
#endif
