using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns FighterMoveController's Active phase into real hit delivery — Player-only this phase (only
/// the Player has a MoveController/attacks at all, see FighterActor's own doc; the Opponent simply
/// has nothing generating hits against it). Reacts to FightMovePhaseChangedEvent rather than
/// re-deriving Startup/Active/Recovery timing itself — FighterMoveController remains the sole
/// authority (see that class's own doc).
///
/// TWO DELIVERY MODES, both decided purely by the active move's own AttackDelivery (see
/// FightMoveDefinition/FightProjectileData's own doc):
///   - Melee (the original/default behavior): polls hits[] against the opponent's hurtboxes every
///     frame while Active.
///   - Projectile: spawns exactly ONE FightProjectile the instant Active begins — that projectile
///     then owns its own travel/overlap loop independently (see its own doc); this class does
///     nothing further for that move's Active window.
///
/// PIPELINE (see this phase's own scope note on keeping responsibilities separate):
///   detect overlap (FightCombatShapes, geometry only)
///   -> resolve + apply (FightHitDispatcher — checks guard, calls FightHitResolver, dispatches to
///      FighterHealth/FighterHitReaction, publishes HitLandedEvent/HitBlockedEvent)
/// This class only ORCHESTRATES detection; it never itself does `defender.Health -= x`, checks guard
/// itself, or reaches into a Transform/MoveController directly. FightProjectile calls the EXACT same
/// FightHitDispatcher — see this phase's own explicit "no creïs un segon sistema de damage" requirement.
///
/// MULTI-HIT GUARD (melee only — a projectile's own destroyOnHit/one-shot nature makes this moot for
/// it): _hitTargetsThisWindow is cleared every time a NEW Active phase begins (a fresh execution of
/// a move, whether the same move or a different one) and a target already in it is skipped for the
/// rest of that window — a move deals damage to a given defender at most once per Active phase.
/// </summary>
public class FighterAttack : MonoBehaviour
{
    private FighterActor _actor;
    private FighterActor _opponent;
    private FighterMoveController _moveController;
    private FightCombatBalanceConfig _balanceConfig;

    private bool _active;
    private bool _hitboxActive;
    private FightMoveDefinition _activeMove;
    private readonly HashSet<FighterActor> _hitTargetsThisWindow = new();

    private System.Action<FightMovePhaseChangedEvent> _onPhaseChanged;
    private System.Action<FightFlowStateChangedEvent> _onFlowChanged;

    public void Initialize(FighterActor actor, FighterActor opponent, FighterMoveController moveController, FightCombatBalanceConfig balanceConfig)
    {
        _actor          = actor;
        _opponent       = opponent;
        _moveController = moveController;
        _balanceConfig  = balanceConfig;
    }

    private void OnEnable()
    {
        _onPhaseChanged = e =>
        {
            if (_moveController == null || e.Source != _moveController) return;

            if (e.Current == FighterMoveState.Active)
            {
                _activeMove = e.Move;
                _hitTargetsThisWindow.Clear();

                if (_activeMove != null && _activeMove.attackDelivery == AttackDelivery.Projectile)
                {
                    SpawnProjectile(_activeMove);
                    _hitboxActive = false; // this move's Active window is owned by the projectile now, not melee polling
                }
                else
                {
                    _hitboxActive = _activeMove != null && _activeMove.hits != null && _activeMove.hits.Length > 0;
                }
            }
            else if (_hitboxActive)
            {
                _hitboxActive = false;
                _activeMove = null;
            }
        };
        // Own FightFlowState gate — Fighting stops FighterMoveController from ever advancing its
        // own phase again the instant it ends (see that class's own Update), which would otherwise
        // leave a hitbox frozen ON (mid-Active) for the ENTIRE RoundEnd/RoundIntro/Countdown of the
        // next round if a round happened to end while a move's Active window was live — see this
        // phase's own scope note on why no stale Active window may survive into a new round.
        _onFlowChanged = e =>
        {
            _active = e.Current == FightFlowState.Fighting;
            if (!_active) _hitboxActive = false;
        };
        EventBus.Subscribe(_onPhaseChanged);
        EventBus.Subscribe(_onFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onPhaseChanged);
        EventBus.Unsubscribe(_onFlowChanged);
    }

