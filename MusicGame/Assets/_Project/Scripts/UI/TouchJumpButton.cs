using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// On-screen jump button — sets TouchInputState.JumpRequested on PRESS (not on a full click's
/// press-and-release), matching Keyboard.spaceKey.wasPressedThisFrame's edge-triggered feel.
/// PlayerController consumes and clears the flag the same frame it reads it.
/// </summary>
public class TouchJumpButton : MonoBehaviour, IPointerDownHandler
{
    public void OnPointerDown(PointerEventData eventData) => TouchInputState.JumpRequested = true;
}
