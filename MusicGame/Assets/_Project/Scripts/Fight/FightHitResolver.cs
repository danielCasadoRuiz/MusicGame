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

    public bool IsBlocked;

    public float BaseDamage;
    public float DamageModifier;   // attacker Strength
    public float DefenseModifier;  // defender Defense
    public float FinalDamage;      // 0 while IsBlocked — see FinalChipDamage instead

    public float BaseHitStun;
    public float HitStunResistanceModifier; // defender Balance
    public float FinalHitStun;              // 0 while IsBlocked — see FinalBlockStun instead

    public float BaseKnockback;
    public float KnockbackModifier; // attacker Knockback
    public float FinalKnockback;    // also folds in defender Balance resistance; reduced further while IsBlocked

    /// <summary>Only meaningful while IsBlocked — the flat chip damage still applied.</summary>
    public float FinalChipDamage;
    /// <summary>Only meaningful while IsBlocked — see FightCombatBalanceConfig.blockStunMultiplier.</summary>
    public float FinalBlockStun;
}

/// <summary>
/// Pure, stateless hit-resolution — turns "this attacker's move hit this defender" into real
/// numbers, reading ONLY FighterStats (via FightCombatBalanceConfig's curves) and the hit's own base
/// values. Never touches Health/MoveController/Transform itself (see FighterAttack's own doc on
/// keeping detection/resolution/reaction separate) — callers apply the returned FightHitResult
/// through FighterHealth.ApplyDamage/FighterHitReaction.ApplyHit, or just call FightHitDispatcher
/// below, which does exactly that.
/// </summary>
public static class FightHitResolver
{
    public static FightHitResult Resolve(FighterActor attacker, FighterActor defender, FightMoveDefinition move,
        FightHitDefinition hitDef, FightCombatBalanceConfig config, bool isBlocked)
    {
        var attackerMods = config != null ? config.ComputeModifiers(attacker != null ? attacker.Stats : null) : FightCombatModifiers.Identity;
        var defenderMods = config != null ? config.ComputeModifiers(defender != null ? defender.Stats : null) : FightCombatModifiers.Identity;

        var result = new FightHitResult
        {
            Attacker = attacker, Defender = defender, Move = move, HitDef = hitDef, IsBlocked = isBlocked,
            BaseDamage = hitDef.baseDamage, DamageModifier = attackerMods.DamageDealtMultiplier, DefenseModifier = defenderMods.DamageTakenMultiplier,
            BaseHitStun = hitDef.baseHitStun, HitStunResistanceModifier = defenderMods.ResistanceMultiplier,
            BaseKnockback = hitDef.baseKnockback, KnockbackModifier = attackerMods.KnockbackDealtMultiplier,
        };

        if (isBlocked)
        {
            float blockStunMult      = config != null ? config.blockStunMultiplier      : 0.5f;
            float blockKnockbackMult = config != null ? config.blockKnockbackMultiplier : 0.5f;

            result.FinalChipDamage = UnityEngine.Mathf.Max(0f, hitDef.chipDamage * defenderMods.DamageTakenMultiplier);
            result.FinalBlockStun  = UnityEngine.Mathf.Max(0f, hitDef.baseHitStun * blockStunMult * defenderMods.ResistanceMultiplier);
            result.FinalKnockback  = UnityEngine.Mathf.Max(0f, hitDef.baseKnockback * blockKnockbackMult * attackerMods.KnockbackDealtMultiplier * defenderMods.ResistanceMultiplier);
            // FinalDamage/FinalHitStun stay 0 — a blocked hit never applies normal damage/hit stun.
        }
        else
        {
            result.FinalDamage    = UnityEngine.Mathf.Max(0f, hitDef.baseDamage * attackerMods.DamageDealtMultiplier * defenderMods.DamageTakenMultiplier);
            result.FinalHitStun   = UnityEngine.Mathf.Max(0f, hitDef.baseHitStun * defenderMods.ResistanceMultiplier);
            result.FinalKnockback = UnityEngine.Mathf.Max(0f, hitDef.baseKnockback * attackerMods.KnockbackDealtMultiplier * defenderMods.ResistanceMultiplier);
        }

        return result;
    }
}

/// <summary>
/// The ONE place a resolved hit is actually APPLIED — checks guard, resolves, then dispatches to
/// FighterHealth/FighterHitReaction and publishes the right event. Both FighterAttack (melee) and
/// FightProjectile (ranged) call this SAME method — see this phase's own explicit "no creïs un segon
/// sistema de damage per projectils" requirement. Neither caller ever touches Health/HitReaction or
/// publishes HitLandedEvent/HitBlockedEvent itself.
/// </summary>
public static class FightHitDispatcher
{
    public static FightHitResult ResolveAndApply(FighterActor attacker, FighterActor defender, FightMoveDefinition move,
        FightHitDefinition hitDef, FightCombatBalanceConfig config)
    {
        bool blocked = defender != null && defender.Guard != null && defender.Guard.WouldBlock(hitDef.attackHeight, hitDef.guardType);
        var result = FightHitResolver.Resolve(attacker, defender, move, hitDef, config, blocked);

        if (blocked)
        {
            EventBus.Publish(new HitBlockedEvent { Attacker = attacker, Defender = defender, Move = move, Result = result });
            if (result.FinalChipDamage > 0f) defender.Health?.ApplyDamage(result.FinalChipDamage);
            defender.HitReaction?.ApplyBlockedHit(result.FinalBlockStun, result.FinalKnockback);
        }
        else
        {
            EventBus.Publish(new HitLandedEvent { Attacker = attacker, Defender = defender, Move = move, Result = result });
            defender?.Health?.ApplyDamage(result.FinalDamage);
            defender?.HitReaction?.ApplyHit(result.FinalHitStun, result.FinalKnockback);
        }

        return result;
    }
}
