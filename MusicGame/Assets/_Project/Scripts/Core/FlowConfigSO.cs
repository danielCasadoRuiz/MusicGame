using UnityEngine;

/// <summary>
/// AppFlowController's own editable config — the one real config value the flow module has right
/// now (which state to advance to right after Boot). Kept as a proper config asset rather than a
/// hardcoded constant specifically so it's a dev-tunable value: once real Intro/MainMenu/
/// SongSelection screens exist, flip this from Gameplay to Intro without touching code.
/// </summary>
[CreateAssetMenu(fileName = "FlowConfig", menuName = "MusicGame/App/Flow Config")]
public class FlowConfigSO : ScriptableObject
{
    public GameFlowState initialState = GameFlowState.Gameplay;
}
