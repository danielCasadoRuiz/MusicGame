using UnityEngine;

/// <summary>
/// How a FightMoveDefinition's hit actually reaches the opponent — see FightMoveDefinition.
/// attackDelivery's own doc. Melee is exactly today's existing FighterAttack behavior; Projectile
/// spawns a FightProjectile instead (see that class's own doc) at the same Active-phase moment a
/// melee hitbox would otherwise turn on.
/// </summary>
public enum AttackDelivery
{
    Melee,
    Projectile,
}

/// <summary>
/// Everything a FightMoveDefinition needs to spawn and run one FightProjectile — only read/used when
/// that move's attackDelivery is Projectile. Plain [Serializable] class, same "embedded data, not a
/// standalone asset" shape as FightHitDefinition.
/// </summary>
[System.Serializable]
public class FightProjectileData
{
    [Tooltip("Optional — instantiated as the projectile's visual child. Null falls back to a plain " +
             "debug sphere sized from hitDefinition.size.x, tinted so it reads clearly as a hazard.")]
    public GameObject visualPrefab;

    [Tooltip("World units/second along the attacker's facing direction (mirrored automatically, " +
             "same convention as FightHitDefinition.localOffset).")]
    public float speed = 8f;

    public float maxLifetime = 3f;
    public float maxDistance = 10f;

    [Tooltip("Spawn position relative to the attacking FighterActor's gameplay root, authored as if " +
             "FacingRight were true — same mirroring convention as FightHitDefinition.localOffset.")]
    public Vector3 localSpawnOffset = new Vector3(0.8f, 1f, 0f);

    [Tooltip("Carries this projectile's own AttackHeight/GuardType/damage/hitStun/knockback/" +
             "chipDamage — resolved through the EXACT SAME FightHitResolver/FightHitDispatcher a " +
             "melee hit uses (see FightProjectile's own doc). localOffset/shape on this entry are " +
             "read relative to the PROJECTILE's own current position, not the attacker's.")]
    public FightHitDefinition hitDefinition = new FightHitDefinition();

    [Tooltip("True: the projectile is destroyed the instant it lands a hit (V1 default — no " +
             "piercing). False is reserved for a future piercing projectile; not implemented this phase.")]
    public bool destroyOnHit = true;
}
