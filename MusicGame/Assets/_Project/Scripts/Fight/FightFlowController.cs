using UnityEngine;

/// <summary>
/// Single source of truth for FightFlowState — same "just a state holder + event publisher" shape
/// as AppFlowController, one level down. Deliberately owns NO timing of its own: each state's
/// actual duration lives wherever the screen that shows THAT state already knows it best
/// (OpponentSelectionController's roulette, VersusScreenController, RoundIntroController's own
/// "ROUND n" + 3-2-1 + FIGHT! sequence) — they call RequestState when THEY decide their moment is
/// over. This is what keeps growing the sequence (RoundEnd, NextRound, KO, MatchWon, MatchLost) a
/// pure addition later: a new controller/state reacting to FightFlowStateChangedEvent and calling
/// RequestState for whatever comes next, never a rewrite of one giant chained coroutine.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController, like FightController/
/// CountdownController) — reacts to GameFlowStateChangedEvent directly to kick the sequence off
/// the instant GameFlowState becomes Fight, restarting fresh (OpponentSelection) every time,
/// including a second Fight entry in the same session.
/// </summary>
public class FightFlowController : MonoBehaviour
{
    public static FightFlowController Instance { get; private set; }

    public FightFlowState CurrentState { get; private set; } = FightFlowState.OpponentSelection;

    private System.Action<GameFlowStateChangedEvent> _onGameFlowChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        _onGameFlowChanged = e =>
        {
            if (e.Current != GameFlowState.Fight) return;

            // Always (re-)announce OpponentSelection here, even on a second Fight entry this
            // session where CurrentState may already equal it — RequestState's own no-op guard
            // (same as AppFlowController's) exists to stop REDUNDANT same-state calls mid-flow,
            // not to skip the one announcement every screen controller needs to actually reset
            // itself for a fresh match.
            Debug.Log("[FightFlowController] Entering Fight — starting at OpponentSelection.");
            var previous = CurrentState;
            CurrentState = FightFlowState.OpponentSelection;
            EventBus.Publish(new FightFlowStateChangedEvent { Previous = previous, Current = CurrentState });
        };
        EventBus.Subscribe(_onGameFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onGameFlowChanged);
    }

    public void RequestState(FightFlowState next)
    {
        if (next == CurrentState) return;
        var previous = CurrentState;
        CurrentState = next;
        Debug.Log($"[FightFlowController] {previous} -> {next}");
        EventBus.Publish(new FightFlowStateChangedEvent { Previous = previous, Current = next });
    }
}
