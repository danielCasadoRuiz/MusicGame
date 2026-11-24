#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class MenuBackgroundValidation
{
    [InitializeOnLoadMethod]
    private static void CheckRequest()
    {
        EditorApplication.delayCall += () =>
        {
            const string request = "Temp/MenuBackgroundValidation.request";
            if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request);
            Verify(); // Run after imports so captures use the latest prefab layering.
        };
    }

    [MenuItem("Tools/MusicGame/UI/Verify Menu Backgrounds")]
    public static void Verify()
    {
        try
        {
            var library = AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>("Assets/_Project/Configs/UI/StyleSkinLibrary.asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/MainMenu.prefab");
            var view = prefab.GetComponent<MainMenuView>();
            var styles = Enum.GetValues(typeof(GameMusicStyle)).Cast<GameMusicStyle>()
                .Where(s => s != GameMusicStyle.Unknown && s != GameMusicStyle.Classical).ToArray();
            if (library.menuBackgrounds.Count != styles.Length || styles.Any(s => library.MenuBackground(s) == null))
                throw new Exception("Missing or duplicate style backgrounds");
            if (library.MenuBackground(GameMusicStyle.Classical) != null)
                throw new Exception("Classical must keep its base background");
            if (view.menuBackground == null || view.previousMenuBackground == null || view.logo.library != library)
                throw new Exception("Missing prefab bindings");
            if (view.menuBackground.raycastTarget || view.previousMenuBackground.raycastTarget || library.composers == null)
                throw new Exception("Background blocks UI or composer reference is missing");
            if (view.menuBackground.transform.GetSiblingIndex() <= prefab.transform.Find("Dim").GetSiblingIndex())
                throw new Exception("Base dim layer would hide the style backgrounds");

            var canvasObject = new GameObject("Background review", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>();
            var cameraObject = new GameObject("Review camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.02f,.02f,.02f);
            camera.orthographic = true;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080);
            var instance = UnityEngine.Object.Instantiate(prefab, canvas.transform);
            var live = instance.GetComponent<MainMenuView>();
            instance.SetActive(true);
            UIFactory.Stretch(instance.GetComponent<RectTransform>());
            live.settingsPanel.SetActive(false);
            live.titleText.gameObject.SetActive(false);
            var target = new RenderTexture(960,540,24);
            camera.targetTexture = target;
            Directory.CreateDirectory("../VisualReviews/MenuBackgrounds");
            foreach (var style in styles)
            {
                live.logo.Show(style);
                live.buttonStyle.Apply(style,1f);
                live.ShowBackground(style);
                Canvas.ForceUpdateCanvases();
                live.ShowBackground(style);
                camera.Render();
                RenderTexture.active = target;
                var capture = new Texture2D(960,540,TextureFormat.RGB24,false);
                capture.ReadPixels(new Rect(0,0,960,540),0,0);
                capture.Apply();
                File.WriteAllBytes("../VisualReviews/MenuBackgrounds/"+style+".png",capture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(capture);
            }
            RenderTexture.active = null;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(canvasObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            File.WriteAllText("../menu-background-validation.txt", "PASS: all 16 styles imported and bound; Classical excluded; composers retained; backgrounds do not intercept clicks; 16 menu previews rendered.");
            Debug.Log("Menu background validation passed (16 styles).");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText("../menu-background-validation.txt", "FAIL: " + e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
#endif