    private void Update()
    {
        if (!_active || !_hitboxActive || _activeMove == null || _opponent == null) return;
        if (_opponent.Health != null && _opponent.Health.IsKO) return;
        if (_hitTargetsThisWindow.Contains(_opponent)) return;

        foreach (var hitDef in _activeMove.hits)
        {
            if (hitDef == null) continue;

            Vector3 hitCenter = ComputeWorldCenter(hitDef);
            if (!OverlapsAnyHurtbox(hitCenter, hitDef, _opponent)) continue;

            var result = FightHitDispatcher.ResolveAndApply(_actor, _opponent, _activeMove, hitDef, _balanceConfig);
            _hitTargetsThisWindow.Add(_opponent);
            Debug.Log($"[FighterAttack] {(result.IsBlocked ? "Hit BLOCKED" : "Hit landed")}: {_activeMove.debugName} -> " +
                      $"{(result.IsBlocked ? result.FinalChipDamage : result.FinalDamage):F1} dmg");
            break; // one resolved hit per target per Active window, even if several hitDefs would overlap this same frame
        }
    }

    private Vector3 ComputeWorldCenter(FightHitDefinition hitDef)
    {
        // Authored as if FacingRight were true — mirror X when actually facing left. See
        // FightHitDefinition.localOffset's own doc.
        float sign = _actor.FacingRight ? 1f : -1f;
        return _actor.transform.position + new Vector3(hitDef.localOffset.x * sign, hitDef.localOffset.y, hitDef.localOffset.z);
    }

    private static bool OverlapsAnyHurtbox(Vector3 hitCenter, FightHitDefinition hitDef, FighterActor defender)
    {
        foreach (var hurtbox in defender.Hurtboxes)
        {
            if (hurtbox != null && FightCombatShapes.Overlaps(hitCenter, hitDef, hurtbox.WorldCenter, hurtbox.Size))
                return true;
        }
        return false;
    }

    // ── Projectile delivery ───────────────────────────────────────────────────

    private void SpawnProjectile(FightMoveDefinition move)
    {
        var data = move.projectile;
        if (data == null)
        {
            Debug.LogWarning($"[FighterAttack] '{move.debugName}' has AttackDelivery.Projectile but no projectile data assigned — nothing spawned.");
            return;
        }

        float sign = _actor.FacingRight ? 1f : -1f;
        Vector3 spawnPos = _actor.transform.position + new Vector3(data.localSpawnOffset.x * sign, data.localSpawnOffset.y, data.localSpawnOffset.z);
        Vector3 direction = new Vector3(sign, 0f, 0f);

        var root = new GameObject($"FightProjectile_{move.debugName}");
        root.transform.position = spawnPos;

        if (data.visualPrefab != null)
        {
            var visual = Instantiate(data.visualPrefab, root.transform);
            visual.transform.localPosition = Vector3.zero;
        }
        else
        {
            var debugVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            debugVisual.transform.SetParent(root.transform, false);
            float diameter = Mathf.Max(0.2f, data.hitDefinition != null ? data.hitDefinition.size.x : 0.4f);
            debugVisual.transform.localScale = Vector3.one * diameter;
            var rend = debugVisual.GetComponent<Renderer>();
            if (rend != null) rend.material.color = new Color(1f, 0.55f, 0.1f);
            var col = debugVisual.GetComponent<Collider>();
            if (col != null) Destroy(col); // no Unity physics used anywhere in combat — see FightCombatShapes' own doc
        }

        root.AddComponent<FightProjectile>().Initialize(_actor, _opponent, move, data, _balanceConfig, direction);
    }

    /// <summary>Explicit API for FightMatchController's between-rounds reset (via FighterActor.
    /// ResetForRound) — deterministically clears any lingering hitbox/target-history state (see
    /// this phase's own scope note: "no hi ha un Active window antic que pugui impactar just quan
    /// comença una nova ronda"). Any already-spawned FightProjectile is left alone — it owns its own
    /// lifetime independently and will simply expire/despawn on its own.</summary>
    public void ResetForRound()
    {
        _hitboxActive = false;
        _activeMove = null;
        _hitTargetsThisWindow.Clear();
    }

    private void OnDrawGizmos()
    {
        if (!FightCombatDebugVisuals.Enabled || !_hitboxActive || _activeMove?.hits == null || _actor == null) return;

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.6f);
        foreach (var hitDef in _activeMove.hits)
        {
            if (hitDef == null) continue;
            Vector3 center = ComputeWorldCenter(hitDef);
            if (hitDef.shape == FightHitShape.Sphere) Gizmos.DrawWireSphere(center, hitDef.size.x);
            else Gizmos.DrawWireCube(center, hitDef.size);
        }
    }
}
