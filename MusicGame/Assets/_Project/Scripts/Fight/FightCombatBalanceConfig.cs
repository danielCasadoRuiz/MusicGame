using UnityEngine;

/// <summary>
/// The single place every combat-build stat's effect is defined — no formulas scattered across
/// FighterAttack/FightHitResolver/FighterMoveController. Each curve's X axis is the RAW build stat in
/// budget points (FighterBuildStats — the Runner's own scale: a 100-point budget split over five stats,
/// ~16 each for a typical 80% run); Y is a multiplier where 1 = neutral. PLACEHOLDER balance values.
///
///   attacker <move.scalingStat> (PunchPower / KickPower / ...) → damage dealt
///   defender Resistance                                          → damage taken + hit-stun resistance
///   attacker ImpactPower                                         → knockback + hit stun dealt
///   own Agility                                                  → Startup/Recovery timing scale
///
/// Legacy FighterStats (Strength/Defense/...) are no longer read by combat.
/// </summary>
[CreateAssetMenu(fileName = "FightCombatBalanceConfig", menuName = "MusicGame/Fight/Combat Balance Config")]
public class FightCombatBalanceConfig : ScriptableObject
{
    [Header("Health")]
    public float defaultMaxHealth = 100f;

    [Header("Build stats → combat multipliers (X = stat points, Y = multiplier)")]
    [Tooltip("Attacker's move.scalingStat → damage DEALT.")]
    public AnimationCurve buildStatToDamage = new AnimationCurve(
        new Keyframe(0f, 0.85f), new Keyframe(16f, 1.00f), new Keyframe(40f, 1.25f));
    [Tooltip("Defender Resistance → damage TAKEN (lower = tankier).")]
    public AnimationCurve resistanceToDamageTaken = new AnimationCurve(
        new Keyframe(0f, 1.10f), new Keyframe(16f, 1.00f), new Keyframe(40f, 0.85f));
    [Tooltip("Defender Resistance → incoming hit stun AND knockback (lower = shrugs hits off).")]
    public AnimationCurve resistanceToStagger = new AnimationCurve(
        new Keyframe(0f, 1.10f), new Keyframe(16f, 1.00f), new Keyframe(40f, 0.85f));
    [Tooltip("Attacker ImpactPower → knockback and hit stun DEALT.")]
    public AnimationCurve impactToStagger = new AnimationCurve(
        new Keyframe(0f, 0.85f), new Keyframe(16f, 1.00f), new Keyframe(40f, 1.30f));
    [Tooltip("Own Agility → Startup/Recovery duration scale (lower = faster). Active never scales.")]
    public AnimationCurve agilityToTimingScale = new AnimationCurve(
        new Keyframe(0f, 1.08f), new Keyframe(16f, 1.00f), new Keyframe(40f, 0.88f));
    public float minTimingScale = 0.7f;
    public float maxTimingScale = 1.15f;

    [Tooltip("Build stats used for a fighter with no real source (no Runner run / no authored opponent build).")]
    public float neutralBuildBudget = 80f;

    [Header("Block — see FighterGuard/FightHitResolver's own doc")]
    [Tooltip("Applied on top of the defender's own resistance when a hit is blocked.")]
    public float blockStunMultiplier = 0.5f;
    [Tooltip("Applied on top of the attacker's impact / defender's resistance when a hit is blocked.")]
    public float blockKnockbackMultiplier = 0.5f;
    [Tooltip("BLOCK/CHIP DAMAGE: a correctly blocked hit still deals this fraction of its would-be damage " +
             "(never 0 — holding guard forever must not be a strategy). A move's own chipDamage is a floor.")]
    [Range(0f, 1f)] public float blockDamageMultiplier = 0.2f;

    [Header("Grab / takedown (FighterGrapple) — Punch + Kick together")]
    [Tooltip("Grab reach beyond FightArenaConfig.minimumFighterSeparation (short).")]
    public float grabRangePadding = 0.45f;
    [Tooltip("Seconds the defender has to break a standing grab with Punch + Kick.")]
    public float grabEscapeWindow = 0.45f;
    [Tooltip("Seconds the defender has to break a RUNNING takedown (shorter: it's a committed rush).")]
    public float takedownEscapeWindow = 0.3f;
    [Tooltip("Attacker forward speed (m/s) at or above which Punch + Kick becomes a running takedown.")]
    public float runningGrabMinSpeed = 5f;
    public float throwDamage = 12f;
    public float throwKnockback = 1.4f;
    public float takedownDamage = 9f;
    [Tooltip("Recovery lock after a WHIFFED grab (nobody in range) — grabs are not free.")]
    public float grabWhiffRecovery = 0.45f;
    [Tooltip("Lock for both fighters after a successful break, plus the push apart.")]
    public float throwBreakLock = 0.25f;
    public float throwBreakPush = 0.9f;

