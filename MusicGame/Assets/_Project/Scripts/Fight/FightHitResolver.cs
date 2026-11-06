using UnityEngine;

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
    public float KnockbackModifier; // attacker's attack power (move.scalingStat) on the reaction curve
    public float FinalKnockback;    // also folds in defender Resistance; reduced further while IsBlocked

    /// <summary>Only meaningful while IsBlocked — the flat chip damage still applied.</summary>
    public float FinalChipDamage;
    /// <summary>Only meaningful while IsBlocked — see FightCombatBalanceConfig.blockStunMultiplier.</summary>
    public float FinalBlockStun;

    /// <summary>Per-attack multipliers that were applied (finisher / Signature / Power) — identity otherwise.</summary>
    public FightAttackBonus Bonus;

    /// <summary>World point where the strike touched (real-body contact, or the hitbox centre) — hit spark.</summary>
    public Vector3 ContactPoint;
    public bool HasContact;
    /// <summary>The defender was lying on the floor (a ground hit: reduced, no new knockdown).</summary>
    public bool IsGroundHit;
    /// <summary>A correct guard was BROKEN (powered x3 finisher): landed as a hit, scaled by finisherGuardBreakFactor.</summary>
    public bool IsGuardBreak;
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
            BaseKnockback = hitDef.baseKnockback, KnockbackModifier = attackerMods.ReactionDealtMultiplier,
            Bonus = bonus,
        };
        // The reaction a hit causes: the move's own baseHitStun / baseKnockback (its character) × the attack power
        // (the move's scaling stat, reaction curve) × the defender's Resistance × the finisher / Power bonus — bounded
        // (FightCombatBalanceConfig.ReactionScale) so a strong build never stun-locks and a weak one still reacts.
        float stunScale = config != null ? config.ReactionScale(attackerMods.ReactionDealtMultiplier, defenderMods.ResistanceMultiplier, bonus.stagger)
                                         : attackerMods.ReactionDealtMultiplier * defenderMods.ResistanceMultiplier * bonus.stagger;
        float pushScale = config != null ? config.ReactionScale(attackerMods.ReactionDealtMultiplier, defenderMods.ResistanceMultiplier, bonus.knockback)
                                         : attackerMods.ReactionDealtMultiplier * defenderMods.ResistanceMultiplier * bonus.knockback;

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
            result.FinalKnockback  = UnityEngine.Mathf.Max(0f, hitDef.baseKnockback * blockKnockbackMult * pushScale);
            // FinalDamage/FinalHitStun stay 0 — a blocked hit never applies normal damage/hit stun.
        }
        else
        {
            result.FinalDamage    = UnityEngine.Mathf.Max(0f, hitDef.baseDamage * attackerMods.DamageDealtMultiplier * defenderMods.DamageTakenMultiplier * bonus.damage);
            // Fixed-fraction moves (the Special): exactly that share of the defender's max health, whoever throws it.
            if (move != null && move.damageAsMaxHealthFraction > 0f && defender != null && defender.Health != null)
                result.FinalDamage = defender.Health.MaxHealth * move.damageAsMaxHealthFraction;
            result.FinalHitStun   = UnityEngine.Mathf.Max(0f, hitDef.baseHitStun * stunScale);
            result.CausesKnockdown = move != null && move.knockdownOnHit;
            result.FinalKnockback = UnityEngine.Mathf.Max(0f, hitDef.baseKnockback * pushScale);
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
    /// <summary>SIDE (2026-11-05): the attacker's striking limb, seen from the defender — a right hand / foot arrives on
    /// the defender's LEFT. 0 = central / unsided.</summary>
    public static int IncomingSide(FighterActor attacker, FightMoveDefinition move) =>
        attacker != null && attacker.MoveController != null && attacker.MoveController.CurrentMove == move
            ? -attacker.MoveController.CurrentStrikeSide : 0;

    /// <summary>The defender's guard stops this strike right now (height, side + reaction time, from above → high guard only).</summary>
    public static bool GuardStops(FighterActor attacker, FighterActor defender, FightMoveDefinition move, FightHitDefinition hitDef) =>
        defender != null && defender.Guard != null && hitDef != null && (move == null || !move.guardBreak) &&
        defender.Guard.WouldBlock(hitDef.attackHeight, hitDef.guardType, IncomingSide(attacker, move),
                                  attacker != null && attacker.Posture == FighterPosture.Airborne);

    public static FightHitResult ResolveAndApply(FighterActor attacker, FighterActor defender, FightMoveDefinition move,
        FightHitDefinition hitDef, FightCombatBalanceConfig config) =>
        ResolveAndApply(attacker, defender, move, hitDef, config, FightAttackBonus.Identity);

    public static FightHitResult ResolveAndApply(FighterActor attacker, FighterActor defender, FightMoveDefinition move,
        FightHitDefinition hitDef, FightCombatBalanceConfig config, FightAttackBonus bonus) =>
        ResolveAndApply(attacker, defender, move, hitDef, config, bonus, null);

    /// <summary>`contact` = where the strike touched (FighterAttack's real-body contact) — carried on the
    /// result for the hit spark; null = the defender's centre.
    /// GROUND HITS (2026-11-04): a DOWNED defender can only be hit by LOW attacks, at
    /// groundHitDamageMultiplier, a limited number of times per knockdown (FighterHitReaction.ApplyGroundHit).
    /// GUARD BREAK: move.guardBreak ignores guard. A POWERED kick finisher knocks down.</summary>
    public static FightHitResult ResolveAndApply(FighterActor attacker, FighterActor defender, FightMoveDefinition move,
        FightHitDefinition hitDef, FightCombatBalanceConfig config, FightAttackBonus bonus, Vector3? contact)
    {
        Vector3 point = contact ?? (defender != null ? defender.transform.position : Vector3.zero);

        // Lying on the floor: only LOW attacks reach, reduced, no new knockdown.
        var reaction = defender != null ? defender.HitReaction : null;
        if (reaction != null && reaction.State == FighterReactionState.Downed && reaction.CanTakeGroundHit)
        {
            if (hitDef.attackHeight != AttackHeight.Low)
            {
                EventBus.Publish(new HitEvadedEvent { Attacker = attacker, Defender = defender, Move = move });
                return new FightHitResult { Attacker = attacker, Defender = defender, Move = move, HitDef = hitDef, IsEvaded = true };
            }
            var ground = FightHitResolver.Resolve(attacker, defender, move, hitDef, config, false, bonus);
            ground.FinalDamage *= config != null ? config.groundHitDamageMultiplier : 0.5f;
            ground.CausesKnockdown = false;
            ground.IsGroundHit = true;
            ground.ContactPoint = point; ground.HasContact = true;
            EventBus.Publish(new HitLandedEvent { Attacker = attacker, Defender = defender, Move = move, Result = ground });
            defender.Health?.ApplyDamage(ground.FinalDamage);
            if (defender.Health == null || !defender.Health.IsKO) reaction.ApplyGroundHit();
            return ground;
        }

        // Invulnerable (Dodge window, knockdown flow): the hit whiffs — nothing is applied.
        if (defender != null && defender.IsInvulnerable)
        {
            var evaded = new FightHitResult { Attacker = attacker, Defender = defender, Move = move, HitDef = hitDef, IsEvaded = true };
            EventBus.Publish(new HitEvadedEvent { Attacker = attacker, Defender = defender, Move = move });
            return evaded;
        }

        int incomingSide = IncomingSide(attacker, move);
        bool blocked = GuardStops(attacker, defender, move, hitDef);
        bool poweredFinisher = attacker != null && attacker.MoveController != null &&
                               attacker.MoveController.CurrentTag == FighterMoveController.AttackTag.Finisher;
        // GUARD BREAK: the strong hit of a POWERED triple press goes through a correct guard, a bit weaker than unguarded.
        bool guardBreak = blocked && poweredFinisher && config != null && config.finisherGuardBreakFactor > 0f;
        if (guardBreak) blocked = false;
        var result = FightHitResolver.Resolve(attacker, defender, move, hitDef, config, blocked, bonus);
        result.ContactPoint = point;
        result.HasContact = true;
        if (guardBreak)
        {
            float f = config.finisherGuardBreakFactor;
            result.IsGuardBreak = true;
            result.FinalDamage *= f;
            result.FinalHitStun *= f;
            result.FinalKnockback *= f;
        }
        // A POWERED kick finisher (paid with an x3) knocks down — not through a guard; the punch one is a big stagger.
        if (!blocked && !guardBreak && config != null && config.poweredKickFinisherKnocksDown && move != null &&
            move.scalingStat == FighterBuildStat.KickPower && poweredFinisher)
            result.CausesKnockdown = true;

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
                defender?.HitReaction?.ApplyHit(result.FinalHitStun, result.FinalKnockback, -incomingSide);
        }

        return result;
    }
}
