/// <summary>
/// Fired the SAME frame Punch is pressed — never delayed to wait and see if a combo forms (see
/// FighterInputController's own doc on why normals must be immediate). The future Move System is
/// the natural consumer; for now, FightDebugHUD just logs it.
/// </summary>
public struct FightNormalPunchEvent { }

/// <summary>Fired the SAME frame Kick is pressed — see FightNormalPunchEvent's own doc.</summary>
public struct FightNormalKickEvent { }

/// <summary>
/// Fired by FightComboRecognizer once a combo is confirmed — either immediately (it can't extend
/// into anything longer) or after a short grace window (it's a strict prefix of a longer combo
/// that never actually completed in time — see FightComboRecognizer's own doc). Carries the full
/// FightComboDefinition (id/debugName/moveId all on it) rather than just an id, so a listener never
/// needs a separate lookup back into the FightComboSetSO.
/// </summary>
public struct FightComboDetectedEvent
{
    public FightComboDefinition Combo;
}
