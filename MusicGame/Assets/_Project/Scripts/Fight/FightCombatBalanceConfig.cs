using UnityEngine;

/// <summary>
/// The single place every FighterStat's V1 combat effect is defined — no formulas scattered across
/// FighterAttack/FightHitResolver/FighterMoveController. Each curve's X axis is the RAW FighterStats
/// value (base ~100, see FighterStatsBuilder) — e.g. the default strengthToDamageDealt curve has
/// keys at (100, 1.00), (150, 1.10), (200, 1.20), meaning "Strength 150 deals 10% more damage",
/// exactly as authored here — 200 Strength is NOT assumed to mean 2x damage anywhere in this layer.
///
/// Deliberately asymmetric coverage — see each field's own doc. Agility/Combo/SpecialPower stay
/// INERT this phase (evaluated only for FightDebugHUD's own transparency/future-readiness, never
/// multiplied into an actual gameplay number) rather than inventing an artificial mechanic just to
/// "use" all eight stats — a worse combat feel is a worse outcome than a temporarily-inert stat.
/// </summary>
[CreateAssetMenu(fileName = "FightCombatBalanceConfig", menuName = "MusicGame/Fight/Combat Balance Config")]
public class FightCombatBalanceConfig : ScriptableObject
{
    [Header("Health")]
    public float defaultMaxHealth = 100f;

    [Header("Strength (attacker) -> damage DEALT multiplier")]
    public AnimationCurve strengthToDamageDealt = new AnimationCurve(
        new Keyframe(100f, 1.00f), new Keyframe(150f, 1.10f), new Keyframe(200f, 1.20f));

    [Header("Defense (defender) -> damage TAKEN multiplier (lower = tankier)")]
    public AnimationCurve defenseToDamageTaken = new AnimationCurve(
        new Keyframe(100f, 1.00f), new Keyframe(150f, 0.90f), new Keyframe(200f, 0.80f));

    [Header("Knockback (attacker) -> knockback DEALT multiplier")]
    public AnimationCurve knockbackToKnockbackDealt = new AnimationCurve(
        new Keyframe(100f, 1.00f), new Keyframe(150f, 1.15f), new Keyframe(200f, 1.30f));

    [Tooltip("Reduces BOTH incoming hit stun duration AND incoming knockback distance — one shared " +
             "dial rather than two, since 'resisting a hit' is one concept for V1.")]
    [Header("Balance (defender) -> resistance multiplier (hit stun AND knockback)")]
    public AnimationCurve balanceToResistance = new AnimationCurve(
        new Keyframe(100f, 1.00f), new Keyframe(150f, 0.85f), new Keyframe(200f, 0.70f));

    [Tooltip("Scales ONLY startupDuration/recoveryDuration (see FighterMoveController's own doc) — " +
             "activeDuration is never touched, so hit timing/hurtbox windows stay predictable.")]
    [Header("Speed -> move timing scale")]
    public AnimationCurve speedToTimingScale = new AnimationCurve(
        new Keyframe(100f, 1.00f), new Keyframe(150f, 0.92f), new Keyframe(200f, 0.85f));
    [Tooltip("Hard clamp on speedToTimingScale's output — prevents a very high Speed (or a bad curve " +
             "edit) from making moves absurdly instant.")]
    public float minTimingScale = 0.7f;
    public float maxTimingScale = 1.15f;

    [Header("Block — see FighterGuard/FightHitResolver's own doc")]
    [Tooltip("Applied on top of the defender's own Balance resistance when a hit is blocked.")]
    public float blockStunMultiplier = 0.5f;
    [Tooltip("Applied on top of the attacker's Knockback/defender's Balance when a hit is blocked.")]
    public float blockKnockbackMultiplier = 0.5f;

    [Header("Inert this phase — evaluated for debug/future use only, never applied to gameplay (see class doc)")]
    public AnimationCurve agilityModifierPreview      = AnimationCurve.Linear(100f, 1f, 200f, 1f);
    public AnimationCurve comboModifierPreview        = AnimationCurve.Linear(100f, 1f, 200f, 1f);
    public AnimationCurve specialPowerModifierPreview = AnimationCurve.Linear(100f, 1f, 200f, 1f);

    /// <summary>Evaluates every curve above against one fighter's stats — the SAME computation both
    /// FightHitResolver (real combat) and FightDebugHUD ("combat modifiers finals") use, so the HUD
    /// can never drift from what an actual hit does.</summary>
    public FightCombatModifiers ComputeModifiers(FighterStats stats)
    {
        float Get(FightStatId id) => stats != null ? stats.Get(id) : 100f;

        return new FightCombatModifiers
        {
            DamageDealtMultiplier    = strengthToDamageDealt.Evaluate(Get(FightStatId.Strength)),
            DamageTakenMultiplier    = defenseToDamageTaken.Evaluate(Get(FightStatId.Defense)),
            KnockbackDealtMultiplier = knockbackToKnockbackDealt.Evaluate(Get(FightStatId.Knockback)),
            ResistanceMultiplier     = balanceToResistance.Evaluate(Get(FightStatId.Balance)),
            TimingScale              = Mathf.Clamp(speedToTimingScale.Evaluate(Get(FightStatId.Speed)), minTimingScale, maxTimingScale),
            AgilityModifierPreview      = agilityModifierPreview.Evaluate(Get(FightStatId.Agility)),
            ComboModifierPreview        = comboModifierPreview.Evaluate(Get(FightStatId.Combo)),
            SpecialPowerModifierPreview = specialPowerModifierPreview.Evaluate(Get(FightStatId.SpecialPower)),
        };
    }
}

/// <summary>One fighter's fully-evaluated V1 combat modifiers — see FightCombatBalanceConfig.ComputeModifiers.</summary>
public struct FightCombatModifiers
{
    public float DamageDealtMultiplier;
    public float DamageTakenMultiplier;
    public float KnockbackDealtMultiplier;
    public float ResistanceMultiplier;
    public float TimingScale;

    // Inert — see FightCombatBalanceConfig's own doc.
    public float AgilityModifierPreview;
    public float ComboModifierPreview;
    public float SpecialPowerModifierPreview;

    public static FightCombatModifiers Identity => new FightCombatModifiers
    {
        DamageDealtMultiplier = 1f, DamageTakenMultiplier = 1f, KnockbackDealtMultiplier = 1f,
        ResistanceMultiplier = 1f, TimingScale = 1f,
        AgilityModifierPreview = 1f, ComboModifierPreview = 1f, SpecialPowerModifierPreview = 1f,
    };
}
