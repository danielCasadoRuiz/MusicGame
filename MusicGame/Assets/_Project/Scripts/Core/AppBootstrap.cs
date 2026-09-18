using UnityEngine;

/// <summary>
/// THE Composition Root of the application — the only place that constructs, configures and wires
/// together the app's persistent, cross-scene modules. Contains no gameplay logic of its own.
///
/// Sequence (every module is fully CONSTRUCTED before any of them is INITIALIZED, so a module's
/// Initialize(context) can safely reach into `context` for another module's reference regardless
/// of construction order):
///   1. construct each module (AddComponent — runs its Awake())
///   2. Configure() each module that has real editable config (IConfigurableModule&lt;T&gt;),
///      reading from the local, always-available AppConfigSO (Resources — must exist before any
///      Addressables system is even up, same reason the Sentis tagger model is Resources-loaded
///      rather than Addressable)
///   3. build AppContext from the constructed modules
///   4. Initialize(context) each module
///
/// Guaranteed to run exactly once before any scene's own Awake(), regardless of which scene
/// happens to load first — deliberately NOT a scene-placed GameObject, so there's nothing to
/// forget to drag into a scene, and no risk of two competing bootstrap objects if a future
/// Frontend/Gameplay/Fight scene split each accidentally included one.
///
/// Only genuinely persistent, cross-scene services belong here (GameSession, AppFlowController,
/// ThemeAssetLoader, ThemeManager so far — see later phases for a possible future Audio Analysis
/// service). Anything scene-local (GameplayManager, AudioSystemBootstrapper, HorizonWorld, etc.)
/// stays exactly where it is; this does not replace or wrap them — a later phase adds small
/// SceneBootstrapper components that connect THOSE to this Context, instead of them each reaching
/// for a global directly.
/// </summary>
public static class AppBootstrap
{
    /// <summary>The single static access point for the composed AppContext — scene bootstrappers
    /// (added in a later phase) read this. New modules should be added to AppContext and consumed
    /// through it, not by growing another standalone `SomeService.Instance`.</summary>
    public static AppContext Context { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (Context != null) return; // already booted

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        if (appConfig == null)
            Debug.LogWarning("[AppBootstrap] No 'Resources/AppConfig.asset' found — modules with " +
                              "real config (e.g. AppFlowController) will fall back to hardcoded defaults.");

        var go = new GameObject("[App Bootstrap]");
        Object.DontDestroyOnLoad(go);

        // 1. construct
        var gameSession = go.AddComponent<GameSession>();
        var appFlow     = go.AddComponent<AppFlowController>();
        var themeAssets = go.AddComponent<ThemeAssetLoader>();
        var themeManager = go.AddComponent<ThemeManager>();

        // 2. configure (only modules that actually have config)
        appFlow.Configure(appConfig != null ? appConfig.flow : null);
        themeManager.Configure(appConfig != null ? appConfig.theme : null);

        // 3. compose context
        Context = new AppContext(gameSession, appFlow, themeAssets, themeManager);

        // 4. initialize (cross-module wiring) — ThemeManager's Initialize immediately resolves a
        // BaseTheme-only CurrentTheme and kicks off loading a random frontend visual, so it must
        // run AFTER ThemeAssetLoader is already constructed above (it is — see the two-phase
        // sequence in this class's own doc).
        ((IAppModule)gameSession).Initialize(Context);
        ((IAppModule)appFlow).Initialize(Context);
        ((IAppModule)themeAssets).Initialize(Context);
        ((IAppModule)themeManager).Initialize(Context);

        Application.quitting += Shutdown;
    }

    private static void Shutdown()
    {
        if (Context == null) return;
        ((IAppModule)Context.GameSession).Shutdown();
        ((IAppModule)Context.AppFlow).Shutdown();
        ((IAppModule)Context.Theme).Shutdown();
        ((IAppModule)Context.ThemeAssets).Shutdown();
        Context = null;
        Application.quitting -= Shutdown;
    }
}
