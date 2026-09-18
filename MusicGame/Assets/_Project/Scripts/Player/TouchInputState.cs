/// <summary>
/// Plain, static, cross-scene bridge between the mobile virtual controls (MobileControlsController/
/// VirtualJoystick/TouchJumpButton, built in the always-loaded UI Scene) and PlayerController (in
/// the Runner Mode Scene) — pure shared data, no lifecycle of its own, the same reasoning
/// GameSession/ThemeManager already rely on for reaching across scenes without AppContext plumbing
/// for something this small.
///
/// JumpRequested is edge-triggered like Keyboard.spaceKey.wasPressedThisFrame: the touch jump
/// button sets it true on press, and PlayerController must clear it back to false the same frame it
/// reads it, or it would look "held" forever.
/// </summary>
public static class TouchInputState
{
    /// <summary>-1..1, left/right — from the virtual joystick's horizontal axis.</summary>
    public static float Lateral;

    /// <summary>True while the virtual joystick is pushed forward past its surge threshold.</summary>
    public static bool Surging;

    /// <summary>Set true by the touch jump button on press; PlayerController reads and clears it.</summary>
    public static bool JumpRequested;
}
