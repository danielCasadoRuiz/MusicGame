/// <summary>
/// What FighterInputController actually consumes — a fighter's intent for THIS frame, completely
/// device-agnostic. Horizontal/Vertical are RAW/ABSOLUTE (positive = screen-right/up; NOT facing-
/// relative — see FightDirectionResolver for that separate step), so an implementation never needs
/// to know which way its own fighter is facing.
///
/// HumanFightInputSource (keyboard + touch) is the only implementation today. The whole point of
/// this interface is that a future AIFightInputSource — driving a rival by simulated
/// reaction/aggression rather than a real joystick — produces the exact same shape of intent, and
/// FighterInputController/the combo recognizer never need to know which kind they're reading from.
/// </summary>
public interface IFightInputSource
{
    /// <summary>-1 (screen-left) .. +1 (screen-right).</summary>
    float Horizontal { get; }
    /// <summary>-1 (screen-down) .. +1 (screen-up).</summary>
    float Vertical { get; }
    /// <summary>True only on the frame Punch was pressed — valid until the next Tick().</summary>
    bool PunchPressed { get; }
    /// <summary>True only on the frame Kick was pressed — valid until the next Tick().</summary>
    bool KickPressed { get; }

    /// <summary>Call exactly once per frame, before reading the properties above — computes/caches
    /// this frame's values so multiple readers this frame (immediate-normal dispatch, buffer
    /// append) always agree, and an edge-triggered press is never silently "consumed twice" or
    /// missed by a second reader.</summary>
    void Tick();
}
