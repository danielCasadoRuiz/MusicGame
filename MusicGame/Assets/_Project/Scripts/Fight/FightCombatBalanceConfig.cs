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
///   own Agility                                                  → Startup/Recovery timing scale (attack speed)
///                                                                   + walk/run/side-walk speed (movement speed)
/// Rivals: rivalBuildBudgetByLevel sizes their build by difficulty level, independent of the player.
///
/// Legacy FighterStats (Strength/Defense/...) are no longer read by combat.
/// </summary>
[CreateAssetMenu(fileName = "FightCombatBalanceConfig", menuName = "MusicGame/Fight/Combat Balance Config")]
public class FightCombatBalanceConfig : ScriptableObject
{
    [Header("Health")]
    public float defaultMaxHealth = 100f;

    // 2026-10-25 rebalance: a LOW base and a big share from the runner. X = stat points (a run's 100-point budget split
    // over five stats: ~4 each for a poor run, ~10 for a middling one, ~16–18 for a very good one, 30+ when a run
    // specialises). 16 points = the old neutral 1.0. Curves clamp at their ends (no runaway values).
    [Header("Build stats → combat multipliers (X = stat points, Y = multiplier)")]
    [Tooltip("Attacker's move.scalingStat (PunchPower for punches, KickPower for kicks…) → damage DEALT.")]
    public AnimationCurve buildStatToDamage = new AnimationCurve(
        new Keyframe(0f, 0.25f), new Keyframe(5f, 0.45f), new Keyframe(10f, 0.70f), new Keyframe(16f, 1.00f),
        new Keyframe(25f, 1.40f), new Keyframe(40f, 1.90f));
    [Tooltip("Defender Resistance → damage TAKEN (lower = tankier).")]
    public AnimationCurve resistanceToDamageTaken = new AnimationCurve(
        new Keyframe(0f, 1.50f), new Keyframe(8f, 1.25f), new Keyframe(16f, 1.00f), new Keyframe(25f, 0.80f), new Keyframe(40f, 0.60f));
    [Tooltip("Defender Resistance → incoming hit stun AND knockback (lower = shrugs hits off).")]
    public AnimationCurve resistanceToStagger = new AnimationCurve(
        new Keyframe(0f, 1.25f), new Keyframe(16f, 1.00f), new Keyframe(40f, 0.75f));
    [Tooltip("Attacker ImpactPower → knockback and hit stun DEALT.")]
    public AnimationCurve impactToStagger = new AnimationCurve(
        new Keyframe(0f, 0.60f), new Keyframe(16f, 1.00f), new Keyframe(40f, 1.45f));
    [Tooltip("Own Agility → ATTACK speed: Startup/Recovery duration scale (lower = faster). Active never scales, so hit " +
             "windows, combo input windows and cancels keep their meaning.")]
    public AnimationCurve agilityToTimingScale = new AnimationCurve(
        new Keyframe(0f, 1.25f), new Keyframe(8f, 1.12f), new Keyframe(16f, 1.00f), new Keyframe(25f, 0.90f), new Keyframe(40f, 0.80f));
    public float minTimingScale = 0.78f;
    public float maxTimingScale = 1.28f;
    [Tooltip("Own Agility → MOVEMENT speed: walk / backpedal / run / side-walk (FighterMovement.SpeedMultiplier). " +
             "Dashes, lunges and knockback keep their own fixed speeds.")]
    public AnimationCurve agilityToMoveSpeed = new AnimationCurve(
        new Keyframe(0f, 0.62f), new Keyframe(8f, 0.78f), new Keyframe(16f, 0.95f), new Keyframe(25f, 1.10f), new Keyframe(40f, 1.30f));
    [Tooltip("Hard floor / ceiling of the movement multiplier: never immobile, never uncontrollable.")]
    public float minMoveSpeedMultiplier = 0.6f;
    public float maxMoveSpeedMultiplier = 1.35f;

    [Header("Rival progression (difficulty — never the player's run)")]
    [Tooltip("Total build points of a rival by its version / tier (OpponentLevelConfig.level). Its FighterStatsProfileSO " +
             "keeps the rival's PROPORTIONS (identity); this sets the size. Independent of the player's build on purpose.")]
    public AnimationCurve rivalBuildBudgetByLevel = new AnimationCurve(
        new Keyframe(1f, 45f), new Keyframe(2f, 60f), new Keyframe(3f, 75f), new Keyframe(4f, 90f));

    /// <summary>A rival's build for its level: the profile's proportions scaled to rivalBuildBudgetByLevel.</summary>
    public FighterBuildStats RivalBuild(FighterBuildStats profile, int level)
    {
        float budget = Mathf.Max(0f, rivalBuildBudgetByLevel.Evaluate(Mathf.Max(1, level)));
        float total = profile.Total;
        if (total <= 0f) return FighterBuildStats.Even(budget);
        float k = budget / total;
        return new FighterBuildStats
        {
            punchPower = profile.punchPower * k, kickPower = profile.kickPower * k, agility = profile.agility * k,
            resistance = profile.resistance * k, impactPower = profile.impactPower * k,
        };
    }

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
    [Tooltip("Multipliers on the Punch Finisher (combo P P P) — only when it is POWERED by one x3 (CombatResourceUse). Its timing lives on the combo definition.")]
    public FightAttackBonus punchFinisher = new(1.5f, 1.6f, 1.8f);
    [Tooltip("Multipliers on the Kick Finisher (combo K K K) — only when powered by one x3.")]
    public FightAttackBonus kickFinisher = new(1.5f, 1.6f, 2.0f);

    [Header("Signature Move (FightMoveSetSO.signatureBasic / signatureSpecial)")]
    [Tooltip("Multipliers on the BASIC Signature (no Special available) — keep below the Special version.")]
    public FightAttackBonus signatureBasic = new(0.75f, 0.8f, 0.8f);
    [Tooltip("Multipliers on the ENHANCED Signature (the projectile — costs 1 Special).")]
    public FightAttackBonus signatureSpecial = new(1.8f, 1.6f, 1.8f);

    [Header("Power State (hold Down + Punch + Kick)")]
    [Tooltip("Seconds Down + Punch + Kick must be held together to activate the Power State.")]
    [Min(0.1f)] public float powerActivationHoldTime = 1.0f;
    [Tooltip("Seconds the Power State lasts when ONE x4 is spent. Activating spends every x4 held (CombatResourceUse).")]
    [Min(0.1f)] public float powerDuration = 4f;
    [Tooltip("Extra seconds for each x4 spent beyond the first (4 s, 5 s with two, 6 s with three…). Provisional.")]
    [Min(0f)] public float powerSecondsPerExtraCharge = 1f;
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
            MoveSpeedMultiplier      = Mathf.Clamp(agilityToMoveSpeed.Evaluate(stats.agility), minMoveSpeedMultiplier, maxMoveSpeedMultiplier),
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
    public float TimingScale;              // own Agility (attack speed)
    public float MoveSpeedMultiplier;      // own Agility (movement speed)

    public static FightCombatModifiers Identity => new FightCombatModifiers
    {
        DamageDealtMultiplier = 1f, DamageTakenMultiplier = 1f, KnockbackDealtMultiplier = 1f,
        ResistanceMultiplier = 1f, TimingScale = 1f, MoveSpeedMultiplier = 1f,
    };
}
