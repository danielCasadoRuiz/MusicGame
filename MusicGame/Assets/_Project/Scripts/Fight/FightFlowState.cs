/// <summary>
/// Fight's OWN internal sequence, entirely separate from GameFlowState (which only ever holds the
/// single macro-state "Fight" for the whole mode — see GameFlowState.cs). FightFlowController is
/// the single source of truth for this; everything else reacts to FightFlowStateChangedEvent.
///
/// Deliberately just the pre-combat sequence for this phase — RoundEnd/NextRound/KO/MatchWon/
/// MatchLost are the obvious next values once real combat exists, added the same way as any of
/// these (a new enum value + whichever controller drives it calling
/// FightFlowController.Instance.RequestState), never a reason to redesign this enum or the
/// controllers that already react to it.
/// </summary>
public enum FightFlowState
{
    OpponentSelection,
    VersusIntro,
    RoundIntro,
    Countdown,
    Fighting,
}
