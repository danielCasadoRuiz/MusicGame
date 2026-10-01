/// <summary>
/// Per-attack multipliers on top of a move's own hit values (FightHitDefinition) — the one way a
/// combo finisher, the Signature variants or the Power State make an attack stronger without a
/// separate attack/damage path. Captured when the move STARTS (FighterMoveController.BeginMove) and
/// carried by its projectile when it spawns, then applied by FightHitResolver.
/// </summary>
[System.Serializable]
public struct FightAttackBonus
{
    public float damage;
    public float stagger;   // hit stun (and block stun)
    public float knockback;

    public static FightAttackBonus Identity => new() { damage = 1f, stagger = 1f, knockback = 1f };

    public FightAttackBonus(float damage, float stagger, float knockback)
    {
        this.damage = damage;
        this.stagger = stagger;
        this.knockback = knockback;
    }

    public static FightAttackBonus operator *(FightAttackBonus a, FightAttackBonus b) =>
        new(a.damage * b.damage, a.stagger * b.stagger, a.knockback * b.knockback);

    public bool IsIdentity => damage == 1f && stagger == 1f && knockback == 1f;

    public override string ToString() => $"dmg ×{damage:0.##} stagger ×{stagger:0.##} kb ×{knockback:0.##}";
}
