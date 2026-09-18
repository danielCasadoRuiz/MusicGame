/// <summary>
/// Placeholder for the future global Player Level system (harder Runner, stronger rivals, more
/// powerful avatars, more effects, possibly new mechanics — none of that exists yet). Passed
/// through FighterStatsBuilder.Build as an optional parameter, already accepted, INTENTIONALLY
/// unused today (no scaling applied) — whether Player Level ends up scaling Fight stats directly,
/// or only affects difficulty/AI/unlocks/visuals instead, is a decision for later; wiring the
/// parameter in now means either answer is a pure addition to this layer, never a reshape of it.
/// </summary>
public readonly struct LevelContext
{
    public readonly int PlayerLevel;

    public LevelContext(int playerLevel) => PlayerLevel = playerLevel;
}
