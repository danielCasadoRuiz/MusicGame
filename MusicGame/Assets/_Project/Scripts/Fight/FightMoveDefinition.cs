using UnityEngine;

/// <summary>Purely a debug/categorization label today — nothing branches on this yet beyond
/// FightDebugHUD's own display.</summary>
public enum FightMoveType
{
    Normal,
    Combo,
}

/// <summary>
/// One executable move — entirely data-driven, same ScriptableObject-per-thing shape as
/// OpponentDefinition/FightComboSetSO. FighterMoveController is the ONLY thing that reads this;
/// nothing hardcodes Punch/Kick-specific behavior anywhere.
///
/// This phase cares about Startup -> Active -> Recovery timing and the seams below (animation,
/// movement, facing) — see each field's own doc for exactly what does and doesn't exist yet to
/// back it.
/// </summary>
[CreateAssetMenu(fileName = "FightMoveDefinition", menuName = "MusicGame/Fight/Move Definition")]
public class FightMoveDefinition : ScriptableObject
{
    [Tooltip("Matched against FightComboDefinition.moveId by FightMoveSetSO.GetByMoveId — keep " +
             "these in sync by hand for now (no combo/move linking UI exists yet).")]
    public string id;
    public string debugName;
    public FightMoveType moveType;

    [Header("Animation — see IFighterAnimationDriver's own doc (no real Animator exists yet)")]
    [Tooltip("A free-form debug/placeholder state name — DebugFighterAnimationDriver just tracks " +
             "whatever string is set here for FightDebugHUD to display. Once real clips exist, " +
             "this is the ONE value that needs to start meaning something real (e.g. an Animator " +
             "trigger/state name) — nothing else about this asset or FighterMoveController changes.")]
    public string animationState;

    [Header("Timing — Startup -> Active -> Recovery")]
    public float startupDuration = 0.1f;
    public float activeDuration = 0.1f;
    public float recoveryDuration = 0.2f;

    [Tooltip("How many seconds BEFORE this move's natural end (startupDuration + activeDuration + " +
             "recoveryDuration) another move is allowed to cancel into it early — see " +
             "FighterMoveController's own doc on the queue/cancel policy. 0 = never cancellable; a " +
             "new request during this move is queued instead and fires the instant this one " +
             "actually finishes.")]
    public float cancelWindow = 0f;

    [Header("Movement — see IFighterMovementDriver's own doc (no real locomotion exists yet)")]
    [Tooltip("If true, movementMultiplier is ignored — the fighter simply can't move under normal " +
             "input while this move is running (locomotion resumes the instant it ends).")]
    public bool movementLocked = true;
    [Tooltip("Ignored while movementLocked is true. 1 = normal speed, 0.5 = half speed, etc.")]
    public float movementMultiplier = 1f;
    [Tooltip("A one-off forward displacement applied once, at move start. 0 = no lunge.")]
    public float lungeDistance = 0f;

    [Header("Facing — see IFightFacingProvider's own doc (still the always-true debug placeholder)")]
    [Tooltip("While true, facing must not flip during this move's Startup/Active (Recovery is " +
             "always safe to flip in, same as most fighting games' own convention) — see " +
             "FighterMoveController.IsFacingLocked. Has no real effect yet since no real facing " +
             "recalculation exists to consult it, but the flag is real, authored data.")]
    public bool lockFacingDuringMove = true;

    [Header("Hitboxes — see FightHitDefinition/FighterAttack's own doc")]
    [Tooltip("All active for the ENTIRE Active phase (FighterMoveController remains the sole " +
             "authority on Startup/Active/Recovery timing — no per-hit sub-window exists yet). " +
             "Empty is tolerated (a move with no offensive hitbox at all).")]
    public FightHitDefinition[] hits = new FightHitDefinition[] { new FightHitDefinition() };

    [Header("Future — not read by anything this phase")]
    [Tooltip("Superseded by hits[].baseDamage — kept only so no existing reference to this field " +
             "breaks; not read anywhere.")]
    public int damage = 0;

    public float TotalDuration => startupDuration + activeDuration + recoveryDuration;
}
