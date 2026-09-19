/// <summary>
/// Fired the SAME frame Punch is pressed — never delayed to wait and see if a combo forms (see
/// FighterInputController's own doc on why normals must be immediate). Source identifies WHICH
/// FighterInputController published this (Player's or Opponent's own — both exist as independent
/// instances now that the Opponent plays for real, see FighterAI's own doc) — every listener
/// (FighterMoveController, FighterMovement) filters on `e.Source == myOwnInputController` so a
/// Player press never drives the Opponent's Fighter and vice versa.
/// </summary>
public struct FightNormalPunchEvent
{
    public FighterInputController Source;
}

/// <summary>Fired the SAME frame Kick is pressed — see FightNormalPunchEvent's own doc.</summary>
public struct FightNormalKickEvent
{
    public FighterInputController Source;
}

/// <summary>
/// Fired by FightComboRecognizer once a combo is confirmed — either immediately (it can't extend
/// into anything longer) or after a short grace window (it's a strict prefix of a longer combo
/// that never actually completed in time — see FightComboRecognizer's own doc). Carries the full
/// FightComboDefinition (id/debugName/moveId all on it) rather than just an id, so a listener never
/// needs a separate lookup back into the FightComboSetSO. Source — see FightNormalPunchEvent's own
/// doc on why this exists and who must filter on it.
/// </summary>
public struct FightComboDetectedEvent
{
    public FighterInputController Source;
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

/// <summary>Fired by FightHitDispatcher instead of HitLandedEvent whenever the defender's guard
/// actually stopped a hit — see FighterGuard.WouldBlock's own doc. Same shape/fields as
/// HitLandedEvent (Result.IsBlocked is true here) so a listener can tell the two apart cleanly
/// without inspecting Result first.</summary>
public struct HitBlockedEvent
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
