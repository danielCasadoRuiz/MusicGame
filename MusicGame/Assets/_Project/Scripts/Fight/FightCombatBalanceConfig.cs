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

    [Header("Knockdown flow — Knockdown → Downed → GetUp → CombatIdle (fighter invulnerable throughout)")]
    [Min(0.05f)] public float knockdownDuration = 1.2f;
    [Min(0f)] public float downedDuration = 0.8f;
    [Min(0.05f)] public float getUpDuration = 1.6f;

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
