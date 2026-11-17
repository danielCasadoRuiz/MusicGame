using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A single GPU frame covering the screen while the outgoing 3D scene is released.</summary>
public sealed class SceneTransitionFrame : MonoBehaviour
{
    private Canvas _canvas;
    private RawImage _image;
    private RenderTexture _frame;
    private int _generation;
    private CanvasGroup _group;
    private Coroutine _reveal;
    private Image _background;

    public bool IsVisible => _canvas != null && _canvas.enabled;

    public IEnumerator Capture()
    {
        StopReveal();
        int generation = ++_generation;
        // The outgoing world still exists here. Capture after its cameras and UI have rendered,
        // before SceneFlow starts disposing any arena, avatar, camera or scene resources.
        if (Application.isBatchMode) yield return null;
        else yield return new WaitForEndOfFrame();
        if (this == null || generation != _generation) yield break;
        EnsureCanvas();
        DisposeFrame();
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            // Preserve the rendered camera stack at screen resolution. Capture directly into the
            // retained colour texture: no downsampling, depth buffer, CPU copy or second GPU texture.
            // ARGB32 costs 4 bytes per pixel (~8 MiB at 1080p, ~32 MiB at 4K), released on handoff.
            int screenWidth = Mathf.Max(1, Screen.width), screenHeight = Mathf.Max(1, Screen.height);
            _frame = new RenderTexture(screenWidth, screenHeight, 0, RenderTextureFormat.ARGB32)
            { name = "Last scene frame", hideFlags = HideFlags.DontSave, useMipMap = false, autoGenerateMips = false,
                antiAliasing = 1, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            _frame.Create();
            var previousActive = RenderTexture.active;
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_frame);
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }
        _image.texture = _frame;
        _background.enabled = true;
        // Correct the backbuffer origin in UVs without copying or resampling the captured pixels.
        _image.uvRect = SystemInfo.graphicsUVStartsAtTop ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
        _image.color = _frame != null ? Color.white : new Color(.04f, .04f, .06f, 1);
        _group.alpha = 1;
        _canvas.gameObject.SetActive(true);
        _canvas.enabled = true;
        Canvas.ForceUpdateCanvases();
    }

    private void EnsureCanvas()
    {
        if (_canvas != null) return;
        var root = new GameObject("Scene transition frame", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = short.MaxValue;
        _canvas.enabled = false;
        _group = root.GetComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = true;
        _background = root.GetComponent<Image>();
        _background.color = new Color(.04f, .04f, .06f, 1); // opaque even if the camera supplied a transparent alpha channel
        var image = new GameObject("Frozen frame", typeof(RectTransform), typeof(RawImage));
        image.transform.SetParent(root.transform, false);
        _image = image.GetComponent<RawImage>();
        _image.raycastTarget = true;
        var rect = (RectTransform)image.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public void Release()
    {
        StopReveal();
        ++_generation; // also cancels a capture waiting for the end of a frame
        if (_canvas != null) { _canvas.enabled = false; _canvas.gameObject.SetActive(false); }
        if (_image != null) _image.texture = null;
        DisposeFrame();
    }

    /// <summary>Keep covering while the destination UI lays out and renders, then blend straight into it.</summary>
    public void RevealMenu()
    {
        if (!IsVisible) return;
        StopReveal();
        _reveal = StartCoroutine(RevealAfterRender(_generation));
    }

    private IEnumerator RevealAfterRender(int generation)
    {
        // SetActive + ForceUpdateCanvases does not submit a rendered menu frame. Give newly enabled
        // layout/TMP/style components a full update, then wait for the destination frame to render
        // behind this cover. Fade the WHOLE cover, including its opaque fallback background.
        yield return null;
        if (Application.isBatchMode) yield return null;
        else yield return new WaitForEndOfFrame();
        // The rendered menu now provides the backdrop. Do not fade an extra black Image over it:
        // multiplying alpha on two overlapping graphics would darken the middle of the crossfade.
        if (_frame != null) _background.enabled = false;
        const float duration = .18f;
        for (float elapsed = 0; elapsed < duration; elapsed += Mathf.Min(Time.unscaledDeltaTime, .05f))
        {
            if (generation != _generation) yield break;
            _group.alpha = 1 - Mathf.SmoothStep(0, 1, elapsed / duration);
            yield return null;
        }
        _reveal = null;
        if (generation == _generation) Release();
    }

    private void StopReveal()
    {
        if (_reveal == null) return;
        StopCoroutine(_reveal);
        _reveal = null;
    }

    private void DisposeFrame()
    {
        if (_frame == null) return;
        _frame.Release();
        Destroy(_frame);
        _frame = null;
    }

    private void OnDestroy() => Release();
}
