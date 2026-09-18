/// <summary>
/// One of the additively-loaded "Mode Scenes" — see SceneFlowController. Deliberately separate
/// from GameFlowState: several GameFlowState values share the SAME Mode Scene (Intro/MainMenu/
/// SongSelection/SongAnalysis all live in Frontend; Gameplay/Results both live in Runner) —
/// GameFlowState is the fine-grained UI/logic state, GameMode is "which physical scene hosts it".
/// </summary>
public enum GameMode
{
    Frontend,
    Runner,
    Fight,
}
