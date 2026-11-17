using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns loading/unloading of the additive Mode Scenes (Frontend/Runner/Fight — see GameMode) and of
/// the always-loaded UI Scene. Persistent (IAppModule, constructed by AppBootstrap on the same
/// [App Bootstrap] GameObject as GameSession/AppFlowController/ThemeManager) — the single place that
/// loads MODE scenes ("AppFlow decideix l'estat, SceneFlow executa les càrregues"). Nested arena
/// content scenes are owned by ArenaEnvironmentLoader; their cleanup is awaited before Fight unloads.
///
/// Target runtime layout: Core (this object's home, whichever scene happens to load first — see
/// AppBootstrap's own doc) + UI (loaded once here, right after boot, never unloaded) + exactly one
/// Mode Scene. Outgoing Runner/Fight scenes are released before loading their replacement to
/// avoid overlapping avatar and arena allocations. Lightweight Frontend may overlap briefly.
/// Every GameFlowState maps to exactly one Mode Scene (see ModeFor) — Frontend and Runner
/// and Fight genuinely REPLACE each other now, they don't coexist as overlays.
///
/// No `.Instance` singleton here — reached only via AppContext.SceneFlow, per this project's "new
/// modules don't repeat the .Instance pattern" rule.
/// </summary>
public class SceneFlowController : MonoBehaviour, IAppModule
{
    private const string UiSceneName = "UI";

    private static readonly Dictionary<GameMode, string> SceneNames = new()
    {
        { GameMode.Frontend, "Frontend" },
        { GameMode.Runner,   "Runner"   },
        { GameMode.Fight,    "Fight"    },
    };

    /// <summary>Null until the first LoadMode call completes (or NotifyCurrentModeAlreadyLoaded is
    /// called for a Mode Scene opened directly).</summary>
    public GameMode? CurrentMode { get; private set; }

    public bool UiSceneLoaded { get; private set; }

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;
    private bool _modeTransitionInProgress;
    public bool IsModeTransitionInProgress => _modeTransitionInProgress;
    private SceneTransitionFrame _transitionFrame;
    public bool IsTransitionFrameVisible => _transitionFrame != null && _transitionFrame.IsVisible;
    public void ReleaseTransitionFrame() => _transitionFrame?.Release();
    public void RevealMenuOverTransitionFrame() => _transitionFrame?.RevealMenu();

    void IAppModule.Initialize(AppContext context)
    {
        StartCoroutine(EnsureUiSceneLoaded());

        // "AppFlow decideix l'estat, SceneFlow executa les càrregues" — this is the one place that
        // translates a GameFlowState transition into an actual scene load/unload. Every state maps
        // to exactly one Mode Scene (ModeFor) — Frontend/Runner/Fight fully replace each other via
        // LoadMode's memory-aware replacement sequence, so this never needs a separate
        // "unload" branch of its own.
        _onFlowStateChanged = e =>
        {
            if (e.Current != GameFlowState.MainMenu) ReleaseTransitionFrame();
            var mode = ModeFor(e.Current);
            if (mode.HasValue && CurrentMode != mode.Value)
                StartCoroutine(LoadMode(mode.Value));
        };
        EventBus.Subscribe(_onFlowStateChanged);
    }

