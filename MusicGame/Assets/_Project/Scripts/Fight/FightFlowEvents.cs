/// <summary>Fired by FightFlowController.RequestState whenever Fight's own internal sequence
/// advances — same shape as GameFlowStateChangedEvent, one level down. Every Fight-flow screen
/// controller (OpponentSelectionController, VersusScreenController, RoundIntroController,
/// FightController) reacts to this instead of polling FightFlowController.Instance every frame.</summary>
public struct FightFlowStateChangedEvent
{
    public FightFlowState Previous;
    public FightFlowState Current;
}
