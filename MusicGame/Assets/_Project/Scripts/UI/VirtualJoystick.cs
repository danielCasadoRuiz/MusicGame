using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// On-screen drag joystick — works identically for a mouse (Editor testing,
/// PlatformService.editorPlatformSimulation = Mobile) or a real touch, since Unity's EventSystem/
/// GraphicRaycaster already treat both as the same pointer events; no separate "simulate touch"
/// system needed.
///
/// By default writes TouchInputState.Lateral/Surging directly — Runner's own MobileControlsController
/// still calls Initialize(background, handle) with no extra arguments and behaves EXACTLY as
/// before. A caller with different output needs (Fight's own movement joystick, writing
/// FightTouchInputState instead) passes an onValueChanged callback and driveTouchInputState:false —
/// same drag-math component, reused rather than cloned, per-instance wired to wherever its output
/// actually belongs. Never hardcode a second copy of this class for a new consumer.
///
/// No art final: `background`/`handle` are plain UIFactory panels sized/positioned by the caller —
/// this component only owns the drag math.
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private RectTransform _background;
    private RectTransform _handle;
    private float _radius;
    private System.Action<Vector2> _onValueChanged;
    private bool _driveTouchInputState;

    // Fraction of the joystick's vertical range that must be pushed forward before it counts as
    // "surging" (Keyboard's W/Up equivalent) — only meaningful while driveTouchInputState is true.
    private const float SurgeThreshold = 0.4f;

    /// <param name="onValueChanged">Optional — receives this joystick's normalized (-1..1, -1..1)
    /// value on every change, INCLUDING (0,0) on release/disable. Null (the default) if a caller
    /// only cares about TouchInputState, same as before this parameter existed.</param>
    /// <param name="driveTouchInputState">Whether to keep writing Runner's own
    /// TouchInputState.Lateral/Surging (default true, i.e. unchanged behavior). A second,
    /// Fight-owned instance of this component should pass false so it never touches Runner's
    /// static state it has nothing to do with.</param>
    public void Initialize(RectTransform background, RectTransform handle,
        System.Action<Vector2> onValueChanged = null, bool driveTouchInputState = true)
    {
        _background = background;
        _handle     = handle;
        _radius     = background.sizeDelta.x * 0.5f;
        _onValueChanged = onValueChanged;
        _driveTouchInputState = driveTouchInputState;
    }

    public void OnPointerDown(PointerEventData eventData) => OnDrag(eventData);

    public void OnDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _background, eventData.position, eventData.pressEventCamera, out var localPoint);

        Vector2 clamped = Vector2.ClampMagnitude(localPoint, _radius);
        _handle.anchoredPosition = clamped;

        Vector2 normalized = _radius > 0f ? clamped / _radius : Vector2.zero;
        SetValue(normalized);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _handle.anchoredPosition = Vector2.zero;
        SetValue(Vector2.zero);
    }

    private void OnDisable()
    {
        // Losing focus/being hidden mid-drag (e.g. a pause menu opening) must not leave whoever
        // reads this stuck mid-strafe/mid-move or permanently surging.
        SetValue(Vector2.zero);
    }

    private void SetValue(Vector2 normalized)
    {
        if (_driveTouchInputState)
        {
            TouchInputState.Lateral = normalized.x;
            TouchInputState.Surging = normalized.y > SurgeThreshold;
        }
        _onValueChanged?.Invoke(normalized);
    }
}
