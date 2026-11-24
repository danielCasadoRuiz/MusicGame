#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class FrontendBackdropSetup
{
    private const string Screens = "Assets/_Project/Prefabs/UI/";
    private const string Cutout = "Assets/_Project/Art/UI/Jukebox/SecretCabinetFloorTransparent.png";

    [InitializeOnLoadMethod]
    private static void CheckRequest()
    {
        EditorApplication.playModeStateChanged -= AfterPlayMode;
        EditorApplication.playModeStateChanged += AfterPlayMode;
        EditorApplication.delayCall += () =>
        {
            const string request = "Temp/FrontendBackdropSetup.request";
            if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request);
            try { Install(); FrontendBackdropValidation.Verify(); }
            catch (Exception e)
            {
                File.WriteAllText("../frontend-backdrop-validation.txt", "FAIL: " + e);
                Debug.LogException(e);
            }
        };
    }

    private static void AfterPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) CheckRequest();
    }

    [MenuItem("Tools/MusicGame/UI/Setup Shared Frontend Backgrounds")]
    public static void Install()
    {
        AssetDatabase.ImportAsset(Cutout, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(Cutout);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        foreach (var screen in new[] { "Jukebox", "SongSelection", "AnalyzingScreen", "Progression/ProgressionScreen", "Collection/RivalCollectionScreen" })
        {
            string path = Screens + screen + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Configure(root);
                if (screen == "Jukebox")
                    root.transform.Find("Content/Cabinet").GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Cutout);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
    }

    public static void Configure(GameObject root)
    {
        var component = root.GetComponent<FrontendStyleBackground>();
        if (component == null) component = root.AddComponent<FrontendStyleBackground>();
        component.library = AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>("Assets/_Project/Configs/UI/StyleSkinLibrary.asset");
        component.backdrop = Layer(root, "SharedStyleBackground");
        component.previousBackdrop = Layer(root, "PreviousSharedStyleBackground");
        var oldBackground = root.transform.Find("Dim") ?? root.transform.Find("Background");
        if (oldBackground != null)
        {
            // The original screen plane remains the click shield; the new art is drawn above it.
            int index = oldBackground.GetSiblingIndex() + 1;
            component.backdrop.transform.SetSiblingIndex(index);
            component.previousBackdrop.transform.SetSiblingIndex(index + 1);
            if (root.GetComponent<JukeboxView>() != null)
            {
                var image = oldBackground.GetComponent<Image>();
                if (image != null) image.color = Color.clear;
                foreach (var receiver in oldBackground.GetComponents<ThemeElementReceiver>())
                    UnityEngine.Object.DestroyImmediate(receiver);
            }
        }
        else
        {
            component.previousBackdrop.transform.SetAsFirstSibling();
            component.backdrop.transform.SetAsFirstSibling();
        }
        // Leave authored prefabs neutral; OnEnable selects this launch's home at runtime.
        component.backdrop.enabled = component.previousBackdrop.enabled = false;
        component.backdrop.texture = component.previousBackdrop.texture = null;
        AppearanceSetup.ConfigureScreen(root);
    }

    private static RawImage Layer(GameObject root, string name)
    {
        var child = root.transform.Find(name);
        var rect = child != null ? (RectTransform)child : UIFactory.CreateRect(name, (RectTransform)root.transform);
        UIFactory.Stretch(rect);
        var image = rect.GetComponent<RawImage>();
        if (image == null) image = rect.gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        return image;
    }
}
#endif
