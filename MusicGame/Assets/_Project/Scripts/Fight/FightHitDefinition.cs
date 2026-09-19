using UnityEngine;

/// <summary>Which overlap test a hit uses — whatever fits the move best (see FightHitDefinition's
/// own doc). Both are tested against the SAME hurtbox shape (an AABB — see FighterHurtbox).</summary>
public enum FightHitShape
{
    Box,
    Sphere,
}

/// <summary>
/// One hitbox's offensive data — everything FighterAttack needs to place, size, and resolve a single
/// hit. Plain [Serializable] class (not a ScriptableObject): it only ever exists embedded inside a
/// FightMoveDefinition's `hits` array, never referenced independently.
///
/// A move's `hits` array is ALREADY a list — this V1 only ever populates one entry, but nothing
/// about FighterAttack/FightMoveDefinition assumes exactly one: a future multi-hit move (e.g. a
/// flurry with several distinct hitboxes active at once) is a pure data addition, not a pipeline
/// change. What's NOT here yet is per-hit timing (an active window narrower than the move's own
/// Active phase) — see FighterAttack's own doc on why: FighterMoveController remains the sole
/// authority on Startup/Active/Recovery timing, so all of a move's hits simply share its one Active
/// window for now.
/// </summary>
[System.Serializable]
public class FightHitDefinition
{
    public FightHitShape shape = FightHitShape.Box;

    [Tooltip("Position relative to the attacking FighterActor's gameplay root, authored as if " +
             "FacingRight were true (i.e. +X = towards the opponent) — FighterAttack mirrors the X " +
             "component automatically when the attacker is actually facing left. Never relative to " +
             "VisualRoot/mesh/bones — stays correct verbatim once a real humanoid replaces the capsule.")]
    public Vector3 localOffset = new Vector3(0.8f, 1f, 0f);

    [Tooltip("Box: full size (x, y, z). Sphere: only .x is used, as the radius.")]
    public Vector3 size = new Vector3(0.6f, 0.6f, 0.6f);

    [Header("Base combat values — see FightCombatBalanceConfig for how FighterStats modify these")]
    public float baseDamage = 10f;
    public float baseHitStun = 0.3f;
    public float baseKnockback = 1f;
}
