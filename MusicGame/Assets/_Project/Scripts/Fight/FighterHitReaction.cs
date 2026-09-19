using UnityEngine;

/// <summary>
/// Player and Opponent share this EXACT component too — whichever fighter gets hit reacts the same
/// way regardless of side, matching FighterHealth's own "no separate system per side" rule. Owns
/// BOTH reaction timers — hit stun (ApplyHit) and block stun (ApplyBlockedHit, see FighterGuard/
/// FightHitDispatcher's own doc on how a hit becomes one or the other) — plus the one-shot knockback
/// displacement shared by both. FighterActor exposes IsInHitStun/IsInBlockStun so its own facing-lock
/// (see FighterActor.Update) freezes during either without any per-move special-casing (task's own
/// explicit ask).
///
/// KNOCKBACK is an immediate, deterministic one-shot displacement — exactly like a move's own lunge
/// (see FightMoveDefinition.lungeDistance) — reusing FightMovementUtility's arena-bounds/minimum-
/// separation clamp so a knockback can never push a fighter out of bounds or through the other one.
/// Deliberately NOT Rigidbody physics (see this phase's own scope note: "vull moviment determinista").
///
/// INTERRUPTING THE MOVE that was running when the hit landed goes through FighterMoveController's
/// own explicit InterruptMove() API — this class never reaches into FighterMoveController's private
/// state. Both ApplyHit and ApplyBlockedHit reuse MoveController.IsHitStunned as the single "cannot
/// start a new move" gate — a block also stops you from acting, so there is no separate
/// "IsBlockStunned" gate on FighterMoveController.
/// </summary>
public class FighterHitReaction : MonoBehaviour
{
    private FighterActor _actor;
    private FighterActor _opponent;
    private FightArenaConfig _arenaConfig;

    public bool IsInHitStun { get; private set; }
    private float _hitStunRemaining;

    public bool IsInBlockStun { get; private set; }
    private float _blockStunRemaining;

    public void Initialize(FighterActor actor, FighterActor opponent, FightArenaConfig arenaConfig)
    {
        _actor       = actor;
        _opponent    = opponent;
        _arenaConfig = arenaConfig;
    }

    /// <summary>Called by whatever lands an UNBLOCKED hit (see FightHitDispatcher) — never damage;
    /// FighterHealth owns that separately (see class doc on responsibility separation).</summary>
    public void ApplyHit(float hitStunDuration, float knockbackDistance)
    {
        BeginActionLock();

        // A fresh, stronger hit extends the reaction; a weaker one landing mid-stun never shortens
        // it — simple and safe for V1 (no combo/stun-scaling rules exist yet).
        _hitStunRemaining = Mathf.Max(_hitStunRemaining, hitStunDuration);
        IsInHitStun = true;

        ApplyKnockback(knockbackDistance);
    }

    /// <summary>Called by whatever lands a BLOCKED hit (see FightHitDispatcher) — same shape as
    /// ApplyHit, distinct timer/flag so FightDebugHUD and future animation can always tell "hit" and
    /// "blocked" apart.</summary>
    public void ApplyBlockedHit(float blockStunDuration, float knockbackDistance)
    {
        BeginActionLock();

        _blockStunRemaining = Mathf.Max(_blockStunRemaining, blockStunDuration);
        IsInBlockStun = true;

        ApplyKnockback(knockbackDistance);
    }

    private void BeginActionLock()
    {
        _actor.MoveController?.InterruptMove();
        if (_actor.MoveController != null) _actor.MoveController.IsHitStunned = true;
        if (_actor.Movement != null) _actor.Movement.SetLock(true, 0f);
    }

    private void ApplyKnockback(float distance)
    {
        if (Mathf.Approximately(distance, 0f) || _opponent == null) return;

        float currentX  = _actor.transform.position.x;
        float opponentX = _opponent.transform.position.x;
        // Away from the opponent — in a 1v1 arena, whoever hit this fighter IS the opponent (see
        // class doc: only one possible attacker/defender pair exists this phase).
        float sign = currentX >= opponentX ? 1f : -1f;

        float targetX = FightMovementUtility.ClampDeltaX(currentX, sign * distance, opponentX, _arenaConfig);
        var pos = _actor.transform.position;
        pos.x = targetX;
        _actor.transform.position = pos;
    }

    private void Update()
    {
        bool wasLocked = IsInHitStun || IsInBlockStun;
        if (!wasLocked) return;

        if (IsInHitStun)
        {
            _hitStunRemaining -= Time.deltaTime;
            if (_hitStunRemaining <= 0f) IsInHitStun = false;
        }
        if (IsInBlockStun)
        {
            _blockStunRemaining -= Time.deltaTime;
            if (_blockStunRemaining <= 0f) IsInBlockStun = false;
        }

        // Unlock exactly once, on the transition frame — never re-asserted every idle frame after,
        // which would otherwise fight a move's OWN SetMovementLock(true, ...) call if this Update
        // happened to run after FighterMoveController's in the same frame (see class doc).
        if (!IsInHitStun && !IsInBlockStun)
        {
            if (_actor.Movement != null) _actor.Movement.SetLock(false, 1f);
            if (_actor.MoveController != null) _actor.MoveController.IsHitStunned = false;
        }
    }

    /// <summary>Explicit API for FightMatchController's between-rounds reset (via FighterActor.
    /// ResetForRound) — clears even an INFINITE hit stun (see FighterHealth's own doc on reusing
    /// this class for KO enforcement). Does not touch Movement/MoveController locks itself — each of
    /// those clears its own lock via its own ResetForRound, called separately by FighterActor.</summary>
    public void ResetForRound()
    {
        IsInHitStun = false;
        _hitStunRemaining = 0f;
        IsInBlockStun = false;
        _blockStunRemaining = 0f;
    }
}
