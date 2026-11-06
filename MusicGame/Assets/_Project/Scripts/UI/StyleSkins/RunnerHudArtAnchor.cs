using UnityEngine;

/// <summary>Keeps the HUD and its separate pause button in the same 3072 x 320 art coordinates.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class RunnerHudArtAnchor : MonoBehaviour
{
    public bool entireBar;
    public Vector2 artCenter;
    public Vector2 artSize;
    private float _width = -1f;
    private bool _fitting;
    private void OnEnable() { Canvas.willRenderCanvases += Fit; _width = -1f; Fit(); }
    private void OnDisable() { Canvas.willRenderCanvases -= Fit; }
    private void OnRectTransformDimensionsChange() { Fit(); }
    public void Fit()
    {
        var rt = transform as RectTransform;
        var parent = transform.parent as RectTransform;
        if (_fitting || rt == null || parent == null) return;
        if (!entireBar && (artSize.x <= 0f || artSize.y <= 0f)) return;
        float width = parent.rect.width;
        if (width <= 0f) return;
        float scale = width / StyleSkinLibrarySO.HudBarCanvas.x;
        var expectedPosition = entireBar ? Vector2.zero : new Vector2(artCenter.x, -artCenter.y) * scale;
        var expectedSize = entireBar ? StyleSkinLibrarySO.HudBarCanvas : artSize * scale;
        float expectedScale = entireBar ? scale : 1f;
        if (Mathf.Abs(width - _width) < .01f && Mathf.Abs(rt.localScale.x - expectedScale) < .0001f
            && (rt.anchoredPosition - expectedPosition).sqrMagnitude < .0001f
            && (rt.sizeDelta - expectedSize).sqrMagnitude < .0001f
            && rt.anchorMin == Vector2.up && rt.anchorMax == Vector2.up) return;
        _width = width;
        _fitting = true;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        if (entireBar)
        {
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = StyleSkinLibrarySO.HudBarCanvas;
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one * scale;
        }
        else
        {
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(artCenter.x, -artCenter.y) * scale;
            rt.sizeDelta = artSize * scale;
            rt.localScale = Vector3.one;
        }
        _fitting = false;
    }
}
