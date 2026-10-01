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
    /// <summary>Monotonic per-fighter press number — identifies ONE physical input, so a press can
    /// never be counted twice by the combo-string recognizer (see FightComboRecognizer).</summary>
    public readonly int Sequence;

    public FightInputEvent(FightButton button, FightHorizontalDirection horizontal, FightVerticalDirection vertical, float time, int sequence = 0)
    {
        Button = button;
        Horizontal = horizontal;
        Vertical = vertical;
        Time = time;
        Sequence = sequence;
    }

    public override string ToString()
    {
        string dir = (Vertical != FightVerticalDirection.Neutral ? Vertical + "+" : "")
                   + (Horizontal != FightHorizontalDirection.Neutral ? Horizontal + "+" : "");
        return Button == FightButton.None ? dir.TrimEnd('+') : $"{dir}{Button}";
    }
}