    [Header("Ground control (after a successful running takedown)")]
    [Tooltip("Hard maximum of the ground-control window (s). It ALWAYS ends here.")]
    public float groundControlDuration = 1.5f;
    [Tooltip("Max ground strikes (Punch) per takedown.")]
    public int groundStrikeMax = 3;
    public float groundStrikeDamage = 4f;
    [Tooltip("Min seconds between ground strikes.")]
    public float groundStrikeInterval = 0.3f;
    [Tooltip("The defender can escape early with Punch + Kick after this many seconds on the ground.")]
    public float groundEscapeMinTime = 0.6f;

    [Header("Knockdown flow — Knockdown → Downed → GetUp → CombatIdle (fighter invulnerable throughout)")]
    [Min(0.05f)] public float knockdownDuration = 1.2f;
    [Min(0f)] public float downedDuration = 0.8f;
    [Min(0.05f)] public float getUpDuration = 1.6f;

    [Header("Finishers (combo definitions with attackBonus = Punch/KickFinisher)")]
    [Tooltip("Multipliers on the Punch Finisher (combo P P P). Its timing lives on the combo definition.")]
    public FightAttackBonus punchFinisher = new(1.5f, 1.6f, 1.8f);
    [Tooltip("Multipliers on the Kick Finisher (combo K K K).")]
    public FightAttackBonus kickFinisher = new(1.5f, 1.6f, 2.0f);

    [Header("Signature Move (FightMoveSetSO.signatureBasic / signatureSpecial)")]
    [Tooltip("Multipliers on the BASIC Signature (no Special available) — keep below the Special version.")]
    public FightAttackBonus signatureBasic = new(0.75f, 0.8f, 0.8f);
    [Tooltip("Multipliers on the ENHANCED Signature (the projectile — costs 1 Special).")]
    public FightAttackBonus signatureSpecial = new(1.8f, 1.6f, 1.8f);

    [Header("Power State (hold Down + Punch + Kick)")]
    [Tooltip("Seconds Down + Punch + Kick must be held together to activate the Power State.")]
    [Min(0.1f)] public float powerActivationHoldTime = 1.0f;
    [Tooltip("Seconds the Power State lasts once activated.")]
    [Min(0.1f)] public float powerDuration = 6f;
    [Min(0f)] public float powerDamageMultiplier = 1.3f;
    [Min(0f)] public float powerStaggerMultiplier = 1.25f;
    [Min(0f)] public float powerKnockbackMultiplier = 1.25f;

    public FightAttackBonus PowerMultipliers => new(powerDamageMultiplier, powerStaggerMultiplier, powerKnockbackMultiplier);

    /// <summary>Evaluates every curve against one fighter's build — the SAME computation FightHitResolver
    /// (real combat), FighterMoveController (timing) and FightDebugHUD use, so they can never drift.</summary>
    public FightCombatModifiers ComputeModifiers(FighterBuildStats stats, FighterBuildStat scalingStat = FighterBuildStat.None)
    {
        return new FightCombatModifiers
        {
            DamageDealtMultiplier    = scalingStat == FighterBuildStat.None ? 1f : buildStatToDamage.Evaluate(stats.Get(scalingStat)),
            DamageTakenMultiplier    = resistanceToDamageTaken.Evaluate(stats.resistance),
            KnockbackDealtMultiplier = impactToStagger.Evaluate(stats.impactPower),
            ResistanceMultiplier     = resistanceToStagger.Evaluate(stats.resistance),
            TimingScale              = Mathf.Clamp(agilityToTimingScale.Evaluate(stats.agility), minTimingScale, maxTimingScale),
        };
    }
}

/// <summary>One fighter's fully-evaluated combat modifiers — see FightCombatBalanceConfig.ComputeModifiers.</summary>
public struct FightCombatModifiers
{
    public float DamageDealtMultiplier;    // attacker, from the move's scaling stat
    public float DamageTakenMultiplier;    // defender Resistance
    public float KnockbackDealtMultiplier; // attacker ImpactPower (knockback + hit stun)
    public float ResistanceMultiplier;     // defender Resistance (hit stun + knockback)
    public float TimingScale;              // own Agility

    public static FightCombatModifiers Identity => new FightCombatModifiers
    {
        DamageDealtMultiplier = 1f, DamageTakenMultiplier = 1f, KnockbackDealtMultiplier = 1f,
        ResistanceMultiplier = 1f, TimingScale = 1f,
    };
}
