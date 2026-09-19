/// <summary>
/// One hit's fully-resolved outcome — see FightHitResolver.Resolve. Carries every intermediate
/// modifier alongside the base/final pairs specifically for FightDebugHUD's "Last Hit" section
/// (task's own explicit ask: this is what balancing actually needs to see).
/// </summary>
public struct FightHitResult
{
    public FighterActor Attacker;
    public FighterActor Defender;
    public FightMoveDefinition Move;
    public FightHitDefinition HitDef;

    public float BaseDamage;
    public float DamageModifier;   // attacker Strength
    public float DefenseModifier;  // defender Defense
    public float FinalDamage;

    public float BaseHitStun;
    public float HitStunResistanceModifier; // defender Balance
    public float FinalHitStun;

    public float BaseKnockback;
    public float KnockbackModifier; // attacker Knockback
    public float FinalKnockback;    // also folds in defender Balance resistance
}

/// <summary>
/// Pure, stateless hit-resolution — turns "this attacker's move hit this defender" into real
/// numbers, reading ONLY FighterStats (via FightCombatBalanceConfig's curves) and the hit's own base
/// values. Never touches Health/MoveController/Transform itself (see FighterAttack's own doc on
/// keeping detection/resolution/reaction separate) — callers apply the returned FightHitResult
/// through FighterHealth.ApplyDamage/FighterHitReaction.ApplyHit.
/// </summary>
public static class FightHitResolver
{
    public static FightHitResult Resolve(FighterActor attacker, FighterActor defender, FightMoveDefinition move,
        FightHitDefinition hitDef, FightCombatBalanceConfig config)
    {
        var attackerMods = config != null ? config.ComputeModifiers(attacker != null ? attacker.Stats : null) : FightCombatModifiers.Identity;
        var defenderMods = config != null ? config.ComputeModifiers(defender != null ? defender.Stats : null) : FightCombatModifiers.Identity;

        float finalDamage    = UnityEngine.Mathf.Max(0f, hitDef.baseDamage * attackerMods.DamageDealtMultiplier * defenderMods.DamageTakenMultiplier);
        float finalHitStun   = UnityEngine.Mathf.Max(0f, hitDef.baseHitStun * defenderMods.ResistanceMultiplier);
        float finalKnockback = UnityEngine.Mathf.Max(0f, hitDef.baseKnockback * attackerMods.KnockbackDealtMultiplier * defenderMods.ResistanceMultiplier);

        return new FightHitResult
        {
            Attacker = attacker, Defender = defender, Move = move, HitDef = hitDef,

            BaseDamage = hitDef.baseDamage, DamageModifier = attackerMods.DamageDealtMultiplier,
            DefenseModifier = defenderMods.DamageTakenMultiplier, FinalDamage = finalDamage,

            BaseHitStun = hitDef.baseHitStun, HitStunResistanceModifier = defenderMods.ResistanceMultiplier, FinalHitStun = finalHitStun,

            BaseKnockback = hitDef.baseKnockback, KnockbackModifier = attackerMods.KnockbackDealtMultiplier, FinalKnockback = finalKnockback,
        };
    }
}
