using UnityEngine;

/// <summary>
/// Single source of truth for "which high-level screen/state is the app in right now" (see
/// GameFlowState). Persistent (DontDestroyOnLoad, created once by AppBootstrap) — screens/services
/// read AppFlowController.Instance.CurrentState (or AppBootstrap.Context.AppFlow) or subscribe to
/// GameFlowStateChangedEvent; nothing else decides this.
///
/// PHASE 1 NOTE: this only tracks/publishes state — it does not yet load/unload scenes or gate
/// GameplayManager/AudioSystemBootstrapper (that begins once Song Analysis is actually extracted
/// from Gameplay's current auto-start). For now CurrentState starts at Boot and is immediately
/// advanced to FlowConfigSO.initialState (Gameplay, by default) in Start(), so the existing
/// single-scene prototype keeps working exactly as before while this scaffolding is introduced. A
/// later phase replaces that with real transitions driven by actual Intro/MainMenu/SongSelection/
/// SongAnalysis screens.
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
        EventBus.Publish(new GameFlowStateChangedEvent { Previous = previous, Current = next });
    }
}
