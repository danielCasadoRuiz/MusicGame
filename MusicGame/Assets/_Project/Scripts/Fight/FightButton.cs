/// <summary>The Fighter's two attack inputs for V1, plus None — see FighterInputController's own
/// doc. None represents a buffered DIRECTION-ONLY tap (no button at all — e.g. one step of a
/// "Forward, Forward" dash) so FightComboDefinition.Step can express a bare-direction combo through
/// the EXACT SAME recognizer/buffer as every button-based one, with zero new matching logic.</summary>
public enum FightButton
{
    Punch,
    Kick,
    None,
}
