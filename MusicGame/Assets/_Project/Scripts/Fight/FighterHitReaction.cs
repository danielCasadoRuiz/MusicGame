using UnityEngine;

/// <summary>What a fighter is currently reacting to — see FighterHitReaction. Ordered by priority.</summary>
public enum FighterReactionState
{
    None      = 0,
    BlockStun = 1,
    HitStun   = 2,
    Knockdown = 3,
    Downed    = 4,
    GetUp     = 5,
}

/// <summary>
/// The defender half of the combat runtime: hit stun, block stun and the knockdown flow
///   Knockdown → Downed → GetUp → (CombatIdle)
/// Every reaction interrupts the fighter's current move (FighterMoveController.InterruptMove) and
/// locks its actions/movement until it ends; the animation is only told what to show.
///
/// INTERRUPTION PRIORITY (simple, deliberate):
///   normal move  <  block stun  <  hit stun  <  knockdown flow  <  match end (Victory/Defeat lock)
///   - a hit/block interrupts any move; a stronger hit extends hit stun, never shortens it;
///   - the knockdown flow can't be interrupted: the fighter is invulnerable from Knockdown until
///     GetUp finishes (IsInvulnerable) and hits during it are ignored;
///   - a KO knocks the fighter down and it STAYS Downed (the round/match ends around it).
/// Durations of the knockdown flow come from FightCombatBalanceConfig.
/// </summary>
public class FighterHitReaction : MonoBehaviour
{
    private FighterActor _actor;
    private FighterActor _opponent;
    private FightArenaConfig _arenaConfig;
    private FightCombatBalanceConfig _balance;

    public FighterReactionState State { get; private set; } = FighterReactionState.None;
    public float StateRemaining { get; private set; }

    public bool IsInHitStun => State == FighterReactionState.HitStun || IsInKnockdownFlow;
    public bool IsInBlockStun => State == FighterReactionState.BlockStun;
    public bool IsInKnockdownFlow => State >= FighterReactionState.Knockdown;
    /// <summary>Hits are ignored while FALLING and GETTING UP; while lying DOWNED it can still take a few
    /// LOW ground hits (CanTakeGroundHit — FightHitDispatcher), then it is untouchable until it is up.</summary>
    public bool IsInvulnerable => IsInKnockdownFlow && !CanTakeGroundHit;
    /// <summary>Lying on the floor with ground hits left this knockdown (not a KO'd fighter).</summary>
    public bool CanTakeGroundHit => State == FighterReactionState.Downed && !_stayDown &&
                                    _groundHits < (_balance != null ? _balance.groundHitsPerKnockdown : 2);
    private int _groundHits;

    private bool _stayDown; // KO: remain Downed
    // Tekken-like get-up (FightCombatBalanceConfig.downedWaitsForGetUpInput): lying Downed until Up / Jump is pressed.
    private bool _getUpRequested, _upWasHeld;
    private float _downedElapsed;

    /// <summary>Lying on the floor, able to get up, waiting for the get-up input (Up / Jump, or RequestGetUp).</summary>
    public bool WaitingForGetUp => State == FighterReactionState.Downed && !_stayDown && StateRemaining <= 0f;

    /// <summary>Get up as soon as the minimum lying time allows (the AI's "Up"; also used by tests).</summary>
    public void RequestGetUp()
    {
        if (State == FighterReactionState.Knockdown || State == FighterReactionState.Downed) _getUpRequested = true;
    }
    private bool _altReaction;

    public void Initialize(FighterActor actor, FighterActor opponent, FightArenaConfig arenaConfig)
    {
        _actor       = actor;
        _opponent    = opponent;
        _arenaConfig = arenaConfig;
        _balance     = Resources.Load<AppConfigSO>("AppConfig")?.combatBalance;
    }

    /// <summary>`attackerSide` = the attacker's striking limb (+1 right / −1 left, 0 unknown): every other hit shows the
    /// head snapped away from THAT hand (HitReactionSide, mirrored for the left), the rest the body reaction.</summary>
    public void ApplyHit(float hitStunDuration, float knockbackDistance) => ApplyHit(hitStunDuration, knockbackDistance, 0);

