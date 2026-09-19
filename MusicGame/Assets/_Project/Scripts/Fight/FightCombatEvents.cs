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

/// <summary>
/// Fired by FighterAttack the instant a hit is resolved (before Health/HitReaction react to it) —
/// carries the full FightHitResult so a listener (FightDebugHUD's "Last Hit" section, future VFX/
/// SFX/AI) never needs a separate lookup. A significant, discrete combat event — never a per-frame
/// tick (see this phase's own scope note on not overloading EventBus).
/// </summary>
public struct HitLandedEvent
{
    public FighterActor Attacker;
    public FighterActor Defender;
    public FightMoveDefinition Move;
    public FightHitResult Result;
}

/// <summary>Fired by FighterHealth.ApplyDamage on every non-zero hit — RemainingHealth is the value
/// AFTER this damage was applied.</summary>
public struct DamageTakenEvent
{
    public FighterActor Fighter;
    public float Amount;
    public float RemainingHealth;
}

/// <summary>Fired by FighterHealth exactly once, the instant CurrentHealth first reaches 0 — no
/// round/match resolution reacts to this yet (see this phase's own scope note); it exists purely as
/// the decoupled hook a future MatchWon/MatchLost system will subscribe to.</summary>
public struct FighterKOEvent
{
    public FighterActor Fighter;
}
