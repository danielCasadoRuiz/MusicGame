#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class PausePresentationSetup
{
    private const string Request = "Logs/PausePresentationSetup.request";

    [InitializeOnLoadMethod]
    private static void OnImport()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Install();
            File.Delete(Request);
        };
    }

    public static void Configure(PauseView view)
    {
        var dim = view.overlayRoot.transform.Find("Dim");
        if (dim != null)
        {
            RemoveTheme(dim.gameObject);
            var image = dim.GetComponent<Image>();
            image.color = new Color(0, 0, 0, .45f);
            image.raycastTarget = true;
        }
        var panel = view.overlayRoot.transform.Find("Panel");
        if (panel != null)
        {
            RemoveTheme(panel.gameObject);
            var image = panel.GetComponent<Image>();
            if (image != null) { image.color = Color.clear; image.raycastTarget = false; }
            foreach (var outline in panel.GetComponents<Outline>()) Object.DestroyImmediate(outline);
        }
        var toggle = view.cameraToggleButton;
        if (toggle != null)
        {
            RemoveTheme(toggle.gameObject);
            toggle.transition = Selectable.Transition.None;
            if (toggle.targetGraphic != null) toggle.targetGraphic.color = Color.clear;
            if (view.cameraToggleIcon == null)
            {
                var icon = new GameObject("CameraIcon", typeof(RectTransform), typeof(Image));
                icon.transform.SetParent(toggle.transform, false);
                view.cameraToggleIcon = icon.GetComponent<Image>();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Resources/UI/CameraViewOutline.png");
            view.cameraToggleIcon.sprite = sprite;
            view.cameraToggleIcon.enabled = true;
            view.cameraToggleIcon.preserveAspect = true;
            view.cameraToggleIcon.raycastTarget = false;
            view.cameraToggleIcon.color = new Color(.85f, .9f, 1f, .65f);
            var rt = view.cameraToggleIcon.rectTransform;
            rt.anchorMin = new Vector2(.52f, 0); rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4, 8); rt.offsetMax = new Vector2(-8, -8);
            view.thirdPersonIcon = view.firstPersonIcon = sprite;
        }
    }

    private static void RemoveTheme(GameObject go)
    {
        foreach (var receiver in go.GetComponents<ThemeColorReceiver>()) Object.DestroyImmediate(receiver);
    }

    [MenuItem("Tools/MusicGame/UI/Update Pause Presentation")]
    public static void Install()
    {
        const string path = "Assets/_Project/Prefabs/UI/PauseMenu.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var view = root.GetComponent<PauseView>();
            Configure(view);
            if (view.cameraToggleIcon == null || view.cameraToggleIcon.sprite == null)
                throw new System.Exception("Pause camera icon is missing.");
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[PausePresentation] Transparent veil, floating buttons and camera icon saved.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
#endif