    void IAppModule.Shutdown()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
        ReleaseTransitionFrame();
    }

    // Boot/Intro/MainMenu/SongSelection/SongAnalysis all live in Frontend — SongAnalysis
    // deliberately does NOT map to Runner: analysis now runs entirely in the always-loaded UI Scene
    // (see SongAnalysisController), specifically so the Runner Mode Scene (and its 3D placeholder
    // world) never flashes into view behind the Analyzing screen while a song is still being
    // analyzed/its theme still transitioning. Runner only loads once GameFlowState actually reaches
    // Gameplay — see SongAnalysisController's own doc on the analysis→theme-transition→Gameplay
    // handoff. Fight is its own Mode Scene.
    private static GameMode? ModeFor(GameFlowState state) => state switch
    {
        GameFlowState.Boot or GameFlowState.Intro or GameFlowState.MainMenu or GameFlowState.SongSelection
            or GameFlowState.SongAnalysis => GameMode.Frontend,
        GameFlowState.Gameplay or GameFlowState.Results => GameMode.Runner,
        GameFlowState.Fight => GameMode.Fight,
        _ => null,
    };

    private IEnumerator EnsureUiSceneLoaded()
    {
        if (SceneManager.GetSceneByName(UiSceneName).isLoaded)
        {
            UiSceneLoaded = true;
            yield break;
        }

        var op = SceneManager.LoadSceneAsync(UiSceneName, LoadSceneMode.Additive);
        if (op == null)
        {
            Debug.LogError($"[SceneFlowController] '{UiSceneName}' isn't in Build Settings — cannot load it.");
            yield break;
        }

        yield return op;
        UiSceneLoaded = true;
        Debug.Log($"[SceneFlowController] '{UiSceneName}' scene loaded.");
    }

    /// <summary>Replaces the active mode scene. Heavy outgoing Runner/Fight scenes are disposed and
    /// unloaded before the next scene constructs its avatars; Core and UI remain loaded. Safe with the
    /// same mode already active (no-op).</summary>
    public IEnumerator LoadMode(GameMode mode)
    {
        // Several flow events can arrive during unloading / collection. Serialize them so
        // two runners or avatar builds cannot start concurrently.
        while (_modeTransitionInProgress) yield return null;
        if (CurrentMode == mode) yield break;
        _modeTransitionInProgress = true;
        try { yield return LoadModeCore(mode); }
        finally
        {
            _modeTransitionInProgress = false;
            if (CurrentMode != GameMode.Frontend ||
                (AppFlowController.Instance != null && AppFlowController.Instance.CurrentState != GameFlowState.MainMenu))
                ReleaseTransitionFrame();
        }
    }

    private IEnumerator LoadModeCore(GameMode mode)
    {
        if (CurrentMode == mode) yield break;

        if (!SceneNames.TryGetValue(mode, out string sceneName))
        {
            Debug.LogError($"[SceneFlowController] No scene name registered for GameMode.{mode}.");
            yield break;
        }

        // Validate before releasing the current scene so a missing destination cannot
        // leave the app without its current mode.
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[SceneFlowController] '{sceneName}' isn't in Build Settings — cannot load it.");
            yield break;
        }
        var previousMode = CurrentMode;
        bool releaseBeforeLoad = previousMode == GameMode.Fight || previousMode == GameMode.Runner;
        if (releaseBeforeLoad && mode == GameMode.Frontend)
        {
            if (_transitionFrame == null) _transitionFrame = gameObject.AddComponent<SceneTransitionFrame>();
            yield return _transitionFrame.Capture();
        }
        if (releaseBeforeLoad)
        {
            // Loading the next scene runs its Awake/avatar construction before LoadSceneAsync
            // completes. Release the outgoing avatars and arena BEFORE that allocation peak.
            yield return UnloadMode(previousMode.Value);
            CurrentMode = null;
            yield return null; // flush deferred mesh / renderer Destroy calls
            yield return Resources.UnloadUnusedAssets();
            System.GC.Collect();
            Debug.Log($"[SceneFlowController] Released {previousMode.Value} before loading {mode}; allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)} MB.");
        }

        var loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (loadOp == null)
        {
            Debug.LogError($"[SceneFlowController] '{sceneName}' isn't in Build Settings — cannot load it.");
            yield break;
        }
        yield return loadOp;

        var newScene = SceneManager.GetSceneByName(sceneName);
        if (newScene.IsValid()) SceneManager.SetActiveScene(newScene);

        CurrentMode = mode;

        if (previousMode.HasValue && !releaseBeforeLoad)
            yield return UnloadMode(previousMode.Value);

        Debug.Log($"[SceneFlowController] Mode Scene now '{sceneName}'" +
                  (previousMode.HasValue ? $" (unloaded '{SceneNames[previousMode.Value]}')." : "."));
    }

    private IEnumerator UnloadMode(GameMode mode)
    {
        if (mode == GameMode.Fight && FightSceneBootstrap.Instance != null)
        {
            var cleanup = FightSceneBootstrap.Instance.ShutdownStageAsync();
            while (!cleanup.IsCompleted) yield return null;
            if (cleanup.IsFaulted) Debug.LogException(cleanup.Exception);
        }
        if (!SceneNames.TryGetValue(mode, out string previousSceneName)) yield break;
        if (!SceneManager.GetSceneByName(previousSceneName).isLoaded) yield break;
        var unloadOp = SceneManager.UnloadSceneAsync(previousSceneName);
        if (unloadOp != null) yield return unloadOp;
    }

    /// <summary>Declares that `mode`'s scene is ALREADY loaded and active — for a Mode Scene opened
    /// directly (in the Editor, or as the app's current single entry scene) rather than loaded via
    /// LoadMode, so this controller's own bookkeeping still reflects reality and a later
    /// LoadMode(otherMode) correctly unloads it instead of leaving it behind or double-loading it.
    /// No-op once a mode is already tracked, so the first caller wins and nothing later overwrites
    /// it by accident.</summary>
    public void NotifyCurrentModeAlreadyLoaded(GameMode mode)
    {
        if (CurrentMode.HasValue) return;
        CurrentMode = mode;
        Debug.Log($"[SceneFlowController] '{SceneNames[mode]}' was already loaded — now tracked as the current Mode Scene.");
    }
}
