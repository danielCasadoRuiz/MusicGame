using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A button drawn as two stacked copies of the Tempo Fury button art in the same place: the classic button
/// underneath and the song style's button on top, whose alpha is the style weight (StyleBlendGroup.Apply).
/// Hover / keyboard selection swaps both layers to their 'selected' art and recolours the label.
/// The layers cover the button rect with the art's glow margin outside it (the body rect = the button rect),
/// and never catch raycasts: the Button's own (transparent) Image stays the click target.
/// Added to the pause buttons by TempoFuryUISetup.
/// </summary>
[ExecuteAlways]
public class StyleBlendButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public StyleSkinLibrarySO library;
    public Image classicLayer;
    public Image styleLayer;
    public TextMeshProUGUI label;

    private GameMusicStyle _style = GameMusicStyle.Unknown;
    private float _weight;
    private bool _hovered, _selected;

    public void SetBlend(GameMusicStyle style, float styleWeight)
    {
        _style = style;
        _weight = Mathf.Clamp01(styleWeight);
        Refresh();
    }

    private void OnEnable()
    {
        _hovered = false;
        _selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        FitLayers();
        Refresh();
    }

    private void OnRectTransformDimensionsChange() => FitLayers();

    public void OnPointerEnter(PointerEventData e) { _hovered = true; Refresh(); }
    public void OnPointerExit(PointerEventData e)  { _hovered = false; Refresh(); }
    public void OnSelect(BaseEventData e)          { _selected = true; Refresh(); }
    public void OnDeselect(BaseEventData e)        { _selected = false; Refresh(); }

    private void Refresh()
    {
        if (library == null) return;
        bool on = _hovered || _selected;
        if (classicLayer != null) classicLayer.sprite = on ? library.classicSelected : library.classicNormal;
        if (styleLayer != null)
        {
            var skin = library.Button(_style);
            styleLayer.sprite = skin != null ? (on ? skin.selected : skin.normal) : null;
            styleLayer.enabled = skin != null && _weight > 0.001f;
            var c = styleLayer.color; c.a = _weight; styleLayer.color = c;
        }
        if (label != null) label.color = on ? library.labelSelected : library.labelNormal;
    }

    // The art's body (1152×192 of 1280×320) matches the button rect; the glow margin hangs outside it.
    private void FitLayers()
    {
        var rect = ((RectTransform)transform).rect;
        var canvas = StyleSkinLibrarySO.ButtonCanvas;
        var body = StyleSkinLibrarySO.ButtonBody;
        float sx = rect.width / body.width, sy = rect.height / body.height;
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
