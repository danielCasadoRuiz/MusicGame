/// <summary>The Fighter's own move-execution phase — see FighterMoveController's own doc for the
/// full state machine and queue/cancel policy.</summary>
public enum FighterMoveState
{
    Idle,
    Startup,
    Active,
    Recovery,
}
