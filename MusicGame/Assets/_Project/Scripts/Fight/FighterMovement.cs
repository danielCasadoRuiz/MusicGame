using UnityEngine;

/// <summary>
/// The Fighter's REAL locomotion — the concrete thing RealFighterMovementDriver forwards
/// FighterMoveController's lock/multiplier/lunge calls into (see that class's own doc for the
/// interface boundary it implements). Reads Forward/Back ONLY from the already-abstracted
/// FighterInputController (see IFightInputSource's own doc) — never touches keyboard/joystick/UI
/// directly, so a future AIFightInputSource-driven Opponent needs nothing new here beyond wiring
/// its own FighterInputController-equivalent.
///
/// Moves ONLY the gameplay root (the owning FighterActor's own Transform) — see FighterActor's own
/// doc on why VisualRoot never needs to know locomotion happened.
///
/// SPEED: FightArenaConfig.baseMovementSpeed is the only tunable this phase — a single flat number,
/// not yet scaled by FighterStats.Speed (see SpeedMultiplier's own doc: "no vull començar a
/// balancejar FightStats" per this phase's own scope note). Nothing here hardcodes Punch/Kick-
/// specific numbers; those live entirely in FightMoveDefinition/FighterMoveController.
///
/// Only responds to input while FightFlowState is Fighting — matches FighterInputController/
/// FighterMoveController's own gating, so a fighter never drifts during Opponent Selection/Versus/
/// Round Intro/Countdown even if input happens to be held.
/// </summary>
public class FighterMovement : MonoBehaviour
{
    private FighterActor _actor;
    private FighterActor _opponent;
    private FightArenaConfig _config;
    private FighterInputController _input;

    private bool _active;
    private bool _locked;
    private float _multiplier = 1f;
    private float _pendingLunge;

    /// <summary>Not read by anything yet — the seam FighterStats.Speed would multiply once real
    /// combat balancing exists (see class doc). Always 1 this phase.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    public bool IsLocked => _locked;
    public float Multiplier => _multiplier;
    public float LastLungeDistance { get; private set; }

    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

    public void Initialize(FighterActor actor, FighterActor opponent, FightArenaConfig config, FighterInputController input)
    {
        _actor    = actor;
        _opponent = opponent;
        _config   = config;
        _input    = input;
    }

    /// <summary>Called by RealFighterMovementDriver.SetMovementLock — see FightMoveDefinition.
    /// movementLocked/movementMultiplier's own doc.</summary>
    public void SetLock(bool locked, float multiplier)
    {
        _locked     = locked;
        _multiplier = multiplier;
    }

    /// <summary>Called by RealFighterMovementDriver.ApplyLunge — accumulates in case more than one
    /// arrives the same frame (shouldn't happen with today's data, but stays correct if it ever
    /// does). Consumed (and clamped against bounds/separation) on the very next Update.</summary>
    public void QueueLunge(float distance) => _pendingLunge += distance;

    private void OnEnable()
    {
        _onFightFlowChanged = e => _active = e.Current == FightFlowState.Fighting;
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFightFlowChanged);
    }

    private void Update()
    {
        if (!_active || _input == null || _actor == null) return;

        float speed = (_config != null ? _config.baseMovementSpeed : 4f) * SpeedMultiplier;

        float dirSign = 0f;
        if (!_locked)
        {
            switch (_input.CurrentHorizontal)
            {
                case FightHorizontalDirection.Forward: dirSign = _actor.FacingRight ?  1f : -1f; break;
                case FightHorizontalDirection.Back:    dirSign = _actor.FacingRight ? -1f :  1f; break;
            }
        }

        float delta = dirSign * speed * _multiplier * Time.deltaTime;

        if (_pendingLunge != 0f)
        {
            float lungeSign = _actor.FacingRight ? 1f : -1f;
            delta += lungeSign * _pendingLunge;
            LastLungeDistance = _pendingLunge;
            _pendingLunge = 0f;
        }

        if (Mathf.Approximately(delta, 0f)) return;

        ApplyClampedDelta(delta);
    }

    private void ApplyClampedDelta(float delta)
    {
        float minX          = _config != null ? _config.minBoundX : -4f;
        float maxX           = _config != null ? _config.maxBoundX : 4f;
        float minSeparation = _config != null ? _config.minimumFighterSeparation : 1f;

        float currentX = _actor.transform.position.x;
        float targetX  = Mathf.Clamp(currentX + delta, minX, maxX);

        if (_opponent != null)
        {
            float opponentX = _opponent.transform.position.x;
            if (targetX > currentX && opponentX > currentX)
                targetX = Mathf.Min(targetX, opponentX - minSeparation);
            else if (targetX < currentX && opponentX < currentX)
                targetX = Mathf.Max(targetX, opponentX + minSeparation);
        }

        var pos = _actor.transform.position;
        pos.x = targetX;
        _actor.transform.position = pos;
    }
}