    public void ApplyHit(float hitStunDuration, float knockbackDistance, int attackerSide)
    {
        if (IsInKnockdownFlow) return;
        BeginActionLock();

        // A fresh, stronger hit extends the reaction; a weaker one landing mid-stun never shortens it.
        float remaining = State == FighterReactionState.HitStun ? StateRemaining : 0f;
        Enter(FighterReactionState.HitStun, Mathf.Max(remaining, hitStunDuration));
        // Alternate between the available hit reactions so consecutive hits don't all look the same
        // (HitReactionAlt falls back to HitReaction when the set has no clip for it).
        _altReaction = !_altReaction;
        float shown = float.IsInfinity(hitStunDuration) ? -1f : hitStunDuration;
        if (!_altReaction && attackerSide != 0) _actor.AnimationDriver?.PlaySided(CombatRole.HitReactionSide, attackerSide, shown);
        else _actor.AnimationDriver?.PlayRole(_altReaction ? CombatRole.HitReactionAlt : CombatRole.HitReaction, shown);

        ApplyKnockback(knockbackDistance);
    }

    public void ApplyBlockedHit(float blockStunDuration, float knockbackDistance)
    {
        if (IsInKnockdownFlow) return;
        // Blocked by a Block MOVE: the move keeps running (still guarding), only pushback applies.
        if (_actor.Guard != null && _actor.Guard.IsBlockMoveActive) { ApplyKnockback(knockbackDistance); return; }
        BeginActionLock();

        float remaining = State == FighterReactionState.BlockStun ? StateRemaining : 0f;
        Enter(FighterReactionState.BlockStun, Mathf.Max(remaining, blockStunDuration));
        if (_actor.Guard != null && _actor.Guard.CoverSide != 0) _actor.AnimationDriver?.PlayGuard(0f, _actor.Guard.State, _actor.Guard.CoverSide);
        else _actor.AnimationDriver?.PlayRole(CombatRole.Block);

        ApplyKnockback(knockbackDistance);
    }

    /// <summary>Knocks the fighter down (a knockdown move's hit, or a KO with `stayDown`).</summary>
    public void ApplyKnockdown(float knockbackDistance, bool stayDown = false)
    {
        if (IsInKnockdownFlow && !stayDown) return;
        BeginActionLock();
        _stayDown = stayDown;
        _groundHits = 0;
        _getUpRequested = false;
        _downedElapsed = 0f;
        // A direction already held when falling does not count: the get-up needs a fresh press.
        _upWasHeld = _actor.InputController != null && _actor.InputController.CurrentVertical == FightVerticalDirection.Up;
        float duration = _balance != null ? _balance.knockdownDuration : 0.8f;
        Enter(FighterReactionState.Knockdown, duration);
        _actor.AnimationDriver?.PlayRole(CombatRole.Knockdown, duration);
        ApplyKnockback(knockbackDistance);
    }

    /// <summary>A LOW ground hit on a downed fighter (FightHitDispatcher): counts, keeps it down a bit longer.</summary>
    public void ApplyGroundHit()
    {
        if (State != FighterReactionState.Downed) return;
        _groundHits++;
        StateRemaining += _balance != null ? _balance.groundHitExtraDown : 0.15f;
        _actor.AnimationDriver?.PlayRole(CombatRole.Downed);
    }

    // ── Grapple hooks (FighterGrapple) ───────────────────────────────────────

    /// <summary>Being held by a grab: locked, "held" reaction, no knockback, for `duration` at most
    /// (the grab normally resolves — throw / break — well before).</summary>
    public void HoldGrabbed(float duration)
    {
        if (IsInKnockdownFlow) return;
        BeginActionLock();
        Enter(FighterReactionState.HitStun, duration);
        _actor.AnimationDriver?.PlayRole(CombatRole.HitReaction, -1f);
    }

    /// <summary>Ends a hit/block stun immediately (grab broken / about to be thrown) — never a knockdown.</summary>
    public void ReleaseStun()
    {
        if (State != FighterReactionState.HitStun && State != FighterReactionState.BlockStun) return;
        State = FighterReactionState.None;
        StateRemaining = 0f;
        if (_actor.Movement != null) _actor.Movement.SetLock(false, 1f);
        if (_actor.MoveController != null) _actor.MoveController.IsHitStunned = false;
    }

