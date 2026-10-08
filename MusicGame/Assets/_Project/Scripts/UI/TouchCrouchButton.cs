using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// On-screen CROUCH button (Runner action B, next to Jump = action A) — HOLD to stay crouched.
/// Only writes TouchInputState.CrouchHeld; PlayerController owns the gameplay state.
/// </summary>
public class TouchCrouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public void OnPointerDown(PointerEventData eventData) => TouchInputState.CrouchHeld = true;
    public void OnPointerUp(PointerEventData eventData)   => TouchInputState.CrouchHeld = false;
    public void OnPointerExit(PointerEventData eventData) => TouchInputState.CrouchHeld = false;
    private void OnDisable() => TouchInputState.CrouchHeld = false;
}
