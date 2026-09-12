/// <summary>
/// Structural/macro musical moments — evolution and turning points, as opposed to the
/// instant-rhythm Micro layer (Kick/Snare/HiHat/Onset/Beat, see RingType).
/// </summary>
public enum MacroEventType
{
    Impact,       // a strong hit or sudden intensity change (DynamicsAnalyzer)
    Drop,         // a release/EDM-style drop (DynamicsAnalyzer)
    BuildupStart, // where a buildup leading to an Impact begins (marker only, no collectible)
    Peak,         // the song's single loudest moment — usually tags an existing Impact/Drop
                  // as the climax instead of standing alone (see GameplayTimeline.BuildMacroEvents)
}
