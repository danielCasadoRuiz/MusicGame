using UnityEngine;

/// <summary>
/// A single spawned instance of a FightMoveDefinition's AttackDelivery.Projectile — travels in a
/// straight line, tests overlap against the SAME FighterHurtbox/FightCombatShapes geometry a melee
/// hitbox uses, and resolves through the EXACT SAME FightHitDispatcher (guard check -> FightHitResolver
/// -> FighterHealth/FighterHitReaction -> HitLandedEvent/HitBlockedEvent) — see this phase's own
/// explicit "no creïs un segon sistema de damage per projectils" requirement. Never touches Health
/// directly.
///
/// Spawned by FighterAttack the instant its owning move's Active phase begins (see that class's own
/// doc) — this class then owns its OWN lifetime entirely independently; it does not react to
/// FightMovePhaseChangedEvent at all (the move that spawned it may already be in Recovery, or even a
/// brand new move, by the time this projectile actually connects or expires — that's intentional:
/// a fireball keeps flying after the fireball animation itself is "done").
///
/// Destroyed on: reaching maxDistance, reaching maxLifetime, hitting its target (if
/// FightProjectileData.destroyOnHit — V1 default, no piercing), or the round ending (see
/// FightFlowStateChangedEvent handling below) so a stray projectile never survives into RoundEnd/the
/// next round.
/// </summary>
public class FightProjectile : MonoBehaviour
{
    private FighterActor _owner;
    private FighterActor _target;
    private FightMoveDefinition _move;
    private FightProjectileData _data;
    private FightCombatBalanceConfig _balanceConfig;
    private Vector3 _direction;

    private float _traveled;
    private float _lifetime;
    private bool _consumed;

    private System.Action<FightFlowStateChangedEvent> _onFlowChanged;

    public FighterActor Owner => _owner;
    public float LifetimeRemaining => _data != null ? Mathf.Max(0f, _data.maxLifetime - _lifetime) : 0f;
    /// <summary>Read-only — lets FighterAI/FightDebugHUD inspect this projectile's own AttackHeight/
    /// GuardType (an already-visible, on-screen hazard) without touching anything it could act on
    /// directly (see FighterAI's own "no fer trampes" doc).</summary>
    public FightHitDefinition HitDefinition => _data != null ? _data.hitDefinition : null;

    public void Initialize(FighterActor owner, FighterActor target, FightMoveDefinition move,
        FightProjectileData data, FightCombatBalanceConfig balanceConfig, Vector3 direction)
    {
        _owner = owner;
        _target = target;
        _move = move;
        _data = data;
        _balanceConfig = balanceConfig;
        _direction = direction.normalized;
    }

    private void OnEnable()
    {
        _onFlowChanged = e =>
        {
            if (e.Current != FightFlowState.Fighting) Destroy(gameObject);
        };
        EventBus.Subscribe(_onFlowChanged);
    }

    private void OnDisable() => EventBus.Unsubscribe(_onFlowChanged);

    private void Update()
    {
        if (_data == null || _consumed) return;

        float delta = _data.speed * Time.deltaTime;
        transform.position += _direction * delta;
        _traveled += delta;
        _lifetime += Time.deltaTime;

        if (_traveled >= _data.maxDistance || _lifetime >= _data.maxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        if (_target == null || _data.hitDefinition == null) return;
        if (_target.Health != null && _target.Health.IsKO) return;

        foreach (var hurtbox in _target.Hurtboxes)
        {
            if (hurtbox == null) continue;
            if (!FightCombatShapes.Overlaps(transform.position, _data.hitDefinition, hurtbox.WorldCenter, hurtbox.Size)) continue;

            var result = FightHitDispatcher.ResolveAndApply(_owner, _target, _move, _data.hitDefinition, _balanceConfig);
            Debug.Log($"[FightProjectile] {(result.IsBlocked ? "Hit BLOCKED" : "Hit landed")}: {(_move != null ? _move.debugName : "?")} -> " +
                      $"{(result.IsBlocked ? result.FinalChipDamage : result.FinalDamage):F1} dmg");

            if (_data.destroyOnHit)
            {
                _consumed = true;
                Destroy(gameObject);
            }
            break;
        }
    }

    private void OnDrawGizmos()
    {
        if (!FightCombatDebugVisuals.Enabled || _data?.hitDefinition == null) return;
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.7f);
        if (_data.hitDefinition.shape == FightHitShape.Sphere) Gizmos.DrawWireSphere(transform.position, _data.hitDefinition.size.x);
        else Gizmos.DrawWireCube(transform.position, _data.hitDefinition.size);
    }
}
