/// <summary>
/// Fight's OWN internal sequence, entirely separate from GameFlowState (which only ever holds the
/// single macro-state "Fight" for the whole mode — see GameFlowState.cs). FightFlowController is
/// the single source of truth for this; everything else reacts to FightFlowStateChangedEvent.
///
/// RoundIntro/Countdown/Fighting REPEAT, once per round — FightMatchController is the only thing
/// that decides whether the next transition out of RoundEnd goes back to RoundIntro (match
/// continues) or forward to MatchWon/MatchLost (see that class's own doc on why this branch does
/// NOT live in any screen controller). No separate per-round enum values exist — "which round" is
/// plain data (FightMatchController.CurrentRound / RoundIntroController.SetRound), not flow state.
/// </summary>
public enum FightFlowState
{
    OpponentSelection,
    VersusIntro,
    RoundIntro,
    Countdown,
    Fighting,
    RoundEnd,
    MatchWon,
    MatchLost,
}
