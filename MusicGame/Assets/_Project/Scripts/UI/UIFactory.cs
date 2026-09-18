using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Minimal uGUI construction helpers — the one place that knows how to build a Text/Button/etc. at
/// runtime, still used for screens that haven't been converted to real prefabs yet, and for
/// dynamic, per-item content within a prefab-based screen (e.g. SongCard instances). Text is
/// TextMeshProUGUI — this project's ONE UI text technology (Section 2 of the "UI unification"
/// plan); nothing here builds legacy UnityEngine.UI.Text.
///
/// The root Canvas/EventSystem are explicitly homed in the always-loaded "UI" Scene (see
/// MoveToUiSceneIfLoaded) regardless of which script/scene calls RootCanvas() first — required
/// since the multi-scene refactor, where the "active" scene shifts as Mode Scenes load/unload.
/// </summary>
public static class UIFactory
{
    private static Canvas _canvasInstance;

    /// <summary>Shared root Canvas — created once, reused by every gameplay UI script. Named
    /// RootCanvas (not "Canvas") to avoid colliding with the UnityEngine.Canvas TYPE used inside
    /// this very method — a method and a type sharing one simple name in the same static class
    /// is a real source of "is a method, which is not valid in the given context" compile errors.</summary>
    public static RectTransform RootCanvas()
    {
        if (_canvasInstance == null)
        {
            var go = new GameObject("[UI Canvas]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasInstance = go.GetComponent<Canvas>();
            _canvasInstance.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvasInstance.sortingOrder = 100;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;

            MoveToUiSceneIfLoaded(go);
            EnsureEventSystem();
        }
        return _canvasInstance.GetComponent<RectTransform>();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("[EventSystem]", typeof(EventSystem), typeof(InputSystemUIInputModule));
        MoveToUiSceneIfLoaded(go);
    }

    // Everything UIFactory builds is meant to live in the always-loaded "UI" Scene (see the
    // multi-scene refactor plan), not whatever scene happens to be SceneManager.GetActiveScene() at
    // creation time — the active scene shifts to whichever Mode Scene SceneFlowController.LoadMode
    // last activated, so without this a UI root built while a Mode Scene is active would be
    // destroyed the instant that Mode Scene unloads. Only the two ROOT objects (Canvas, EventSystem)
    // need this — everything else UIFactory creates gets SetParent'd under the Canvas, and Unity
    // automatically moves a reparented object into its new parent's scene.
    private static void MoveToUiSceneIfLoaded(GameObject go)
    {
        var uiScene = SceneManager.GetSceneByName("UI");
        if (uiScene.IsValid() && uiScene.isLoaded)
            SceneManager.MoveGameObjectToScene(go, uiScene);
        else
            Debug.LogWarning($"[UIFactory] 'UI' scene not loaded yet — '{go.name}' will live in " +
                              "whatever scene is currently active instead.");
    }

    // ── Primitives ──────────────────────────────────────────────────────────

    public static RectTransform CreateRect(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    public static Image CreatePanel(string name, RectTransform parent, Color color)
    {
        var rt  = CreateRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    public static TextMeshProUGUI CreateText(string name, RectTransform parent, string content, int fontSize,
        Color color, TextAlignmentOptions anchor = TextAlignmentOptions.Center, FontStyles style = FontStyles.Normal)
    {
        var rt  = CreateRect(name, parent);
        var txt = rt.gameObject.AddComponent<TextMeshProUGUI>();
        txt.text                = content;
        txt.fontSize             = fontSize;
        txt.color                = color;
        txt.alignment            = anchor;
        txt.fontStyle            = style;
        txt.enableWordWrapping   = false;
        txt.overflowMode         = TextOverflowModes.Overflow;
        return txt;
    }

    /// <summary>Real Button component — onClick fires through EventSystem/InputSystemUIInputModule,
    /// not IMGUI's control-ID/hotControl bookkeeping (the source of the old double-click bug: a
    /// GUI.Button whose branch stops being drawn between MouseDown and MouseUp leaves
    /// GUIUtility.hotControl stuck, silently eating the next click).</summary>
    public static Button CreateButton(string name, RectTransform parent, string label, out TextMeshProUGUI labelText)
    {
        var rt  = CreateRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.12f);

        var btn = rt.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = new Color(1f, 1f, 1f, 0.12f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.22f);
        colors.pressedColor     = new Color(1f, 1f, 1f, 0.40f);
        colors.disabledColor    = new Color(1f, 1f, 1f, 0.05f);
        btn.colors = colors;

        labelText = CreateText(name + "Label", rt, label, 15, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(labelText.rectTransform);
        return btn;
    }

    /// <summary>Background + horizontally-filled foreground — used for progress/rating bars.</summary>
    public static Image CreateFillBar(string name, RectTransform parent, Color bg, Color fill, out Image fillImage)
    {
        var back   = CreatePanel(name + "Bg", parent, bg);
        var fillRt = CreateRect(name + "Fill", back.rectTransform);
        fillImage             = fillRt.gameObject.AddComponent<Image>();
        fillImage.color       = fill;
        fillImage.type        = Image.Type.Filled;
        fillImage.fillMethod  = Image.FillMethod.Horizontal;
        fillImage.fillOrigin  = (int)Image.OriginHorizontal.Left;
        Stretch(fillRt);
        return back;
    }

    /// <summary>Real, draggable uGUI Slider (0..1 by default) — background + fill + handle, wired
    /// via Unity's own Slider component (it manages the fill's anchorMax.x itself).</summary>
    public static Slider CreateSlider(string name, RectTransform parent, float value, Color fillColor)
    {
        var bg = CreatePanel(name + "Bg", parent, new Color(1f, 1f, 1f, 0.12f));

        var fillArea = CreateRect(name + "FillArea", bg.rectTransform);
        Stretch(fillArea);
        var fillRt = CreateRect(name + "Fill", fillArea);
        var fillImage = fillRt.gameObject.AddComponent<Image>();
        fillImage.color = fillColor;
        Stretch(fillRt);

        var handleArea = CreateRect(name + "HandleArea", bg.rectTransform);
        Stretch(handleArea);
        var handleRt = CreateRect(name + "Handle", handleArea);
        var handleImage = handleRt.gameObject.AddComponent<Image>();
        handleImage.color = Color.white;
        handleRt.sizeDelta = new Vector2(14f, 0f);

        var slider = bg.gameObject.AddComponent<Slider>();
        slider.fillRect      = fillRt;
        slider.handleRect    = handleRt;
        slider.targetGraphic = handleImage;
        slider.direction     = Slider.Direction.LeftToRight;
        slider.minValue      = 0f;
        slider.maxValue      = 1f;
        slider.value         = value;
        return slider;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Anchored, fixed-size box — the uGUI equivalent of the old IMGUI absolute Rect.</summary>
    public static void SetBox(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;
    }

    /// <summary>Stacks a top-anchored, horizontally-stretched box downward from the running
    /// cursor `y` (both panel-local pixels) — the uGUI equivalent of an IMGUI "y += rowHeight"
    /// layout cursor.</summary>
    public static void StackTop(RectTransform rt, ref float y, float height, float horizontalInset = 16f)
    {
        SetBox(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(-horizontalInset * 2f, height));
        y -= height;
    }
}
