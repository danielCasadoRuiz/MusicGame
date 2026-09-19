using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Generic on-screen action button — fires an arbitrary callback on press (edge-triggered, same
/// "PRESS not click" feel as TouchJumpButton), parameterized instead of hardwired to one static
/// field. TouchJumpButton stays as-is (Runner's own, already shipped, not worth touching); this is
/// what Fight's Punch/Kick buttons use, and what any FUTURE single-action touch button should reach
/// for instead of cloning another hardwired one-off.
/// </summary>
public class TouchActionButton : MonoBehaviour, IPointerDownHandler
{
    private System.Action _onPress;

    public void Initialize(System.Action onPress) => _onPress = onPress;

    public void OnPointerDown(PointerEventData eventData) => _onPress?.Invoke();
}
