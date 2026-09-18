/// <summary>
/// A persistent, app-level service created and owned by AppBootstrap (the Composition Root).
/// Two-phase lifecycle on purpose: Unity's own Awake() should only do self-contained setup
/// (Instance assignment, DontDestroyOnLoad) — Initialize(AppContext) runs AFTER every module has
/// already been constructed, so a module can safely reach into `context` for another module's
/// reference without caring which one happened to construct first. Shutdown() is the inverse,
/// called by AppBootstrap on app quit — mainly a hook for future modules that hold unmanaged
/// resources/handles/subscriptions that must not outlive the app.
/// </summary>
public interface IAppModule
{
    void Initialize(AppContext context);
    void Shutdown();
}
