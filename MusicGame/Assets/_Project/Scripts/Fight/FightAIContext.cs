using UnityEngine;

/// <summary>
/// Everything FighterAI is allowed to know about the current situation, captured ONCE per decision
/// tick (see FighterAI's own doc on reactionTime pacing — this is deliberately NOT rebuilt every
/// frame) so every scoring function reads from the SAME consistent snapshot instead of each
/// re-querying FighterActor/FightMatchController independently. Read-only from the outside;
/// FightAIContext.Capture is the only place that ever builds one.
///
/// NO CHEATING (see this phase's own explicit "no fer trampes" checklist): this only ever reads
/// PUBLIC, already-resolved state (health%, posture, current move/phase, positions) — the exact same
/// information a human player can see on screen. It never inspects the Player's buffered inputs,
/// upcoming combo intentions, or anything not yet resolved into visible game state.
/// </summary>
public class FightAIContext
{
    // ── Self ──────────────────────────────────────────────────────────────────
    public float SelfHealthPercent;
    public FighterPosture SelfPosture;
    public FighterMovementState SelfMovementState;
    public FighterGuardState SelfGuardState;
    public bool SelfHitStun;
    public bool SelfBlockStun;
    public bool SelfKO;
    public bool SelfCanAct;
    public FightMoveDefinition SelfCurrentMove;
    public FighterMoveState SelfMovePhase;

    // ── Opponent (i.e. whoever THIS fighter is fighting — the Player, from the AI's perspective) ──
    public float OpponentHealthPercent;
    public FighterPosture OpponentPosture;
    public FighterMovementState OpponentMovementState;
    public FighterGuardState OpponentGuardState;
    public bool OpponentHitStun;
    public bool OpponentBlockStun;
    public bool OpponentKO;
    public FightMoveDefinition OpponentCurrentMove;
    public FighterMoveState OpponentMovePhase;
    public float OpponentMovePhaseProgress01;

    // ── Combat ────────────────────────────────────────────────────────────────
    public float Distance;
    public bool SelfFacingRight;
    /// <summary>Arena room between self and the bound in front of/behind self's OWN facing.</summary>
    public float SpaceAhead;
    public float SpaceBehind;
    public float RoundTimeRemaining;
    public int SelfRoundsWon;
    public int OpponentRoundsWon;

    /// <summary>The nearest projectile NOT owned by self (i.e. a real incoming threat) — null if
    /// none exists. See FighterAI's own doc on why this is enough (no physical prediction needed).</summary>
    public FightProjectile IncomingProjectile;
    public float IncomingProjectileDistance;

    public static FightAIContext Capture(FighterActor self, FighterActor opponent, FightArenaConfig arenaConfig, FightMatchController match)
    {
        var ctx = new FightAIContext();
        if (self == null || opponent == null) return ctx;

        ctx.SelfHealthPercent = HealthPercent(self);
        ctx.SelfPosture = self.Posture;
        ctx.SelfMovementState = self.MovementState;
        ctx.SelfGuardState = self.Guard != null ? self.Guard.State : FighterGuardState.None;
        ctx.SelfHitStun = self.HitReaction != null && self.HitReaction.IsInHitStun;
        ctx.SelfBlockStun = self.HitReaction != null && self.HitReaction.IsInBlockStun;
        ctx.SelfKO = self.Health != null && self.Health.IsKO;
        ctx.SelfCanAct = !ctx.SelfKO && !ctx.SelfHitStun && !ctx.SelfBlockStun &&
                         (self.MoveController == null || self.MoveController.CanAttack);
        ctx.SelfCurrentMove = self.MoveController != null ? self.MoveController.CurrentMove : null;
        ctx.SelfMovePhase = self.MoveController != null ? self.MoveController.CurrentPhase : FighterMoveState.Idle;

        ctx.OpponentHealthPercent = HealthPercent(opponent);
        ctx.OpponentPosture = opponent.Posture;
        ctx.OpponentMovementState = opponent.MovementState;
        ctx.OpponentGuardState = opponent.Guard != null ? opponent.Guard.State : FighterGuardState.None;
        ctx.OpponentHitStun = opponent.HitReaction != null && opponent.HitReaction.IsInHitStun;
        ctx.OpponentBlockStun = opponent.HitReaction != null && opponent.HitReaction.IsInBlockStun;
        ctx.OpponentKO = opponent.Health != null && opponent.Health.IsKO;
        ctx.OpponentCurrentMove = opponent.MoveController != null ? opponent.MoveController.CurrentMove : null;
        ctx.OpponentMovePhase = opponent.MoveController != null ? opponent.MoveController.CurrentPhase : FighterMoveState.Idle;
        ctx.OpponentMovePhaseProgress01 = opponent.MoveController != null ? opponent.MoveController.PhaseProgress01 : 0f;

        ctx.Distance = self.DistanceToOpponent;
        ctx.SelfFacingRight = self.FacingRight;

        float minX = arenaConfig != null ? arenaConfig.minBoundX : -4f;
        float maxX = arenaConfig != null ? arenaConfig.maxBoundX : 4f;
        float selfX = self.transform.position.x;
        ctx.SpaceAhead  = Mathf.Max(0f, ctx.SelfFacingRight ? maxX - selfX : selfX - minX);
        ctx.SpaceBehind = Mathf.Max(0f, ctx.SelfFacingRight ? selfX - minX : maxX - selfX);

        if (match != null)
        {
            ctx.RoundTimeRemaining = match.RoundTimeRemaining;
            bool selfIsPlayer = self.Side == FighterSide.Player;
            ctx.SelfRoundsWon     = selfIsPlayer ? match.PlayerRoundsWon   : match.OpponentRoundsWon;
            ctx.OpponentRoundsWon = selfIsPlayer ? match.OpponentRoundsWon : match.PlayerRoundsWon;
        }

        FindIncomingProjectile(self, ctx);

        return ctx;
    }

    private static void FindIncomingProjectile(FighterActor self, FightAIContext ctx)
    {
        float best = float.MaxValue;
        foreach (var projectile in Object.FindObjectsByType<FightProjectile>(FindObjectsSortMode.None))
        {
            if (projectile == null || projectile.Owner == self) continue; // only real incoming threats, never our own
            float dist = Mathf.Abs(projectile.transform.position.x - self.transform.position.x);
            if (dist < best)
            {
                best = dist;
                ctx.IncomingProjectile = projectile;
                ctx.IncomingProjectileDistance = dist;
            }
        }
    }

    private static float HealthPercent(FighterActor actor) =>
        actor != null && actor.Health != null && actor.Health.MaxHealth > 0f
            ? actor.Health.CurrentHealth / actor.Health.MaxHealth
            : 0f;
}
