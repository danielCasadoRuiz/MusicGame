#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class MainMenuScaleSetup
{
    const string Path="Assets/_Project/Prefabs/UI/MainMenu.prefab";
    static MainMenuScaleSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        string request="Temp/MainMenuScale.request";
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(EditorApplication.isPlaying){EditorApplication.ExitPlaymode();return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request);
        var root=PrefabUtility.LoadPrefabContents(Path);
        try{Configure(root.GetComponent<MainMenuView>());PrefabUtility.SaveAsPrefabAsset(root,Path);}
        finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();Capture();
        File.WriteAllText("../main-menu-scale.txt","Applied 120% central menu scale with responsive fit. Utility icons, backgrounds and campaign status stay independent.");
    }
    public static void Configure(MainMenuView view)
    {
        var group=view.transform.Find("MainMenuPresentation") as RectTransform;
        if(group==null)group=UIFactory.CreateRect("MainMenuPresentation",(RectTransform)view.transform);
        var centre=Vector2.one*.5f;
        UIFactory.SetBox(group,centre,centre,centre,new Vector2(0,-10),new Vector2(1920,1080));
        view.presentationRoot=group;view.presentationScale=1.2f;group.localScale=Vector3.one*1.2f;
        foreach(var button in new[]{view.playButton,view.rivalsButton,view.progressionButton,view.jukeboxButton,view.quitButton})
            if(button!=null&&button.transform.parent!=group)button.transform.SetParent(group,false);
        var logo=view.logo.transform.parent;
        if(logo.parent!=group)logo.SetParent(group,false);
        logo.SetAsLastSibling();
        if(view.settingsPanel!=null)view.settingsPanel.transform.SetAsLastSibling();
    }
    static void Capture()
    {
        var holder=new GameObject("Menu scale review",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var camObject=new GameObject("Menu review camera",typeof(Camera));var camera=camObject.GetComponent<Camera>();
        var target=new RenderTexture(1920,1080,24);camera.targetTexture=target;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;
        var canvas=holder.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
        var scaler=holder.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        try
        {
            var live=Object.Instantiate(AssetDatabase.LoadAssetAtPath<MainMenuView>(Path),holder.transform);UIFactory.Stretch((RectTransform)live.transform);live.gameObject.SetActive(true);
            live.settingsPanel.SetActive(false);live.quitButton.gameObject.SetActive(false);
            live.logo.Show(GameMusicStyle.Electronic);live.buttonStyle.Apply(GameMusicStyle.Electronic,1);live.ShowBackground(GameMusicStyle.Electronic);
            live.playButtonLabel.text="PLAY";live.rivalsButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text="COLLECTION";
            live.progressionButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text="PROGRESSION";live.jukeboxButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text="JUKEBOX";
            live.profileText.gameObject.SetActive(true);live.profileText.text="LEVEL 2 · match 4 of 5";
            Canvas.ForceUpdateCanvases();live.FitPresentation();camera.Render();RenderTexture.active=target;
            var capture=new Texture2D(1920,1080,TextureFormat.RGB24,false);capture.ReadPixels(new Rect(0,0,1920,1080),0,0);capture.Apply();
            Directory.CreateDirectory("../VisualReviews/MainMenu");File.WriteAllBytes("../VisualReviews/MainMenu/menu-120-percent.png",capture.EncodeToPNG());Object.DestroyImmediate(capture);
        }
        finally{RenderTexture.active=null;camera.targetTexture=null;Object.DestroyImmediate(target);Object.DestroyImmediate(holder);Object.DestroyImmediate(camObject);}
    }
}
#endif
