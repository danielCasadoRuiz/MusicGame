using UnityEngine;

/// <summary>
/// Single source of truth for "should the UI/input be touch/mobile-shaped or desktop-shaped" —
/// every UI screen and input reader asks PlatformService.IsMobile instead of checking
/// Application.isMobilePlatform directly. That real platform check is always false in the Editor
/// (there's no actual device), and Mobile is this project's real first target platform, so
/// AppConfigSO.editorPlatformSimulation lets a developer force Mobile while testing in the Editor —
/// with mouse clicks/drags standing in for touches, since Unity's EventSystem already treats both
/// identically (see VirtualJoystick/TouchJumpButton — no separate "simulate touch" system needed).
/// A real build ignores the override entirely and always uses the real platform.
/// </summary>
public static class PlatformService
{
    public static PlatformMode Current
    {
        get
        {
#if UNITY_EDITOR
            var appConfig = Resources.Load<AppConfigSO>("AppConfig");
            if (appConfig != null) return appConfig.editorPlatformSimulation;
#endif
            return Application.isMobilePlatform ? PlatformMode.Mobile : PlatformMode.Desktop;
        }
    }

    public static bool IsMobile => Current == PlatformMode.Mobile;
}
