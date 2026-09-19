/// <summary>
/// Fight's own touch-input bridge — the exact same "shared static state, written by a UI
/// component, read and cleared by whoever owns real input reading" pattern as Player/
/// TouchInputState (Runner's own), just for Fight's controls (movement joystick, Punch/Kick
/// buttons) instead of Runner's (strafe/surge joystick, jump button). Kept as its own, separate
/// static class rather than adding fields to TouchInputState — the two modes never run at once,
/// but giving Fight's inputs Runner-flavoured names (Lateral/Surging/JumpRequested) would be far
/// more confusing than the tiny duplication of "a few static fields" costs.
/// </summary>
public static class FightTouchInputState
{
    /// <summary>-1..1, screen-space (NOT facing-relative — see FightDirectionResolver).</summary>
    public static float Horizontal;
    /// <summary>-1..1, screen-space.</summary>
    public static float Vertical;
    public static bool PunchRequested;
    public static bool KickRequested;
}
