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

    /// <summary>The defender was invulnerable (Dodge window / knockdown flow): nothing was applied.</summary>
    public bool IsEvaded;
    /// <summary>The landed hit knocks the defender down (move.knockdownOnHit).</summary>
    public bool CausesKnockdown;

    public float BaseDamage;
    public float DamageModifier;   // attacker build stat (move.scalingStat)
    public float DefenseModifier;  // defender Resistance
    public float FinalDamage;      // 0 while IsBlocked — see FinalChipDamage instead

    public float BaseHitStun;
    public float HitStunResistanceModifier; // defender Resistance
    public float FinalHitStun;              // 0 while IsBlocked — see FinalBlockStun instead

    public float BaseKnockback;
    public float KnockbackModifier; // attacker ImpactPower
    public float FinalKnockback;    // also folds in defender Resistance; reduced further while IsBlocked

    /// <summary>Only meaningful while IsBlocked — the flat chip damage still applied.</summary>
    public float FinalChipDamage;
    /// <summary>Only meaningful while IsBlocked — see FightCombatBalanceConfig.blockStunMultiplier.</summary>
    public float FinalBlockStun;

    /// <summary>Per-attack multipliers that were applied (finisher / Signature / Power) — identity otherwise.</summary>
    public FightAttackBonus Bonus;
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
        FightHitDefinition hitDef, FightCombatBalanceConfig config, bool isBlocked) =>
        Resolve(attacker, defender, move, hitDef, config, isBlocked, FightAttackBonus.Identity);

    /// <summary>`bonus` (finisher / Signature / Power — see FightAttackBonus) scales damage, hit/block
    /// stun and knockback on top of every stat modifier, landed or blocked.</summary>
    public static FightHitResult Resolve(FighterActor attacker, FighterActor defender, FightMoveDefinition move,
        FightHitDefinition hitDef, FightCombatBalanceConfig config, bool isBlocked, FightAttackBonus bonus)
    {
        var scaling      = move != null ? move.scalingStat : FighterBuildStat.None;
        var attackerMods = config != null && attacker != null ? config.ComputeModifiers(attacker.BuildStats, scaling) : FightCombatModifiers.Identity;
        var defenderMods = config != null && defender != null ? config.ComputeModifiers(defender.BuildStats) : FightCombatModifiers.Identity;

        var result = new FightHitResult
        {
            Attacker = attacker, Defender = defender, Move = move, HitDef = hitDef, IsBlocked = isBlocked,
            BaseDamage = hitDef.baseDamage, DamageModifier = attackerMods.DamageDealtMultiplier, DefenseModifier = defenderMods.DamageTakenMultiplier,
            BaseHitStun = hitDef.baseHitStun, HitStunResistanceModifier = defenderMods.ResistanceMultiplier,
            BaseKnockback = hitDef.baseKnockback, KnockbackModifier = attackerMods.KnockbackDealtMultiplier,
            Bonus = bonus,
        };

        if (isBlocked)
        {
            float blockStunMult      = config != null ? config.blockStunMultiplier      : 0.5f;
            float blockKnockbackMult = config != null ? config.blockKnockbackMultiplier : 0.5f;

            // Block / chip damage: a correct guard REDUCES the hit (blockDamageMultiplier × the would-be
            // damage), it never nulls it — holding guard forever is not a strategy. A hand-authored
            // chipDamage is kept as a floor.
            float blockDamageMult = config != null ? config.blockDamageMultiplier : 0.2f;
            float chipBase = UnityEngine.Mathf.Max(hitDef.chipDamage, hitDef.baseDamage * attackerMods.DamageDealtMultiplier * blockDamageMult);
            result.FinalChipDamage = UnityEngine.Mathf.Max(0f, chipBase * defenderMods.DamageTakenMultiplier * bonus.damage);
            result.FinalBlockStun  = UnityEngine.Mathf.Max(0f, hitDef.baseHitStun * blockStunMult * defenderMods.ResistanceMultiplier * bonus.stagger);
            result.FinalKnockback  = UnityEngine.Mathf.Max(0f, hitDef.baseKnockback * blockKnockbackMult * attackerMods.KnockbackDealtMultiplier * defenderMods.ResistanceMultiplier * bonus.knockback);
            // FinalDamage/FinalHitStun stay 0 — a blocked hit never applies normal damage/hit stun.
        }
        else
        {
            result.FinalDamage    = UnityEngine.Mathf.Max(0f, hitDef.baseDamage * attackerMods.DamageDealtMultiplier * defenderMods.DamageTakenMultiplier * bonus.damage);
            result.FinalHitStun   = UnityEngine.Mathf.Max(0f, hitDef.baseHitStun * attackerMods.KnockbackDealtMultiplier * defenderMods.ResistanceMultiplier * bonus.stagger);
            result.CausesKnockdown = move != null && move.knockdownOnHit;
            result.FinalKnockback = UnityEngine.Mathf.Max(0f, hitDef.baseKnockback * attackerMods.KnockbackDealtMultiplier * defenderMods.ResistanceMultiplier * bonus.knockback);
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
        FightHitDefinition hitDef, FightCombatBalanceConfig config) =>
        ResolveAndApply(attacker, defender, move, hitDef, config, FightAttackBonus.Identity);

    public static FightHitResult ResolveAndApply(FighterActor attacker, FighterActor defender, FightMoveDefinition move,
        FightHitDefinition hitDef, FightCombatBalanceConfig config, FightAttackBonus bonus)
    {
        // Invulnerable (Dodge window, knockdown flow): the hit whiffs — nothing is applied.
        if (defender != null && defender.IsInvulnerable)
        {
            var evaded = new FightHitResult { Attacker = attacker, Defender = defender, Move = move, HitDef = hitDef, IsEvaded = true };
            EventBus.Publish(new HitEvadedEvent { Attacker = attacker, Defender = defender, Move = move });
            return evaded;
        }

        bool blocked = defender != null && defender.Guard != null && defender.Guard.WouldBlock(hitDef.attackHeight, hitDef.guardType);
        var result = FightHitResolver.Resolve(attacker, defender, move, hitDef, config, blocked, bonus);

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
            if (result.CausesKnockdown && defender?.Health != null && !defender.Health.IsKO)
                defender.HitReaction?.ApplyKnockdown(result.FinalKnockback);
            else
                defender?.HitReaction?.ApplyHit(result.FinalHitStun, result.FinalKnockback);
        }

        return result;
    }
}
