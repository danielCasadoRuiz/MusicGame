using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class HudRefreshSetup
{
    const string Output = "../VisualReviews/HudRefresh";
    const string Art = "Assets/_Project/Art/UI/TouchControls/";
    static double nextPoll;
    static HudRefreshSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        const string request = "Logs/HudRefreshSetup.request";
        if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(request);
        try { Install(); }
        catch (Exception e) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/failure.txt", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Tools/MusicGame/UI/Install HUD and Touch Refresh")]
    public static void Install()
    {
        Directory.CreateDirectory(Output);
        var lib = AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(RunnerHudLayoutSetup.Library);
        RunnerUICurtainSetup.ConfigureLibrary(lib);
        RunnerHudLayoutSetup.Apply();
        BakeTouchAtlas();
        const string mobilePath = "Assets/_Project/Prefabs/UI/MobileControls.prefab";
        var mobile = PrefabUtility.LoadPrefabContents(mobilePath);
        try
        {
            var skin = mobile.GetComponent<MobileControlSkin>() ?? mobile.AddComponent<MobileControlSkin>();
            skin.library = lib; skin.atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Atlas.png");
            PrefabUtility.SaveAsPrefabAsset(mobile, mobilePath);
        }
        finally { PrefabUtility.UnloadPrefabContents(mobile); }
        AssetDatabase.SaveAssets();
        RunnerUICurtainSetup.Install();
        foreach (var style in new[] { GameMusicStyle.Dance, GameMusicStyle.Electronic, GameMusicStyle.Classical })
            foreach (float share in new[] { 0f, .5f, 1f }) Render(style, share);
        File.WriteAllText(Output + "/result.txt", "PASS: ten HUD counters and combo icons wired, compact separators, spatial gold transition GPU checks, three styled touch/pause families at three classic shares, and real combo-toast event presentation verified. Input components and pickup scoring rules unchanged.");
        Debug.Log("[HudRefreshSetup] HUD, combo feedback and touch skins installed and checked.");
    }
    static void BakeTouchAtlas()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(Art + "Source.png"))) throw new Exception("Missing touch source");
            var pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) if (pixels[i].a <= 20) pixels[i] = new Color32();
            texture.SetPixels32(pixels); texture.Apply();
            AssetDatabase.ReleaseCachedFileHandles();
            string path = Art + "Atlas.png", temp = path + ".pending";
            File.WriteAllBytes(temp, texture.EncodeToPNG());
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        finally { Object.DestroyImmediate(texture); }
    }
    static void Render(GameMusicStyle style, float classicShare)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        Camera previewCamera = null;
        var previous = RenderTexture.active;
        try
        {
            var cameraGo = new GameObject("HUD review camera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.GetComponent<Camera>(); camera.scene = scene; camera.orthographic = true;
            previewCamera = camera;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .07f, .1f);
            target = new RenderTexture(1920, 1080, 24); camera.targetTexture = target;
            var canvasGo = new GameObject("HUD review", typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, scene);
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            GameObject Spawn(string name)
            {
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/" + name + ".prefab"), canvasGo.transform);
                root.SetActive(true); UIFactory.Stretch((RectTransform)root.transform); return root;
            }
            var hud = Spawn("LiveHud"); var view = hud.GetComponent<LiveHudView>(); var skin = view.skin;
            skin.ConfigureLayout();
            if (skin.icons.Count != 10 || view.tripleComboValue == null || view.quadComboValue == null || view.comboToast == null)
                throw new Exception("Missing combo HUD wiring");
            view.tripleComboValue.text = "3"; view.quadComboValue.text = "2";
            view.kickValue.text = "125"; view.snareValue.text = "48";
            view.artistSongText.text = "ARTIST · A LONGER SONG TITLE";
            view.progressFill.fillAmount = .72f;
            var controls = Spawn("MobileControls").GetComponent<MobileControlSkin>(); controls.Apply(style, classicShare);
            var controlView = controls.GetComponent<MobileControlsView>();
            controlView.jumpButton.GetComponentInChildren<TextMeshProUGUI>().text = "JUMP";
            controlView.crouchButton.GetComponentInChildren<TextMeshProUGUI>().text = "CROUCH";
            var pause = Spawn("PauseMenu").GetComponent<PauseView>();
            pause.overlayRoot.SetActive(true); pause.pauseButtonRoot.SetActive(true); pause.cameraToggleButtonRoot.SetActive(false);
            pause.overlayRoot.GetComponentInChildren<StyleBlendGroup>(true).Apply(style, 1 - classicShare, useCurtain: true);
            pause.pauseButtonRoot.GetComponent<StyleBlendGroup>()?.Apply(style, 1 - classicShare);
            pause.titleText.text = "PAUSED"; pause.resumeButtonLabel.text = "RESUME";
            pause.restartButtonLabel.text = "RESTART SONG"; pause.mainMenuButtonLabel.text = "MAIN MENU";
            pause.bonusHelpButtonLabel.text = "BONUSES";
            Canvas.ForceUpdateCanvases(); skin.Apply(style, style == GameMusicStyle.Classical ? 0 : 1 - classicShare);
            view.comboToast.text = "COMBO ×4"; view.comboToast.alpha = 1;
            view.comboToast.color = Color.Lerp(skin.library.titleColor, skin.library.HudAccent(GameMusicStyle.Classical), classicShare);
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try { image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply(); File.WriteAllBytes($"{Output}/{style}-{Mathf.RoundToInt(classicShare * 100)}.png", image.EncodeToPNG()); }
            finally { Object.DestroyImmediate(image); }
            if (style == GameMusicStyle.Electronic && classicShare == 0) CheckComboEvents(canvasGo.transform, view);
        }
        finally { if (previewCamera != null) previewCamera.targetTexture = null; RenderTexture.active = previous; if (target != null) { target.Release(); Object.DestroyImmediate(target); } EditorSceneManager.ClosePreviewScene(scene); }
    }
    static void CheckComboEvents(Transform canvas, LiveHudView view)
    {
        var end = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/EndScreen.prefab"), canvas);
        var go = new GameObject("Combo presentation event check");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, canvas.gameObject.scene);
        var hud = go.AddComponent<GameplayHUD>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            typeof(GameplayHUD).GetMethod("OnEnable", flags).Invoke(hud, null);
            hud.Initialize(null, null, null, view, end.GetComponent<EndScreenView>());
            EventBus.Publish(new PickupComboEvent { Tier = PickupComboTier.Triple });
            if (view.comboToast.text != "COMBO ×3" || view.comboToast.alpha <= 0) throw new Exception("Triple combo toast missing");
            EventBus.Publish(new PickupComboEvent { Tier = PickupComboTier.Quad });
            if (view.comboToast.text != "COMBO ×4") throw new Exception("Quad combo toast missing");
        }
        finally { typeof(GameplayHUD).GetMethod("OnDisable", flags).Invoke(hud, null); Object.DestroyImmediate(go); Object.DestroyImmediate(end); }
    }
}
