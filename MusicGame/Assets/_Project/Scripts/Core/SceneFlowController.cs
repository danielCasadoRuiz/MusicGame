using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns loading/unloading of the additive Mode Scenes (Frontend/Runner/Fight — see GameMode) and of
/// the always-loaded UI Scene. Persistent (IAppModule, constructed by AppBootstrap on the same
/// [App Bootstrap] GameObject as GameSession/AppFlowController/ThemeManager) — the single place that
/// calls SceneManager.LoadSceneAsync/UnloadSceneAsync, so no individual screen or gameplay script
/// ever loads a scene directly ("AppFlow decideix l'estat, SceneFlow executa les càrregues" — see the
/// multi-scene refactor plan's own section on this split of responsibility).
///
/// Target runtime layout: Core (this object's home, whichever scene happens to load first — see
/// AppBootstrap's own doc) + UI (loaded once here, right after boot, never unloaded) + exactly one
/// Mode Scene, with a brief overlap of two Mode Scenes while LoadMode is transitioning (load next,
/// THEN unload previous — never the reverse, same ordering discipline as ThemeManager's Addressables
/// swaps). Every GameFlowState maps to exactly one Mode Scene (see ModeFor) — Frontend and Runner
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

    void IAppModule.Initialize(AppContext context)
    {
        StartCoroutine(EnsureUiSceneLoaded());

        // "AppFlow decideix l'estat, SceneFlow executa les càrregues" — this is the one place that
        // translates a GameFlowState transition into an actual scene load/unload. Every state maps
        // to exactly one Mode Scene (ModeFor) — Frontend/Runner/Fight fully replace each other via
        // LoadMode's own load-next-then-unload-previous sequence, so this never needs a separate
        // "unload" branch of its own.
        _onFlowStateChanged = e =>
        {
            var mode = ModeFor(e.Current);
            if (mode.HasValue && CurrentMode != mode.Value)
                StartCoroutine(LoadMode(mode.Value));
        };
        EventBus.Subscribe(_onFlowStateChanged);
    }

    void IAppModule.Shutdown()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
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

    /// <summary>Loads `mode`'s scene additively, makes it the active scene, then unloads whatever
    /// Mode Scene was previously active — Core and UI are never touched. Safe to call again with the
    /// same mode already active (no-op).</summary>
    public IEnumerator LoadMode(GameMode mode)
    {
        if (CurrentMode == mode) yield break;

        if (!SceneNames.TryGetValue(mode, out string sceneName))
        {
            Debug.LogError($"[SceneFlowController] No scene name registered for GameMode.{mode}.");
            yield break;
        }

        var previousMode = CurrentMode;

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

        if (previousMode.HasValue && SceneNames.TryGetValue(previousMode.Value, out string previousSceneName))
        {
            var unloadOp = SceneManager.UnloadSceneAsync(previousSceneName);
            if (unloadOp != null) yield return unloadOp;
        }

        Debug.Log($"[SceneFlowController] Mode Scene now '{sceneName}'" +
                  (previousMode.HasValue ? $" (unloaded '{SceneNames[previousMode.Value]}')." : "."));
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
