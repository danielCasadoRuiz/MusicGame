using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A button drawn as two stacked copies of its art in the same place: the classic art underneath and the song
/// style's art on top, whose alpha is the style weight (StyleBlendGroup.Apply).
/// Hover / keyboard selection / press swaps both layers to their 'selected' art and recolours the label.
///   MenuButton  Tempo Fury button: the layers cover the button rect with the art's glow margin outside it
///               (the body rect = the button rect).
///   HudPause    the Runner bar's pause glyph (StyleSkinLibrarySO.hud); the layers keep their prefab rects.
/// The layers never catch raycasts: the Button's own (transparent) Image stays the click target.
/// Added by TempoFuryUISetup.
/// </summary>
[ExecuteAlways]
public class StyleBlendButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler,
    IPointerDownHandler, IPointerUpHandler
{
    public enum Art { MenuButton, HudPause }

    public StyleSkinLibrarySO library;
    public bool stretchMiddle;
    public bool accentFace;
    private Material _accentMaterial;
    public Art art = Art.MenuButton;
    public Image classicLayer;
    public Image styleLayer;
    public TextMeshProUGUI label;

    private GameMusicStyle _style = GameMusicStyle.Unknown;
    private GameMusicStyle _base = GameMusicStyle.Unknown;   // what the bottom layer shows: Unknown = the classic art
    private float _weight;
    private bool _curtain;
    private Material _transition;
    private bool _hovered, _selected, _pressed;

    /// <param name="baseStyle">Bottom layer: Unknown = the classic art (default); a style = cross-fade from that
    /// style's button to `style` (the main menu coming back from a game).</param>
    public void SetBlend(GameMusicStyle style, float styleWeight, GameMusicStyle baseStyle = GameMusicStyle.Unknown, bool useCurtain = false)
    {
        _style = style;
        _base = baseStyle;
        _weight = Mathf.Clamp01(styleWeight);
        _curtain = useCurtain;
        Refresh();
    }

    private void OnEnable()
    {
        _hovered = _pressed = false;
        _selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        FitLayers();
        Refresh();
    }

    private void OnRectTransformDimensionsChange() => FitLayers();

    public void OnPointerEnter(PointerEventData e) { _hovered = true; Refresh(); }
    public void OnPointerExit(PointerEventData e)  { _hovered = false; _pressed = false; Refresh(); }
    public void OnSelect(BaseEventData e)          { _selected = true; Refresh(); }
    public void OnDeselect(BaseEventData e)        { _selected = false; Refresh(); }
    public void OnPointerDown(PointerEventData e)  { _pressed = true; Refresh(); }
    public void OnPointerUp(PointerEventData e)    { _pressed = false; Refresh(); }

    private void Refresh()
    {
        if (library == null) return;
        // The HUD pause has no hover look of its own: only pressed / keyboard-selected light it up.
        bool on = art == Art.HudPause ? _pressed || _selected : _hovered || _selected || _pressed;
        Sprite classic, styled;
        if (art == Art.HudPause)
        {
            var c = library.ClassicHud;
            var s = _style == GameMusicStyle.Classical ? null : library.Hud(_style);
            classic = c != null ? (on ? c.pauseSelected : c.pause) : null;
            styled = s != null ? (on ? s.pauseSelected : s.pause) : null;
        }
        else
        {
            var s = library.Button(_style);
            var b = _base != GameMusicStyle.Unknown ? library.Button(_base) : null;
            classic = b != null ? (on ? b.selected : b.normal) : on ? library.classicSelected : library.classicNormal;
            styled = s != null ? (on ? s.selected : s.normal) : null;
        }
        if (classicLayer != null)
        {
            classicLayer.sprite = classic;
            classicLayer.enabled = styled == null || _weight < .9999f;
            classicLayer.material = art == Art.HudPause ? library.classicPauseMaterial : null;
            var classicColor=classicLayer.color;
            classicColor.a=art==Art.HudPause && styled!=null ? 1-_weight : 1;
            classicLayer.color=classicColor;
        }
        if (styleLayer != null)
        {
            styleLayer.sprite = styled;
            styleLayer.enabled = styled != null && _weight > 0.001f;
            var col = styleLayer.color; col.a = _weight; styleLayer.color = col;
            styleLayer.material = null;
            if (_curtain && classic != null && styled != null)
            {
                if(_transition==null)_transition=UIStyleTransition.Create(library,"Button style curtain");
                if(_transition!=null)
                {
                    styleLayer.enabled=true;
                    // Every pause button has the same left/right frontier, including its glow margin.
                    UIStyleTransition.Apply(_transition,styleLayer,styleLayer.rectTransform,library,1-_weight,classic,Color.white,Color.white);
                    if(classicLayer!=null)classicLayer.enabled=false;
                }
            }
        }
        if (label != null) label.color = on ? library.labelSelected : library.labelNormal;
        if(accentFace&&styleLayer!=null&&library.customSongAccentMaterial!=null)
        {
            if(_accentMaterial==null)_accentMaterial=new Material(library.customSongAccentMaterial){name="Custom song CTA accent",hideFlags=HideFlags.DontSave};
            Color accent=library.SongHeadingColor(_style,false);Color.RGBToHSV(accent,out _,out var saturation,out _);
            if(saturation<.35f)SongStyleSurface.Palette(library,_style,out _,out accent);
            _accentMaterial.SetColor("_Accent",accent);styleLayer.material=_accentMaterial;
            var linear=accent.linear;float luminance=.2126f*linear.r+.7152f*linear.g+.0722f*linear.b;
            if(label!=null)label.color=luminance>.179f?library.labelNormal:Color.white;
        }
    }

    private void OnDestroy(){UIStyleTransition.Release(_transition);if(_accentMaterial!=null){if(Application.isPlaying)Destroy(_accentMaterial);else DestroyImmediate(_accentMaterial);}}

    // The art's body (1152×192 of 1280×320) matches the button rect; the glow margin hangs outside it.
    public void FitLayers()
    {
        if (art != Art.MenuButton) return;
        var rect = ((RectTransform)transform).rect;
        var canvas = StyleSkinLibrarySO.ButtonCanvas;
        var body = StyleSkinLibrarySO.ButtonBody;
        float sx = rect.width / body.width, sy = rect.height / body.height;
        if(stretchMiddle)sx=sy;
        var min = new Vector2(-body.x * sx, -(canvas.y - body.yMax) * sy);
        var max = new Vector2((canvas.x - body.xMax) * sx, body.y * sy);
        foreach (var layer in new[] { classicLayer, styleLayer })
        {
            if (layer == null) continue;
            var rt = layer.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = min; rt.offsetMax = max;
        }
    }
}
