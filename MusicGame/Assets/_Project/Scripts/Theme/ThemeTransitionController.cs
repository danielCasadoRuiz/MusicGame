/// <summary>
/// Owns the ONE configurable value the Theme Transition architecture needs today — how long a
/// receiver's color interpolation takes when CurrentTheme changes (Section 4 of the multi-scene
/// refactor plan: "la transició ha de tenir duració configurable"). Deliberately thin: the actual
/// interpolation logic lives entirely in ThemeReceiverBehaviour, which every screen already
/// inherits from — this class never learns about a specific Button/Image/Text, it only answers "how
/// long should that take".
///
/// Reads ThemeSystemConfigSO.themeTransitionDuration through ThemeManager.Config (already exposed
/// for the Theme Debugger) rather than being its own persistent module — there is no per-instance
/// state to manage here, just one shared config number every receiver in every scene needs cheap,
/// consistent access to.
/// </summary>
public static class ThemeTransitionController
{
    private const float DefaultDuration = 0.4f;

    public static float Duration =>
        ThemeManager.Instance != null && ThemeManager.Instance.Config != null
            ? ThemeManager.Instance.Config.themeTransitionDuration
            : DefaultDuration;
}
