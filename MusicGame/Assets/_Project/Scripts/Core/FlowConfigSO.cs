using UnityEngine;

/// <summary>
/// AppFlowController's own editable config — the one real config value the flow module has right
/// now (which state to advance to right after Boot). Kept as a proper config asset rather than a
/// hardcoded constant specifically so it's a dev-tunable value — e.g. FlowConfig.asset currently sets
/// this to Intro (the real app entry point, now that Intro/MainMenu exist), but a developer wanting
/// to jump straight into Gameplay for quick iteration can flip it locally without touching code.
/// </summary>
[CreateAssetMenu(fileName = "FlowConfig", menuName = "MusicGame/App/Flow Config")]
public class FlowConfigSO : ScriptableObject
{
    public GameFlowState initialState = GameFlowState.Gameplay;
}
