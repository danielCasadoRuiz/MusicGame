using UnityEngine;

/// <summary>
/// The independent module SLOTS of the Runner visual environment. Each slot resolves to at most one
/// prefab (see RunnerEnvironmentController). Add new slots here (Sky, FarScenery, MidScenery,
/// NearScenery, Lighting, Fog, VFX …) together with a field on RunnerEnvironmentBaseSO /
/// RunnerEnvironmentStyleSO — the resolution and lifecycle code is slot-generic.
/// </summary>
public enum RunnerEnvironmentSlot
{
    Horizon,
    MusicReactive,
}

/// <summary>How a style decides one module slot — explicit, never inferred from a null prefab.</summary>
public enum EnvironmentOverrideMode
{
    [Tooltip("Use the prefab defined by the base Runner environment.")]
    Inherit,
    [Tooltip("Use the prefab assigned here instead of the base one.")]
    Override,
    [Tooltip("Instantiate nothing for this slot, even if the base environment has a module.")]
    Disabled,
}

/// <summary>One slot's decision in a RunnerEnvironmentStyleSO. `prefab` only matters for Override.</summary>
[System.Serializable]
public class EnvironmentModuleOverride
{
    public EnvironmentOverrideMode mode = EnvironmentOverrideMode.Inherit;
    [Tooltip("Only used when Mode = Override. Must contain an IRunnerEnvironmentModule component " +
             "(e.g. SpectrumBars3D, HorizonMountainLayers).")]
    public GameObject prefab;
}

/// <summary>Everything a module gets from the Runner — handed over by the controller, so prefabs
/// never need scene references or runtime searches. One instance, reused every frame.</summary>
public class RunnerEnvironmentContext
{
    /// <summary>The Runner's resolved HorizonConfig (a module may use its own override instead).</summary>
    public HorizonConfig Config;
    /// <summary>The Horizon water plane (reflection baseline for the bars).</summary>
    public HorizonWater Water;
    /// <summary>The module instance's own transform — generated content goes under it.</summary>
    public Transform ModuleRoot;
    /// <summary>Per-frame: the path/world and the song clock time.</summary>
    public MusicWorldManager World;
    public float SongTime;
}

/// <summary>
/// Implemented by a module prefab's component(s): initialized ONCE by RunnerEnvironmentController
/// after instantiation, then ticked from HorizonWorld's deterministic LateUpdate order.
/// </summary>
public interface IRunnerEnvironmentModule
{
    void InitializeModule(RunnerEnvironmentContext context);
    void TickModule(RunnerEnvironmentContext context);
}
