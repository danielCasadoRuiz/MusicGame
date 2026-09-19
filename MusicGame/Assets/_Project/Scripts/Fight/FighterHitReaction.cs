using UnityEngine;

/// <summary>
/// Player and Opponent share this EXACT component too — whichever fighter gets hit reacts the same
/// way regardless of side, matching FighterHealth's own "no separate system per side" rule. Owns
/// the hit-stun timer and the one-shot knockback displacement; FighterActor exposes IsInHitStun so
/// its own facing-lock (see FighterActor.Update) freezes during a reaction without any per-move
/// special-casing (task's own explicit ask).
///
/// KNOCKBACK is an immediate, deterministic one-shot displacement — exactly like a move's own lunge
/// (see FightMoveDefinition.lungeDistance) — reusing FightMovementUtility's arena-bounds/minimum-
/// separation clamp so a knockback can never push a fighter out of bounds or through the other one.
/// Deliberately NOT Rigidbody physics (see this phase's own scope note: "vull moviment determinista").
///
/// INTERRUPTING THE MOVE that was running when the hit landed goes through FighterMoveController's
/// own explicit InterruptMove() API — this class never reaches into FighterMoveController's private
/// state.
/// </summary>
public class FighterHitReaction : MonoBehaviour
{
    private FighterActor _actor;
    private FighterActor _opponent;
    private FightArenaConfig _arenaConfig;

    public bool IsInHitStun { get; private set; }
    private float _hitStunRemaining;

    public void Initialize(FighterActor actor, FighterActor opponent, FightArenaConfig arenaConfig)
    {
        _actor       = actor;
        _opponent    = opponent;
        _arenaConfig = arenaConfig;
    }

    /// <summary>Called by whatever lands the hit (see FighterAttack) — never damage; FighterHealth
    /// owns that separately (see class doc on responsibility separation).</summary>
    public void ApplyHit(float hitStunDuration, float knockbackDistance)
    {
        _actor.MoveController?.InterruptMove();
        if (_actor.MoveController != null) _actor.MoveController.IsHitStunned = true;

        // A fresh, stronger hit extends the reaction; a weaker one landing mid-stun never shortens
        // it — simple and safe for V1 (no combo/stun-scaling rules exist yet).
        _hitStunRemaining = Mathf.Max(_hitStunRemaining, hitStunDuration);
        IsInHitStun = true;

        if (_actor.Movement != null) _actor.Movement.SetLock(true, 0f);

        ApplyKnockback(knockbackDistance);
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
        if (!IsInHitStun) return;

        _hitStunRemaining -= Time.deltaTime;
        if (_hitStunRemaining > 0f) return;

        IsInHitStun = false;
        if (_actor.Movement != null) _actor.Movement.SetLock(false, 1f);
        if (_actor.MoveController != null) _actor.MoveController.IsHitStunned = false;
    }

    /// <summary>Explicit API for FightMatchController's between-rounds reset (via FighterActor.
    /// ResetForRound) — clears even an INFINITE hit stun (see FighterHealth's own doc on reusing
    /// this class for KO enforcement). Does not touch Movement/MoveController locks itself — each of
    /// those clears its own lock via its own ResetForRound, called separately by FighterActor.</summary>
    public void ResetForRound()
    {
        IsInHitStun = false;
        _hitStunRemaining = 0f;
    }
}
