/// <summary>
/// High-level application screens/states — see AppFlowController, the single owner of "which one
/// are we in right now". Intentionally just an enum for now: several of these currently share the
/// one existing Unity scene and are purely logical/UI states until a real Frontend/Gameplay/Fight
/// scene split happens in a later phase.
/// </summary>
public enum GameFlowState
{
    Boot,
    Intro,
    MainMenu,
    SongSelection,
    SongAnalysis,
    Gameplay,
    Results,
    Fight,
}
