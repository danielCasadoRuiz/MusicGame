/// <summary>One jump per landing, with a short grace period for involuntary ground loss.</summary>
public struct RunnerJumpGrace
{
    private bool _available;
    private float _remaining;

    public bool TryJump(bool grounded, float verticalVelocity, bool pressed, float deltaTime, float graceSeconds)
    {
        // CharacterController can still report the previous Move's ground contact just after
        // takeoff. Rising from a jump must never re-arm the jump during that stale contact.
        bool supported = grounded && verticalVelocity <= 0f;
        if (supported)
        {
            _available = true;
            _remaining = System.Math.Max(0f, graceSeconds);
        }
        else
            _remaining = System.Math.Max(0f, _remaining - System.Math.Max(0f, deltaTime));

        if (!pressed || !_available || (!supported && _remaining <= 0f)) return false;
        _available = false;
        _remaining = 0f;
        return true;
    }

    public void Reset() => this = default;
}
