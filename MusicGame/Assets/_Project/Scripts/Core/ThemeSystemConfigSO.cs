using UnityEngine;

/// <summary>
/// The Theme system's own editable config — composed under AppConfigSO like every other module
/// config (see AppConfigSO's own doc). Everything referenced here is LOCAL/always-available
/// (BaseTheme, the registries) — the registries only hold AssetReferences, never the heavy
/// MusicStyleVisualSO/EventThemeSO content itself, so this whole config stays lightweight even as
/// more styles/events are added (see MusicStyleRegistrySO/EventThemeRegistrySO/ThemeAssetLoader).
/// </summary>
[CreateAssetMenu(fileName = "ThemeSystemConfig", menuName = "MusicGame/App/Theme System Config")]
public class ThemeSystemConfigSO : ScriptableObject
{
    public BaseThemeSO             baseTheme;
    public MusicStyleRegistrySO    musicStyleRegistry;
    public EventThemeRegistrySO    eventThemeRegistry;
    public FrontendVisualRegistrySO frontendVisualRegistry;

    [Tooltip("How long ThemeReceiverBehaviour's color interpolation takes when CurrentTheme changes " +
             "— see ThemeTransitionController. 0 = instant snap.")]
    public float themeTransitionDuration = 0.4f;
}
