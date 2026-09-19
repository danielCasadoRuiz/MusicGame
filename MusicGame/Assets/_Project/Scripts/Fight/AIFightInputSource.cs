/// <summary>
/// The Opponent's "hands" — implements IFightInputSource EXACTLY like HumanFightInputSource does,
/// just written by FighterAI's decisions instead of a keyboard/touch. This is the ONLY surface
/// FighterAI ever touches to make the Fighter do anything — it never reaches into
/// FighterMoveController/FighterAttack/FighterMovement/Health directly (see FighterAI's own doc on
/// why: the Opponent plays through the EXACT same Input -> FighterInputController -> InputBuffer/
/// ComboRecognizer -> Move System pipeline the Player does).
///
/// Same edge-triggered press semantics as HumanFightInputSource: RequestPunch/RequestKick set a
/// flag that Tick() reads ONCE and clears, so a press is never silently "held" across frames or
/// double-consumed. Held direction (SetDirection) persists until FighterAI changes it — exactly
/// like a human holding a key.
/// </summary>
public class AIFightInputSource : IFightInputSource
{
    public float Horizontal { get; private set; }
    public float Vertical { get; private set; }
    public bool PunchPressed { get; private set; }
    public bool KickPressed { get; private set; }

    private float _desiredHorizontal;
    private float _desiredVertical;
    private bool _punchRequested;
    private bool _kickRequested;

    /// <summary>ABSOLUTE/screen-space, exactly like a real IFightInputSource — see that interface's
    /// own doc. FighterAI computes this from real world positions (e.g. Mathf.Sign(opponentX -
    /// selfX) to move "towards" the opponent) rather than thinking in Forward/Back itself; the
    /// facing-relative conversion still happens in the SAME FightDirectionResolver every human
    /// press goes through.</summary>
    public void SetDirection(float horizontal, float vertical)
    {
        _desiredHorizontal = horizontal;
        _desiredVertical = vertical;
    }

    public void RequestPunch() => _punchRequested = true;
    public void RequestKick() => _kickRequested = true;

    public void Tick()
    {
        Horizontal = _desiredHorizontal;
        Vertical = _desiredVertical;
        PunchPressed = _punchRequested;
        KickPressed = _kickRequested;
        _punchRequested = false;
        _kickRequested = false;
    }
}
