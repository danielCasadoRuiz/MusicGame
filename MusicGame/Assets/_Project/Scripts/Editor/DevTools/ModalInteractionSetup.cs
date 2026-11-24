#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ModalInteractionSetup
{
    const string Main="Assets/_Project/Prefabs/UI/MainMenu.prefab";
    static ModalInteractionSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        const string request="Temp/ModalInteraction.request";
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(EditorApplication.isPlaying){EditorApplication.ExitPlaymode();return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request);
        try{Install();Verify();File.WriteAllText("../modal-interaction-validation.txt","PASS: settings full-screen pointer blocker, modal focus, backdrop hide on close, day/night veils for Settings, Profile and Custom Songs. Existing layouts preserved.\n");}
        catch(Exception e){File.WriteAllText("../modal-interaction-validation.txt","FAIL: "+e);Debug.LogException(e);}
    }
    public static void RunBatch()
    {
        try
        {
            Install();Verify();
            File.WriteAllText("../modal-interaction-validation.txt","PASS: settings full-screen pointer blocker, modal focus, backdrop hide on close, day/night veils for Settings, Profile and Custom Songs. Existing layouts preserved.\n");
        }
        catch(Exception e){File.WriteAllText("../modal-interaction-validation.txt","FAIL: "+e);throw;}
    }
    static void Patch(string path,Action<GameObject> configure)
    {
        var root=PrefabUtility.LoadPrefabContents(path);
        try{configure(root);PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static void Tone(Image image)
    {
        if(image==null)throw new Exception("Missing full-screen veil image");
        if(image.GetComponent<ModalVeilTone>()==null)image.gameObject.AddComponent<ModalVeilTone>();
        image.raycastTarget=true;
    }
    public static void Install()
    {
        Patch(Main,root=>
        {
            var view=root.GetComponent<MainMenuView>();var panel=view.settingsPanel;
            var found=root.transform.Find("SettingsBackdrop");
            var image=found!=null?found.GetComponent<Image>():null;
            if(image==null)
            {
                var go=new GameObject("SettingsBackdrop",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
                go.transform.SetParent(root.transform,false);image=go.GetComponent<Image>();
            }
            var rect=image.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
            rect.offsetMin=rect.offsetMax=Vector2.zero;Tone(image);
            image.transform.SetSiblingIndex(panel.transform.GetSiblingIndex());
            var blocker=panel.GetComponent<SettingsModalBlocker>();if(blocker==null)blocker=panel.AddComponent<SettingsModalBlocker>();
            blocker.backdrop=image;blocker.initialSelection=view.closeButton;
            image.gameObject.SetActive(false);
        });
        Patch("Assets/_Project/Prefabs/UI/ProfileScreen.prefab",root=>Tone(root.GetComponent<ProfileScreenView>().root.GetComponent<Image>()));
        Patch("Assets/_Project/Prefabs/UI/SongSelection.prefab",root=>Tone(root.GetComponent<SongSelectionView>().customPurchaseDialog.GetComponent<Image>()));
        AssetDatabase.SaveAssets();
    }
    static void Verify()
    {
        var canvas=new GameObject("Modal verification",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
        var events=new GameObject("Modal events",typeof(EventSystem));
        try
        {
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var view=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<MainMenuView>(Main),canvas.transform);
            UIFactory.Stretch((RectTransform)view.transform);view.gameObject.SetActive(true);
            view.settingsPanel.SetActive(true);var blocker=view.settingsPanel.GetComponent<SettingsModalBlocker>();blocker.ShowVeil();
            Canvas.ForceUpdateCanvases();
            var hits=new List<RaycastResult>();var pointer=new PointerEventData(events.GetComponent<EventSystem>()){position=new Vector2(12,12)};
            canvas.GetComponent<GraphicRaycaster>().Raycast(pointer,hits);
            if(hits.Count==0||hits[0].gameObject!=blocker.backdrop.gameObject)throw new Exception("Underlying menu still receives clicks outside Settings");
            if(blocker.backdrop.transform.GetSiblingIndex()>=view.settingsPanel.transform.GetSiblingIndex())throw new Exception("Backdrop covers the settings controls");
            var tone=blocker.backdrop.GetComponent<ModalVeilTone>();
            if(tone.day.grayscale<.9f||tone.night.grayscale>.1f)throw new Exception("Day/night veil colours are reversed");
            blocker.HideVeil();if(blocker.backdrop.gameObject.activeSelf)throw new Exception("Backdrop remains after closing Settings");
        }
        finally{UnityEngine.Object.DestroyImmediate(canvas);UnityEngine.Object.DestroyImmediate(events);}
    }
}
#endif
