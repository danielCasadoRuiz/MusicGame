using UnityEngine;

/// <summary>
/// A style's Runner environment: for EACH slot, explicitly Inherit (base prefab), Override (this
/// prefab) or Disabled (nothing). Referenced by a theme override layer (MusicStyleVisualSO /
/// EventThemeSO / FrontendVisualPresetSO → runnerEnvironment); no style at all = everything inherited.
/// </summary>
[CreateAssetMenu(fileName = "RunnerEnvironmentStyle", menuName = "MusicGame/Environment/Runner Environment Style")]
public class RunnerEnvironmentStyleSO : ScriptableObject, IContentGroupProvider
{
    [Tooltip("Download group of this style's quality variants (Styles category). Empty = 'Style_<asset name>'.")]
    public string contentGroup = "";
    public string ContentGroupOrDefault => string.IsNullOrWhiteSpace(contentGroup) ? ContentKeys.Style(name) : contentGroup;

    [Tooltip("Horizon slot (far backdrop — mountains today).")]
    public EnvironmentModuleOverride horizon = new();
    [Tooltip("Music-reactive slot (spectrum bars today).")]
    public EnvironmentModuleOverride musicReactive = new();

    public EnvironmentModuleOverride Get(RunnerEnvironmentSlot slot) => slot switch
    {
        RunnerEnvironmentSlot.Horizon       => horizon,
        RunnerEnvironmentSlot.MusicReactive => musicReactive,
        _                                   => null,
    };
}
