using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// On-screen drag joystick feeding TouchInputState.Lateral/Surging — works identically for a mouse
/// (Editor testing, PlatformService.editorPlatformSimulation = Mobile) or a real touch, since
/// Unity's EventSystem/GraphicRaycaster already treat both as the same pointer events; no separate
/// "simulate touch" system needed.
///
/// No art final: `background`/`handle` are plain UIFactory panels sized/positioned by
/// MobileControlsController — this component only owns the drag math.
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private RectTransform _background;
    private RectTransform _handle;
    private float _radius;

    // Fraction of the joystick's vertical range that must be pushed forward before it counts as
    // "surging" (Keyboard's W/Up equivalent).
    private const float SurgeThreshold = 0.4f;

    public void Initialize(RectTransform background, RectTransform handle)
    {
        _background = background;
        _handle     = handle;
        _radius     = background.sizeDelta.x * 0.5f;
    }

    public void OnPointerDown(PointerEventData eventData) => OnDrag(eventData);

    public void OnDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _background, eventData.position, eventData.pressEventCamera, out var localPoint);

        Vector2 clamped = Vector2.ClampMagnitude(localPoint, _radius);
        _handle.anchoredPosition = clamped;

        Vector2 normalized = _radius > 0f ? clamped / _radius : Vector2.zero;
        TouchInputState.Lateral = normalized.x;
        TouchInputState.Surging = normalized.y > SurgeThreshold;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _handle.anchoredPosition = Vector2.zero;
        TouchInputState.Lateral = 0f;
        TouchInputState.Surging = false;
    }

    private void OnDisable()
    {
        // Losing focus/being hidden mid-drag (e.g. a pause menu opening) must not leave the player
        // stuck mid-strafe or permanently surging.
        TouchInputState.Lateral = 0f;
        TouchInputState.Surging = false;
    }
}
