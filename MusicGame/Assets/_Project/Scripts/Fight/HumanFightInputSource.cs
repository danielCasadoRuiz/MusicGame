using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keyboard + touch — same dual-input convention as PlayerController's own input-reading methods
/// (readTouch/readKeyboard gated by PlatformService.IsMobile/DualInputInEditor): touch is read
/// whenever this is a real/simulated mobile platform OR we're in the Editor (where the keyboard is
/// ADDITIONALLY always available too, never platform-gated — see PlatformService.DualInputInEditor's
/// own doc for why). The touch UI itself (joystick/buttons) stays strictly platform-gated
/// separately, in FightController — this class only decides whether to LISTEN to it.
///
/// Keyboard bindings (chosen to never collide with Runner's own — not that the two ever run at the
/// same time, but for a developer's muscle memory testing both back to back):
///   Movement: WASD or Arrow Keys (both work, same as Runner's own strafe keys)
///   Punch:    J
///   Kick:     K
/// </summary>
public class HumanFightInputSource : IFightInputSource
{
    public float Horizontal { get; private set; }
    public float Vertical { get; private set; }
    public bool PunchPressed { get; private set; }
    public bool KickPressed { get; private set; }

    public void Tick()
    {
        bool readTouch    = PlatformService.IsMobile || PlatformService.DualInputInEditor;
        bool readKeyboard = !PlatformService.IsMobile || PlatformService.DualInputInEditor;

        float h = 0f, v = 0f;
        bool punch = false, kick = false;

        if (readTouch)
        {
            h = FightTouchInputState.Horizontal;
            v = FightTouchInputState.Vertical;
            if (FightTouchInputState.PunchRequested) { punch = true; FightTouchInputState.PunchRequested = false; }
            if (FightTouchInputState.KickRequested)  { kick  = true; FightTouchInputState.KickRequested  = false; }
        }

        if (readKeyboard)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                // Touch (if actively non-zero) takes priority for the analog axes — same
                // "whichever is actually being used wins" convention PlayerController's own
                // UpdateLateralOffset uses for keyboard vs joystick.
                if (Mathf.Approximately(h, 0f))
                {
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  h -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
                }
                if (Mathf.Approximately(v, 0f))
                {
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed)   v += 1f;
                }

                punch |= kb.jKey.wasPressedThisFrame;
                kick  |= kb.kKey.wasPressedThisFrame;
            }
        }

        Horizontal   = h;
        Vertical     = v;
        PunchPressed = punch;
        KickPressed  = kick;
    }
}
