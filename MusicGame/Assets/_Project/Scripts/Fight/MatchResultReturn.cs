/// <summary>
/// Where Song Selection's BACK goes. Song Selection opened from the Match Result (SELECT SONG) returns to
/// that same result screen; from anywhere else it returns to the Main Menu.
/// The return re-enters GameFlowState.Fight directly at the result state (FightFlowController): no roulette,
/// no match, no farewell, no rewards or campaign advance — MatchResultController only re-presents the result
/// it cached when SELECT SONG was pressed. Pressing PLAY in Song Selection drops the return (a new song is
/// being played; the result has been left for good).
/// </summary>
public static class MatchResultReturn
{
    /// <summary>Song Selection was opened from the Match Result and BACK should return there.</summary>
    public static bool Armed { get; private set; }
    // BACK pressed: the next Fight entry resumes the result instead of starting a match.
    private static bool s_resumeRequested;

    public static void Arm() { Armed = true; s_resumeRequested = false; }
    public static void Disarm() { Armed = false; s_resumeRequested = false; }

    /// <summary>Song Selection BACK: to the result it came from, otherwise to the Main Menu.</summary>
    public static void Back()
    {
        if (!Armed)
        {
            AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
            return;
        }
        Armed = false;
        s_resumeRequested = true;
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.Fight);
    }

    /// <summary>FightFlowController on a Fight entry: true (once) when that entry is a return to the result.</summary>
    public static bool ConsumeResume()
    {
        bool r = s_resumeRequested;
        s_resumeRequested = false;
        return r;
    }
}
