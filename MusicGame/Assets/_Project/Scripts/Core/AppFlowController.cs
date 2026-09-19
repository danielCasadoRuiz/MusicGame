using UnityEngine;

/// <summary>
/// Single source of truth for "which high-level screen/state is the app in right now" (see
/// GameFlowState). Persistent (DontDestroyOnLoad, created once by AppBootstrap) — screens/services
/// read AppFlowController.Instance.CurrentState (or AppBootstrap.Context.AppFlow) or subscribe to
/// GameFlowStateChangedEvent; nothing else decides this.
///
/// This class only tracks/publishes state — it never loads/unloads scenes itself (SceneFlowController
/// reacts to GameFlowStateChangedEvent for that) and never gates GameplayManager/
/// AudioSystemBootstrapper directly (they react to the same event/notifications independently).
/// CurrentState starts at Boot and is immediately advanced to FlowConfigSO.initialState in Start() —
/// currently Intro, the app's real entry point now that Intro/MainMenu exist (see
/// IntroScreenController/MainMenuController).
/// </summary>
public class AppFlowController : MonoBehaviour, IAppModule, IConfigurableModule<FlowConfigSO>
{
    public static AppFlowController Instance { get; private set; }

    public GameFlowState CurrentState { get; private set; } = GameFlowState.Boot;

    private FlowConfigSO _config;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Configure(FlowConfigSO config) => _config = config;

    void IAppModule.Initialize(AppContext context) { /* no cross-module wiring needed yet */ }
    void IAppModule.Shutdown() { }

    private void Start()
    {
        // See PHASE 1 NOTE above.
        RequestState(_config != null ? _config.initialState : GameFlowState.Gameplay);
    }

    public void RequestState(GameFlowState next)
    {
        if (next == CurrentState) return;
        var previous = CurrentState;
        CurrentState = next;
        // Same "log every real transition" convention FightFlowController already uses one level
        // down — without this, "which code actually requested SongAnalysis" was unanswerable from
        // the Console alone, which is exactly what made a report like "Replay Song still shows
        // Analysis" impossible to confirm/deny from logs.
        Debug.Log($"[AppFlowController] {previous} -> {next}");
        EventBus.Publish(new GameFlowStateChangedEvent { Previous = previous, Current = next });
    }
}
