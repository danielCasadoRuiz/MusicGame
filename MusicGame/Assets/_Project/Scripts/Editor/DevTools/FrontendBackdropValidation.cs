#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class FrontendBackdropValidation
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [MenuItem("Tools/MusicGame/UI/Verify Shared Frontend Backgrounds")]
    public static void Verify()
    {
        const string screens = "Assets/_Project/Prefabs/UI/";
        var library = AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>("Assets/_Project/Configs/UI/StyleSkinLibrary.asset");
        var home = StyledTitleView.MenuStyle(library);
        var homeTexture = library.MenuBackground(home);
        Check(homeTexture != null, "Home texture missing");
        var canvasObject = new GameObject("Frontend background verification", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var cameraObject = new GameObject("Frontend background review camera", typeof(Camera));
        var canvas = canvasObject.GetComponent<Canvas>();
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.orthographic = true;
        var rt = new RenderTexture(1280,720,24);
        camera.targetTexture = rt;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 10;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920,1080);
        Directory.CreateDirectory("../VisualReviews/SharedFrontendBackground");
        GameObject live = null;
        try
        {
            foreach (string screen in new[] { "Jukebox", "SongSelection", "AnalyzingScreen", "Progression/ProgressionScreen", "Collection/RivalCollectionScreen" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(screens + screen + ".prefab");
                Check(prefab != null, screen + " prefab missing");
                live = UnityEngine.Object.Instantiate(prefab, canvas.transform);
                UIFactory.Stretch((RectTransform)live.transform);
                live.SetActive(true);
                var background = live.GetComponent<FrontendStyleBackground>();
                Check(background != null && background.library == library, screen + " background binding missing");
                background.ShowHome();
                Check(background.backdrop.texture == homeTexture && background.backdrop.enabled, screen + " does not inherit home");
                Check(!background.backdrop.raycastTarget && !background.previousBackdrop.raycastTarget, screen + " blocks input");
                if (screen == "Jukebox")
                {
                    var view = live.GetComponent<JukeboxView>();
                    view.dialog.SetActive(false);
                    view.ApplyBrand(home);
                    view.ConfigurePlaybackHeader();
                    Canvas.ForceUpdateCanvases();
                    view.FitCabinet();
                    var viewport = ((RectTransform)live.transform).rect.size;
                    var design = view.cabinetContent.rect.size;
                    var fitted = Vector2.Scale(design, view.cabinetContent.localScale);
                    Check(Mathf.Abs(fitted.x - viewport.x) < 2f && Mathf.Abs(fitted.y - viewport.y) < 2f,
                        "Jukebox art and controls do not fill the 16:9 viewport together");
                    var cabinet = live.transform.Find("Content/Cabinet").GetComponent<Image>();
                    Check(cabinet.sprite.name == "SecretCabinetFloorTransparent", "Jukebox is using the opaque art");
                    Check(live.transform.Find("Background").GetComponent<Image>().color.a == 0, "Jukebox backdrop is opaque");
                    Capture(camera, rt, screen);
                }
                else if (screen != "AnalyzingScreen")
                {
                    if (screen == "Collection/RivalCollectionScreen")
                        live.GetComponent<RivalCollectionView>().detailRoot.gameObject.SetActive(false);
                    Check(background.backdrop.transform.GetSiblingIndex() > live.transform.Find("Dim").GetSiblingIndex(),
                        screen + " old dim hides the new background");
                    Capture(camera, rt, System.IO.Path.GetFileName(screen));
                    live.SetActive(false);
                    live.SetActive(true);
                    Check(background.backdrop.texture == homeTexture && background.backdrop.enabled,
                        screen + " loses current style on reopening");
                }
                else
                {
                    var view = live.GetComponent<AnalyzingScreenView>();
                    var presentation = live.GetComponent<AnalysisPresentation>();
                    presentation.Build(view);
                    presentation.Begin();
                    Check(background.backdrop.texture == homeTexture, "First analysis does not start in home style");
                    Capture(camera, rt, "Analysis-before-detection");
                    var apply = typeof(AnalysisPresentation).GetMethod("ApplyTheme", BindingFlags.Instance | BindingFlags.NonPublic);
                    foreach (var style in library.menuBackgrounds.Select(b => b.style))
                    {
                        presentation.Begin();
                        presentation.Reveal(style);
                        Check(background.backdrop.texture == library.MenuBackground(style), "Wrong detected background: " + style);
                        Check(style == home || background.previousBackdrop.color.a == 1, "Background cut before fade: " + style);
                        apply.Invoke(presentation, new object[] { AnalysisPresentation.Accent(style), .5f });
                        Check(style == home || Mathf.Approximately(background.previousBackdrop.color.a,.5f), "Mid fade incorrect: " + style);
                        if (style == GameMusicStyle.Electronic) Capture(camera, rt, "Analysis-mid-fade");
                        apply.Invoke(presentation, new object[] { AnalysisPresentation.Accent(style), 1f });
                        Check(!background.previousBackdrop.enabled, "Old background remains after fade: " + style);
                        if (style == GameMusicStyle.Electronic) Capture(camera, rt, "Analysis-detected-Electronic");
                    }
                    presentation.Begin();
                    Check(background.backdrop.texture == homeTexture && !background.previousBackdrop.enabled, "Retry retains the previous song's background");
                    presentation.Reveal(GameMusicStyle.Classical);
                    Check(background.backdrop.texture == homeTexture, "Classical should retain the base home background");
                    DetectedStyleLabel.Cancel();
                }
                UnityEngine.Object.DestroyImmediate(live);
                live = null;
            }
            File.WriteAllText("../frontend-backdrop-validation.txt", "PASS: Jukebox, Song Selection, Progression and Collection inherit home, including reopening; initial analysis inherits home; all 16 detected styles crossfade at 0/50/100%; retry resets home; Classical retains base; transparent cabinet assigned; UI input unblocked. Preview captures generated.");
            Debug.Log("Shared frontend background validation passed.");
        }
        finally
        {
            if (live != null) UnityEngine.Object.DestroyImmediate(live);
            RenderTexture.active = null;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(canvasObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    private static void Capture(Camera camera, RenderTexture target, string name)
    {
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
        image.Apply();
        File.WriteAllBytes("../VisualReviews/SharedFrontendBackground/"+name+".png",image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        RenderTexture.active = null;
    }
}
#endif
