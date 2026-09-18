/// <summary>
/// Which input/UI shape the app should present — see PlatformService for how the EFFECTIVE mode is
/// resolved (a real device always uses its own Application.isMobilePlatform; this enum only matters
/// as an Editor-only override for testing mobile UI/input without a device — see
/// AppConfigSO.editorPlatformSimulation).
/// </summary>
public enum PlatformMode
{
    Desktop,
    Mobile,
}
