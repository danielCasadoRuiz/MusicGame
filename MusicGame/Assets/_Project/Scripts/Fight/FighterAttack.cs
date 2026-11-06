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
/// REAL-BODY CONTACT (2026-11-04): when both fighters have a real avatar (FighterActor.Body), a melee hit
/// connects only when the striking fist / foot capsule actually TOUCHES the defender's body capsules
/// (FighterBody) — no more hits from a distance the animation never covers. The move's hits[] still
/// supplies the numbers (damage, stun, height, guard); its box only decides contact for placeholder
/// capsules and for ground hits on a DOWNED defender (a lying box). Contact keeps being checked for
/// lateContactSeconds into Recovery, so a clip whose impact lands a hair after the Active window still
/// counts. Crouching defenders still make HIGH attacks whiff, and AIRBORNE ones LOW attacks (rules, independent of the clip).
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

    // Captured ONCE, the instant this move's Active phase begins — see FightHitTracking's own doc.
    // Linear (the only behavior implemented this phase) keeps hitbox placement fixed to the
    // attacker's facing AT THAT MOMENT for the whole Active window, which is what actually lets a
    // sidestep dodge it (the attacker's LIVE facing is usually frozen anyway via
    // lockFacingDuringMove, but capturing explicitly stays correct even for a move that isn't).
    // Partial/Homing tracking would re-sample these mid-Active instead — not implemented this phase.
    // Real-body contact keeps being looked for this long into Recovery (clip impact slightly late).
    private const float LateContactSeconds = 0.1f;
    // A strike within this gap of the body counts (motion blur / mocap variance — visually it touches).
    public const float ContactSlack = 0.12f;
    private float _lateUntil = -1f;

    private Vector3 _activeAttackForward = Vector3.right;
    private Vector3 _activeAttackSide = Vector3.forward;

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
                _lateUntil = -1f;
                LastContactGap = float.PositiveInfinity;
                _activeAttackForward = _actor.ForwardXZ;
                _activeAttackSide    = _actor.SideXZ;

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
            else if (_hitboxActive && e.Current == FighterMoveState.Recovery && e.Previous == FighterMoveState.Active &&
                     _activeMove != null && UsesBodies && !_hitTargetsThisWindow.Contains(_opponent))
            {
                _lateUntil = Time.time + LateContactSeconds; // see class doc
            }
            else if (_hitboxActive)
            {
                _hitboxActive = false;
                _activeMove = null;
                _lateUntil = -1f;
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

    /// <summary>Both fighters have real bodies: contact is decided by FighterBody (see class doc).</summary>
    private bool UsesBodies => _actor != null && _opponent != null && _actor.Body != null && _opponent.Body != null;

    /// <summary>Debug: closest gap (m) between the striking limb and the defender's body this Active window.</summary>
    public float LastContactGap { get; private set; } = float.PositiveInfinity;

    private void Update()
    {
        if (!_active || !_hitboxActive || _activeMove == null || _opponent == null) return;
        if (_lateUntil > 0f && Time.time > _lateUntil) { _hitboxActive = false; _activeMove = null; _lateUntil = -1f; return; }
        if (_opponent.Health != null && _opponent.Health.IsKO) return;
        if (_hitTargetsThisWindow.Contains(_opponent)) return;

        bool downed = _opponent.HitReaction != null && _opponent.HitReaction.State == FighterReactionState.Downed;
        bool crouching = _opponent.Posture == FighterPosture.Crouching;
        bool airborne = _opponent.Posture == FighterPosture.Airborne;

        foreach (var hitDef in _activeMove.hits)
        {
            if (hitDef == null) continue;
            if (crouching && hitDef.attackHeight == AttackHeight.High) continue; // whiffs over a crouch
            if (airborne && hitDef.attackHeight == AttackHeight.Low) continue;    // a jump clears low punches / kicks

            Vector3 hitCenter = ComputeWorldCenter(hitDef);
            Vector3 contact;
            if (downed)
            {
                if (!OverlapsLyingBody(hitCenter, hitDef, _opponent)) continue;
                contact = hitCenter;
            }
            else if (UsesBodies)
            {
                var limb = FighterBody.LimbFor(_activeMove);
                bool touches = _actor.Body.StrikeTouches(_opponent.Body, limb, ContactSlack, out contact, out float gap);
                LastContactGap = Mathf.Min(LastContactGap, gap);
                // The blow meets the GUARDING ARMS: a block when the guard really stops it (side, height, in time); a guard
                // late or on the wrong side doesn't — the blow goes through it (it lands). Arms of a fighter NOT guarding
                // are no target (two guards at close range already touch: a clash, not a hit).
                if (!touches && _opponent.Guard != null && _opponent.Guard.State != FighterGuardState.None &&
                    _actor.Body.StrikeTouchesArms(_opponent.Body, limb, ContactSlack, out var armContact))
                {
                    touches = true;
                    contact = armContact;
                }
                if (!touches) continue;
            }
            else
            {
                if (!OverlapsAnyHurtbox(hitCenter, hitDef, _opponent)) continue;
                contact = hitCenter;
            }

            var result = FightHitDispatcher.ResolveAndApply(_actor, _opponent, _activeMove, hitDef, _balanceConfig,
                                                            _moveController != null ? _moveController.CurrentAttackBonus : FightAttackBonus.Identity,
                                                            contact);
            _hitTargetsThisWindow.Add(_opponent);
            Debug.Log($"[FighterAttack] {(result.IsBlocked ? "Hit BLOCKED" : "Hit landed")}: {_activeMove.debugName} -> " +
                      $"{(result.IsBlocked ? result.FinalChipDamage : result.FinalDamage):F1} dmg");
            break; // one resolved hit per target per Active window, even if several hitDefs would overlap this same frame
        }
    }

    private Vector3 ComputeWorldCenter(FightHitDefinition hitDef)
    {
        // Authored in the attacker's own local frame — localOffset.x = forward distance,
        // localOffset.z = sideways offset, localOffset.y = height — transformed into world space
        // via the CAPTURED attack-start forward/side basis (see _activeAttackForward's own doc),
        // not the live one and never assumed to be world X/Z. Reduces to the old
        // "mirror X, pass Z straight through" behavior exactly when forward/side happen to be
        // world-axis-aligned, so every move authored before 3D depth existed keeps working verbatim.
        Vector3 worldOffset = _activeAttackForward * hitDef.localOffset.x
                             + _activeAttackSide    * hitDef.localOffset.z
                             + Vector3.up           * hitDef.localOffset.y;
        return _actor.transform.position + worldOffset;
    }

    // A DOWNED fighter lies on the floor, its body stretched away from the attacker: a flat box there.
    private bool OverlapsLyingBody(Vector3 hitCenter, FightHitDefinition hitDef, FighterActor defender)
    {
        float floor = defender.transform.position.y - 1f;
        foreach (var h in defender.Hurtboxes)
            if (h != null) { floor = h.WorldCenter.y - h.Size.y * 0.5f; break; }
        Vector3 center = defender.transform.position + _actor.ForwardXZ * 0.45f;
        center.y = floor + 0.25f;
        return FightCombatShapes.Overlaps(hitCenter, hitDef, center, new Vector3(1.7f, 0.5f, 1.7f));
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

        // Same captured attack-start basis ComputeWorldCenter uses (see its own doc) — the
        // projectile then travels in a straight line along `direction` for its whole lifetime (see
        // FightProjectile's own doc), so THIS is the one moment its trajectory is ever decided.
        Vector3 spawnPos = _actor.transform.position
                          + _activeAttackForward * data.localSpawnOffset.x
                          + _activeAttackSide    * data.localSpawnOffset.z
                          + Vector3.up           * data.localSpawnOffset.y;
        // A real avatar throws it FROM ITS HANDS (the release pose of the throw clip), a little in front of them.
        var anim = _actor.BoundAnimator;
        if (anim != null && anim.isHuman)
        {
            var l = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            var r = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (l != null && r != null) spawnPos = (l.position + r.position) * 0.5f + _activeAttackForward * 0.2f;
        }
        Vector3 direction = _activeAttackForward;

        var root = new GameObject($"FightProjectile_{move.debugName}");
        root.transform.position = spawnPos;

        if (data.visualPrefab != null)
        {
            var visual = Instantiate(data.visualPrefab, root.transform);
            visual.transform.localPosition = Vector3.zero;
        }
        else
        {
            // No authored visual: the code-built fireball (core + glow + burning trail + light).
            float diameter = Mathf.Max(0.3f, data.hitDefinition != null ? data.hitDefinition.size.x : 0.4f);
            FightFireballVisual.Build(root.transform, diameter);
        }

        root.AddComponent<FightProjectile>().Initialize(_actor, _opponent, move, data, _balanceConfig, direction,
                                                        _moveController != null ? _moveController.CurrentAttackBonus : FightAttackBonus.Identity);
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
        _lateUntil = -1f;
        _hitTargetsThisWindow.Clear();
    }

    private void OnDrawGizmos()
    {
        if (!FightCombatDebugVisuals.Enabled || _actor == null) return;
        if (_actor.Body != null)
            _actor.Body.DrawGizmos(new Color(0.2f, 0.8f, 1f, 0.35f), new Color(1f, 0.2f, 0.2f, 0.9f),
                                   FighterBody.LimbFor(_activeMove), _hitboxActive);
        if (!_hitboxActive || _activeMove?.hits == null || UsesBodies) return;

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
