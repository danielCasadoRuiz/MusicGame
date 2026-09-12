using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Minimal uGUI construction helpers. Everything is built at RUNTIME, in code — no prefabs, no
/// manual scene wiring — matching how every other system in this project bootstraps itself
/// (GameplayManager.Awake() AddComponent-chaining MusicClock/FallRespawnSystem/etc.).
///
/// Replaces the old OnGUI-drawn menus/HUD: real Canvas + Button/Image/Text, driven by
/// UnityEngine.EventSystems + the new Input System's InputSystemUIInputModule (this project's
/// Active Input Handling is New-Input-System-only — see ProjectSettings — so the legacy
/// StandaloneInputModule would never receive clicks at all).
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

            EnsureEventSystem();
        }
        return _canvasInstance.GetComponent<RectTransform>();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("[EventSystem]", typeof(EventSystem), typeof(InputSystemUIInputModule));
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

    public static Text CreateText(string name, RectTransform parent, string content, int fontSize,
        Color color, TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
    {
        var rt  = CreateRect(name, parent);
        var txt = rt.gameObject.AddComponent<Text>();
        txt.font                = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.text                = content;
        txt.fontSize             = fontSize;
        txt.color                = color;
        txt.alignment            = anchor;
        txt.fontStyle            = style;
        txt.horizontalOverflow   = HorizontalWrapMode.Overflow;
        txt.verticalOverflow     = VerticalWrapMode.Overflow;
        return txt;
    }

    /// <summary>Real Button component — onClick fires through EventSystem/InputSystemUIInputModule,
    /// not IMGUI's control-ID/hotControl bookkeeping (the source of the old double-click bug: a
    /// GUI.Button whose branch stops being drawn between MouseDown and MouseUp leaves
    /// GUIUtility.hotControl stuck, silently eating the next click).</summary>
    public static Button CreateButton(string name, RectTransform parent, string label, out Text labelText)
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

        labelText = CreateText(name + "Label", rt, label, 15, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
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
