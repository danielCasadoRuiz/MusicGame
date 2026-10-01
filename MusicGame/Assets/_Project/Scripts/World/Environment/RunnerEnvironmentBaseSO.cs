using UnityEngine;

/// <summary>
/// BASE Runner environment — the default module of every slot. Referenced by BaseThemeSO
/// (runnerEnvironment), so it is the always-available fallback: a style that inherits a slot gets
/// this slot's prefab; with no style at all every slot comes from here.
/// </summary>
[CreateAssetMenu(fileName = "RunnerEnvironment_Base", menuName = "MusicGame/Environment/Runner Environment Base")]
public class RunnerEnvironmentBaseSO : ScriptableObject
{
    [Header("Default modules")]
    [Tooltip("Horizon slot — far backdrop (currently the PNG mountain silhouette layers).")]
    public GameObject horizonPrefab;
    [Tooltip("Music-reactive slot — currently the 3D spectrum bars.")]
    public GameObject musicReactivePrefab;

    [Header("Testing (Editor / development builds only)")]
    [Tooltip("When set, the Runner uses THIS style instead of the one resolved from the current " +
             "theme / music style. Clear it to go back to normal resolution. Ignored in release builds.")]
    public RunnerEnvironmentStyleSO testStyleOverride;

    public GameObject GetPrefab(RunnerEnvironmentSlot slot) => slot switch
    {
        RunnerEnvironmentSlot.Horizon       => horizonPrefab,
        RunnerEnvironmentSlot.MusicReactive => musicReactivePrefab,
        _                                   => null,
    };
}
