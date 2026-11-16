#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class AnalysisUISetup
{
    [InitializeOnLoadMethod]
    static void RefreshRequested()
    {
        EditorApplication.delayCall+=()=>
        {
            const string request="Logs/AnalysisUISetup.request";
            if(!File.Exists(request)||EditorApplication.isPlayingOrWillChangePlaymode)return;
            Install();File.Delete(request);
        };
    }
    [MenuItem("Tools/MusicGame/UI/Install Analysis Presentation")]
    public static void Install()
    {
        const string dir="Assets/_Project/Art/UI/Analysis";
        Directory.CreateDirectory(dir);
        var tex=new Texture2D(1024,2304,TextureFormat.RGBA32,false);
        var pixels=new Color32[1024*2304];
        for(int i=0;i<18;i++)
        {
            Color accent=AnalysisPresentation.Accent((GameMusicStyle)i);
            for(int y=0;y<56;y++) for(int x=0;x<1024;x++)
            {
                float left=12, right=1011;
                if(x<left||x>right) continue;
                bool edge=y<3||y>52||x-left<3||right-x<3;
                float stripe=Mathf.Repeat(x+y*2,64)<4 ? .19f:0;
                float light=.6f+.3f*Mathf.Sin(y/55f*Mathf.PI)+stripe;
                // Different surface rhythms remain aligned in one atlas.
                if(i==4||i==5) light-=Mathf.Repeat(x*7+y*13,97)<4?.22f:0;
                if(i==15||i==17) light+=.12f*Mathf.Sin(x*.03f);
                Color fill=Color.Lerp(accent,Color.white,Mathf.Clamp01(light-.45f));
                pixels[x+(i*128+y+4)*1024]=fill;
                pixels[x+(i*128+64+y+4)*1024]=edge?Color.Lerp(accent,Color.white,.6f):new Color(.045f,.055f,.075f,1);
            }
        }
        tex.SetPixels32(pixels); tex.Apply();
        string path=dir+"/AnalysisBars.png"; File.WriteAllBytes(path,tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Default; importer.alphaIsTransparency=true; importer.mipmapEnabled=false;
        importer.npotScale=TextureImporterNPOTScale.None; importer.isReadable=true; importer.maxTextureSize=4096; importer.textureCompression=TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        const string motifs="Assets/_Project/Art/UI/Analysis/AnalysisMotifs.png";
        AssetDatabase.ImportAsset(motifs);
        var motifImporter=(TextureImporter)AssetImporter.GetAtPath(motifs);
        motifImporter.textureType=TextureImporterType.Default; motifImporter.alphaIsTransparency=true;
        motifImporter.npotScale=TextureImporterNPOTScale.None; motifImporter.mipmapEnabled=false;
        motifImporter.isReadable=false; motifImporter.maxTextureSize=2048;
        motifImporter.textureCompression=TextureImporterCompression.CompressedHQ; motifImporter.SaveAndReimport();
        string prefab="Assets/_Project/Prefabs/UI/AnalyzingScreen.prefab";
        var root=PrefabUtility.LoadPrefabContents(prefab);
        try
        {
            var p=root.GetComponent<AnalysisPresentation>() ?? root.AddComponent<AnalysisPresentation>();
            p.library=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>("Assets/_Project/Configs/UI/StyleSkinLibrary.asset");
            p.barAtlas=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            p.motifAtlas=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/UI/Analysis/AnalysisMotifs.png");
            p.styleFlashShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/StyleRevealFlash.shader");
            p.styleRevealClip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Sfx/StyleRevealMagic.wav");
            var flashCheck=new Material(p.styleFlashShader);
            try{if(ShaderUtil.ShaderHasError(p.styleFlashShader)||!flashCheck.SetPass(0))throw new System.Exception("Style flash shader failed compilation");}
            finally{Object.DestroyImmediate(flashCheck);}
            if(p.library==null||p.barAtlas==null||p.motifAtlas==null) throw new System.Exception("Missing analysis presentation assets");
            PrefabUtility.SaveAsPrefabAsset(root,prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();

        Preview(); Debug.Log("Analysis UI installed: 18 atlas rows, TempoFuryBrush, real progress, metadata, style reveal.");
    }
    public static void Configure(GameObject root)
    {
        var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/UI/Analysis/AnalysisBars.png");
        if(atlas==null) return; // Run Install once to author the atlas.
        var p=root.GetComponent<AnalysisPresentation>() ?? root.AddComponent<AnalysisPresentation>();
        p.library=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>("Assets/_Project/Configs/UI/StyleSkinLibrary.asset");
        p.barAtlas=atlas;
        p.motifAtlas=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/UI/Analysis/AnalysisMotifs.png");
            p.styleFlashShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/StyleRevealFlash.shader");
            p.styleRevealClip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Sfx/StyleRevealMagic.wav");
    }
    private static void Preview()
    {
        const string output="../VisualReviews/AnalysisUI";
        Directory.CreateDirectory(output);
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var canvasGo=new GameObject("PreviewCanvas",typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo,scene);
        var canvas=canvasGo.GetComponent<Canvas>();
        var cameraGo=new GameObject("PreviewCamera",typeof(Camera));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
        var cam=cameraGo.GetComponent<Camera>(); cam.clearFlags=CameraClearFlags.SolidColor; cam.backgroundColor=Color.black; cam.orthographic=true;
        cam.scene=scene;
        var rt=new RenderTexture(1920,1080,24); cam.targetTexture=rt;
        canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=cam; canvas.planeDistance=10;
        var scaler=canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1920,1080);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/AnalyzingScreen.prefab");
        var root=Object.Instantiate(prefab,canvasGo.transform); root.SetActive(true);
        UIFactory.Stretch((RectTransform)root.transform);
        var view=root.GetComponent<AnalyzingScreenView>(); var p=root.GetComponent<AnalysisPresentation>(); p.Build(view); p.Begin();
        view.titleText.text="ANALYSING SONG"; view.progressFill.fillAmount=.64f;
        root.transform.Find("AnalysisContent/Song").GetComponent<TMPro.TMP_Text>().text="Midnight Motion";
        root.transform.Find("AnalysisContent/Artist").GetComponent<TMPro.TMP_Text>().text="Alex Morgan";
        view.tipText.text="Finding the beat…";
        var apply=typeof(AnalysisPresentation).GetMethod("ApplyImmediate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var update=typeof(AnalysisPresentation).GetMethod("Update",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        try {
            Canvas.ForceUpdateCanvases(); update.Invoke(p,null); Canvas.ForceUpdateCanvases();
            cam.Render(); RenderTexture.active=rt;
            var neutral=new Texture2D(1920,1080,TextureFormat.RGB24,false); neutral.ReadPixels(new Rect(0,0,1920,1080),0,0);neutral.Apply();
            File.WriteAllBytes(output+"/Neutral.png",neutral.EncodeToPNG());Object.DestroyImmediate(neutral);
            for(int i=2;i<=17;i++) {
                apply.Invoke(p,new object[]{(GameMusicStyle)i}); Canvas.ForceUpdateCanvases(); update.Invoke(p,null); Canvas.ForceUpdateCanvases();
                cam.Render(); RenderTexture.active=rt;
                var png=new Texture2D(1920,1080,TextureFormat.RGB24,false); png.ReadPixels(new Rect(0,0,1920,1080),0,0); png.Apply();
                File.WriteAllBytes(output+"/"+(GameMusicStyle)i+".png",png.EncodeToPNG()); Object.DestroyImmediate(png);
            }
            apply.Invoke(p,new object[]{GameMusicStyle.Dance});
            DetectedStyleLabel.Show(view.titleText,GameMusicStyle.Dance,(RectTransform)canvasGo.transform);
            var reveal=canvasGo.transform.Find("DetectedStyle").GetComponent<TMPro.TextMeshProUGUI>();
            reveal.maxVisibleCharacters=int.MaxValue;reveal.rectTransform.localScale=Vector3.one;
            view.tipText.text="";Canvas.ForceUpdateCanvases();
            cam.Render();RenderTexture.active=rt;
            var detected=new Texture2D(1920,1080,TextureFormat.RGB24,false);detected.ReadPixels(new Rect(0,0,1920,1080),0,0);detected.Apply();
            File.WriteAllBytes(output+"/DetectedStyle.png",detected.EncodeToPNG());Object.DestroyImmediate(detected);
        } finally { RenderTexture.active=null; cam.targetTexture=null; Object.DestroyImmediate(rt); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }

}
#endif
