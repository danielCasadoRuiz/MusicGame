/// <summary>
/// The composed set of persistent, app-level services — built once by AppBootstrap (the
/// Composition Root) and handed to every module's Initialize(), and to each scene's own
/// SceneBootstrapper. This is the ONE place new code should reach for a persistent service.
///
/// GameSession/AppFlowController/ThemeManager keep their own static `Instance` too, only because
/// that already-established project convention (MusicClock.Instance, CameraFollow.Instance, etc.)
/// has other existing consumers — but going forward, NEW cross-scene services should be added here
/// and consumed via AppContext, not by growing another `SomeService.Instance`.
/// </summary>
public class AppContext
{
    public GameSession       GameSession { get; }
    public AppFlowController AppFlow     { get; }
    public ThemeAssetLoader  ThemeAssets { get; }
    public ThemeManager      Theme       { get; }

    public AppContext(GameSession gameSession, AppFlowController appFlow, ThemeAssetLoader themeAssets, ThemeManager theme)
    {
        GameSession = gameSession;
        AppFlow     = appFlow;
        ThemeAssets = themeAssets;
        Theme       = theme;
    }
}
