/// <summary>
/// One discrete, buffered input — a button press, with whatever relative direction was held at
/// that exact moment (Neutral if none). Only button presses are buffered (bare directions never
/// are — every combo step in FightComboDefinition pairs a button with a direction, matching how
/// real fighting-game notation like "Forward + A" always reads).
/// </summary>
public readonly struct FightInputEvent
{
    public readonly FightButton Button;
    public readonly FightHorizontalDirection Horizontal;
    public readonly FightVerticalDirection Vertical;
    public readonly float Time;

    public FightInputEvent(FightButton button, FightHorizontalDirection horizontal, FightVerticalDirection vertical, float time)
    {
        Button = button;
        Horizontal = horizontal;
        Vertical = vertical;
        Time = time;
    }

    public override string ToString()
    {
        string dir = Horizontal != FightHorizontalDirection.Neutral ? Horizontal + "+"
                   : Vertical   != FightVerticalDirection.Neutral   ? Vertical + "+"
                   : "";
        return $"{dir}{Button}";
    }
}
