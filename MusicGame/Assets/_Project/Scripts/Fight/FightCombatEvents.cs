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
    /// <summary>Combo string this press belongs to (see FightComboRecognizer).</summary>
    public int StringId;
}

/// <summary>Fired the SAME frame Kick is pressed — see FightNormalPunchEvent's own doc.</summary>
public struct FightNormalKickEvent
{
    public FighterInputController Source;
    public int StringId;
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
    /// <summary>Combo string it belongs to — moves from the same string share it.</summary>
    public int StringId;
    /// <summary>Extends an already-completed combo of the same string (PPP → PPPK): the move system
    /// cancels that combo's move and starts this one instead of running both.</summary>
    public bool ReplacesPrevious;
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

/// <summary>A hit reached a defender that was invulnerable (Dodge window / knockdown flow) — nothing applied.</summary>
public struct HitEvadedEvent
{
    public FighterActor Attacker;
    public FighterActor Defender;
    public FightMoveDefinition Move;
}

/// <summary>Fired once by FighterInputController when the Down + Punch + Kick hold completes
/// (POWER CHORD) — FighterMoveController decides whether the Power State actually starts.</summary>
public struct FightPowerRequestedEvent
{
    public FighterInputController Source;
}

/// <summary>Power State started / refreshed / ended (FighterPowerState) — the hook for future UI/VFX.</summary>
public struct PowerStateChangedEvent
{
    public FighterActor Fighter;
    public bool Active;
    public float Duration;
}

/// <summary>A Signature Move actually STARTED — Enhanced = the Special version (1 Special consumed).</summary>
public struct SignatureExecutedEvent
{
    public FighterActor Fighter;
    public FightMoveDefinition Move;
    public bool Enhanced;
    public int SpecialsLeft;
}

/// <summary>A Forward-Forward charge reached the opponent fast enough to body-check them (see
/// FighterMovement.TryTackle). Published after the hit was resolved through FightHitDispatcher, so
/// HitLandedEvent/HitBlockedEvent/HitEvadedEvent for the same contact have already fired.</summary>
/// <summary>An airborne kick started while carrying real horizontal speed (a flying kick).</summary>
public struct FightFlyingKickEvent { public FighterActor Fighter; public float Speed; }

public enum FightGrappleKind { Grab, Takedown, Throw, Break, Whiff, GroundStrike, GroundEscape, GroundEnd }

/// <summary>Grab / takedown lifecycle (FighterGrapple) — the meaningful ones are toasted in playtests.</summary>
public struct FightGrappleEvent { public FighterActor Attacker; public FighterActor Defender; public FightGrappleKind Kind; public float Damage; }

/// <summary>Punch + Kick pressed together (within FightFlowConfig.grabChordWindow, no Down held) —
/// a logical GRAB request (or a grab/ground escape when the source is the one being held).</summary>
public struct FightGrabRequestedEvent { public FighterInputController Source; }

public struct FightTackleEvent
{
    public FighterActor Attacker;
    public FighterActor Defender;
    public float Speed;     // m/s at contact
    public float Momentum;  // 0..1 between tackleMinSpeed and full charge speed
    public FightHitResult Result;
}

/// <summary>A move started by spending a run resource (x3 TripleCombo, x4 QuadCombo, SPECIAL) — see
/// FighterMoveController.BeginMove. (Power State's x4 is announced by PowerStateChangedEvent.)</summary>
public struct FightResourceSpentEvent
{
    public FighterActor Fighter;
    public FightMoveDefinition Move;
    public CombatResourceType Type;
    public int Amount;
}