    /// <summary>Downed fighter escapes ground control early: straight into the get-up.</summary>
    public void ForceGetUp()
    {
        if (_stayDown || (State != FighterReactionState.Knockdown && State != FighterReactionState.Downed)) return;
        float getUp = _balance != null ? _balance.getUpDuration : 0.9f;
        Enter(FighterReactionState.GetUp, getUp);
        _actor.AnimationDriver?.PlayRole(CombatRole.GetUp, getUp);
    }

    private void Enter(FighterReactionState state, float duration)
    {
        State = state;
        StateRemaining = duration;
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

        // Away from the opponent, on the real horizontal combat plane — in a 1v1 arena, whoever hit
        // this fighter IS the opponent, and the OPPONENT's own ForwardXZ already points from them
        // towards us, so it's exactly the "away from attacker" direction we need.
        // Travelled over time by FighterMovement (knockbackSpeed) — a visible push, never a snap.
        Vector3 delta = _opponent.ForwardXZ * distance;
        if (_actor.Movement != null) _actor.Movement.QueueKnockback(delta);
        else _actor.transform.position = FightMovementUtility.ClampXZ(_actor.transform.position, delta, _opponent.transform.position, _arenaConfig);
    }

    private void Update()
    {
        if (State == FighterReactionState.None) return;
        if (float.IsInfinity(StateRemaining)) return; // held until reset (KO)

        if ((State == FighterReactionState.Knockdown || State == FighterReactionState.Downed) && !_stayDown) PollGetUpInput();
        if (State == FighterReactionState.Downed) _downedElapsed += Time.deltaTime;

        StateRemaining -= Time.deltaTime;
        if (StateRemaining > 0f) return;

        switch (State)
        {
            case FighterReactionState.Knockdown:
                Enter(FighterReactionState.Downed, _stayDown ? float.PositiveInfinity : (_balance != null ? _balance.downedDuration : 0.45f));
                _actor.AnimationDriver?.PlayRole(CombatRole.Downed);
                return;
            case FighterReactionState.Downed:
                if (StaysDown()) { StateRemaining = 0f; return; } // lying until the get-up input
                float getUp = _balance != null ? _balance.getUpDuration : 0.9f;
                Enter(FighterReactionState.GetUp, getUp);
                _actor.AnimationDriver?.PlayRole(CombatRole.GetUp, getUp);
                return;
        }

        // HitStun / BlockStun / GetUp finished: unlock exactly once, on the transition frame.
        State = FighterReactionState.None;
        StateRemaining = 0f;
        if (_actor.Movement != null) _actor.Movement.SetLock(false, 1f);
        if (_actor.MoveController != null) _actor.MoveController.IsHitStunned = false;
    }

    // Up pressed (fresh) or the Jump button → get up once the minimum lying time is over.
    private void PollGetUpInput()
    {
        var input = _actor.InputController;
        if (input == null) return;
        bool up = input.CurrentVertical == FightVerticalDirection.Up;
        if (up && !_upWasHeld) _getUpRequested = true;
        _upWasHeld = up;
        if (input.ConsumeJump(0.2f)) _getUpRequested = true; // consumed: no jump right after standing up
    }

    private bool StaysDown()
    {
        if (_balance == null || !_balance.downedWaitsForGetUpInput || _getUpRequested) return false;
        // Nobody to press Up (a disabled AI, no input at all): it gets up by itself, as before.
        bool hasDriver = _actor.AI != null ? _actor.AI.isActiveAndEnabled : _actor.InputController != null;
        if (!hasDriver) return false;
        // No ground hits left: it is untouchable on the floor, so it must not be able to lie there forever.
        if (_groundHits >= _balance.groundHitsPerKnockdown) return false;
        if (_balance.downedMaxDuration > 0f && _downedElapsed >= _balance.downedMaxDuration) return false;
        return true;
    }

    public void ResetForRound()
    {
        State = FighterReactionState.None;
        StateRemaining = 0f;
        _stayDown = false;
        _groundHits = 0;
        _getUpRequested = false;
        _downedElapsed = 0f;
    }
}
