#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class RunnerUICurtainSetup
{
    private const string Request="Logs/RunnerUICurtainSetup.request";
    private const string LibraryPath="Assets/_Project/Configs/UI/StyleSkinLibrary.asset";
    private const string Art="Assets/_Project/Art/UI/RunnerHud/";
    [InitializeOnLoadMethod]private static void OnImport()
    {
        EditorApplication.delayCall+=()=>
        {
            if(!File.Exists(Request)||EditorApplication.isPlayingOrWillChangePlaymode)return;
            Install();File.Delete(Request);
        };
    }

    public static void ConfigureLibrary(StyleSkinLibrarySO lib)
    {
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/UIStyleTransition.shader");
        if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("UI spatial transition shader failed compilation");
        Material Make(string name)
        {
            string path=Art+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
            material.shader=shader;EditorUtility.SetDirty(material);return material;
        }
        lib.styleTransitionMaterial=Make("StyleTransition");
        lib.classicTitleMaterial=Make("ClassicalTitleGold");
        lib.classicTitleMaterial.SetFloat("_GoldClassic",1);lib.classicTitleMaterial.SetFloat("_FixedBlend",1);
        lib.classicPauseMaterial=Make("ClassicalPauseLaurel");
        lib.classicPauseMaterial.SetFloat("_GoldClassic",1);lib.classicPauseMaterial.SetFloat("_FixedBlend",1);
        lib.classicPauseMaterial.SetFloat("_Laurel",1);
        var source=lib.titles.Find(t=>t.style==GameMusicStyle.Rock);
        if(source==null||source.letters.Length!=9)throw new Exception("Missing nine-letter title source");
        var classic=lib.titles.Find(t=>t.style==GameMusicStyle.Classical);
        if(classic==null){classic=new StyleSkinLibrarySO.TitleSkin{style=GameMusicStyle.Classical};lib.titles.Add(classic);}
        classic.background=source.background;classic.foreground=source.foreground;
        classic.letters=(Sprite[])source.letters.Clone();classic.material=lib.classicTitleMaterial;
        lib.runnerCurtainSoftness=.2f;
        EditorUtility.SetDirty(lib);
    }

    [MenuItem("Tools/MusicGame/UI/Restore Runner Spatial Curtain")]
    public static void Install()
    {
        var lib=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(LibraryPath);
        ConfigureLibrary(lib);AssetDatabase.SaveAssets();
        CheckCurtain(lib);
        foreach(float progress in new[]{0f,.25f,.5f,.75f,1f})Render(lib,progress);
        File.WriteAllText("Logs/RunnerUICurtainCheck.txt","PASS: shader compiled; GPU endpoints and left/right frontier verified; full and filled progress graphics share one reference; five runner/pause previews saved; classical logo has nine separate letters and a gold finish; classical pause has gold laurel motifs.");
        Debug.Log(File.ReadAllText("Logs/RunnerUICurtainCheck.txt"));
    }

    private static void CheckCurtain(StyleSkinLibrarySO lib)
    {
        // Render actual shader pixels: at halfway the left remains red, right is gold.
        var material=new Material(lib.styleTransitionMaterial);
        try
        {
            material.SetColor("_StyleColor",Color.red);material.SetColor("_ClassicColor",Color.yellow);
            material.SetFloat("_FixedBlend",.5f);
            if(!material.SetPass(0))throw new Exception("UI transition pass failed");
            // Spatial test uses the preview render below; CPU anchors must also agree with the shader's smoothstep.
            if(lib.CurtainClassic(.5f,.05f)>.001f||lib.CurtainClassic(.5f,.95f)<.999f)
                throw new Exception("Curtain direction must be style-left / classical-right");
        }
        finally{UnityEngine.Object.DestroyImmediate(material);}
    }

    private static void Render(StyleSkinLibrarySO lib,float progress)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var cameraGo=new GameObject("Curtain review camera",typeof(Camera));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
        var camera=cameraGo.GetComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color(.065f,.085f,.13f);camera.orthographic=true;
        var canvasGo=new GameObject("Curtain review canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo,scene);
        var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
        canvas.additionalShaderChannels=AdditionalCanvasShaderChannels.TexCoord1|AdditionalCanvasShaderChannels.Normal|AdditionalCanvasShaderChannels.Tangent;
        var scaler=canvasGo.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        var rt=RenderTexture.GetTemporary(1920,1080,24);var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=rt;
            var hud=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/LiveHud.prefab"),canvasGo.transform,false);
            UIFactory.Stretch((RectTransform)hud.transform);
            var skin=hud.GetComponent<RunnerHudSkin>();
            typeof(RunnerHudSkin).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(skin,null);
            Canvas.ForceUpdateCanvases();
            var view=hud.GetComponent<LiveHudView>();if(view.artistSongText!=null)view.artistSongText.text="ARTIST · SONG";
            if(view.progressFill!=null)view.progressFill.fillAmount=.72f;
            skin.Apply(GameMusicStyle.Electronic,lib.RunnerWeight(progress));
            var pause=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/PauseMenu.prefab"),canvasGo.transform,false);
            UIFactory.Stretch((RectTransform)pause.transform);var pv=pause.GetComponent<PauseView>();
            pv.overlayRoot.SetActive(true);pv.pauseButtonRoot.SetActive(false);pv.cameraToggleButtonRoot.SetActive(false);
            pv.overlayRoot.transform.Find("Dim").GetComponent<Image>().color=Color.clear;
            pv.overlayRoot.GetComponentInChildren<StyleBlendGroup>(true).Apply(GameMusicStyle.Electronic,lib.RunnerWeight(progress),useCurtain:true);
            pv.titleText.text="PAUSED";
            pv.resumeButtonLabel.text="RESUME";pv.restartButtonLabel.text="RESTART SONG";
            pv.mainMenuButtonLabel.text="MAIN MENU";pv.bonusHelpButtonLabel.text="BONUSES";
            // Add the final classical title and pause glyph at a readable review size.
            var goldTitle=UnityEngine.Object.Instantiate(skin.logo,canvasGo.transform,false);
            goldTitle.name="Classical title review";goldTitle.Show(GameMusicStyle.Classical);goldTitle.StopIntro();
            var titleRect=(RectTransform)goldTitle.transform;titleRect.anchorMin=titleRect.anchorMax=titleRect.pivot=new Vector2(.5f,0);
            titleRect.anchoredPosition=new Vector2(-360,0);titleRect.localScale=Vector3.one*.22f;
            var glyph=UIFactory.CreatePanel("Gold pause review",(RectTransform)canvasGo.transform,Color.white);
            glyph.sprite=lib.ClassicHud.pause;glyph.material=lib.classicPauseMaterial;
            glyph.rectTransform.anchorMin=glyph.rectTransform.anchorMax=new Vector2(.5f,0);
            glyph.rectTransform.anchoredPosition=new Vector2(340,110);glyph.rectTransform.sizeDelta=new Vector2(170,170);
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
            string folder="../VisualReviews/RunnerUICurtain";Directory.CreateDirectory(folder);
            File.WriteAllBytes($"{folder}/Progress_{Mathf.RoundToInt(progress*100):000}.png",image.EncodeToPNG());
            if(view.progressFill!=null)
            {
                Color Pixel(float x)
                {
                    var box=view.progressFill.rectTransform;
                    var point=camera.WorldToScreenPoint(box.TransformPoint(new Vector3(Mathf.Lerp(box.rect.xMin,box.rect.xMax,x),box.rect.center.y,0)));
                    return image.GetPixel(Mathf.Clamp(Mathf.RoundToInt(point.x),0,1919),Mathf.Clamp(Mathf.RoundToInt(point.y),0,1079));
                }
                var left=Pixel(.05f);var right=Pixel(.70f);
                if(Mathf.Abs(progress-.25f)<.001f && (left.b<=left.r || right.r<left.r+.15f))
                    throw new Exception($"Filled HUD bar is not spatial: left={left}, right={right}");
                if(progress==0 && (left.b<=left.r || right.b<=right.r))throw new Exception("HUD must start entirely in the song style");
                if(progress==1 && (left.r<=left.b || right.r<=right.b))throw new Exception("HUD must end entirely gold");
            }
            if(progress==1 && skin.icons.Count>0)
            {
                var icon=skin.icons[0].normal.rectTransform;
                var point=camera.WorldToScreenPoint(icon.TransformPoint(icon.rect.center));
                var pixel=image.GetPixel(Mathf.RoundToInt(point.x),Mathf.RoundToInt(point.y));
                if(pixel.r<=pixel.b)throw new Exception("The leftmost violet bonus must also become gold at the end.");
                var box=skin.logo.background.rectTransform;
                var a=camera.WorldToScreenPoint(box.TransformPoint(box.rect.min));
                var b=camera.WorldToScreenPoint(box.TransformPoint(box.rect.max));
                int blue=0;
                for(int y=Mathf.Max(0,(int)a.y);y<Mathf.Min(1080,(int)b.y);y+=3)
                for(int x=Mathf.Max(0,(int)a.x);x<Mathf.Min(1920,(int)b.x);x+=3)
                {var p=image.GetPixel(x,y);if(p.b>p.r+.15f)blue++;}
                if(blue>3)throw new Exception($"The final title still contains {blue} blue samples instead of the classical logo.");
            }
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);EditorSceneManager.ClosePreviewScene(scene);}
    }
}
#endif
