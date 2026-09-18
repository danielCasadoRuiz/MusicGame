/// <summary>
/// Fired by AppFlowController whenever CurrentState actually changes — screens/services subscribe
/// to this instead of polling AppFlowController.Instance.CurrentState every frame.
/// </summary>
public struct GameFlowStateChangedEvent
{
    public GameFlowState Previous;
    public GameFlowState Current;
}
