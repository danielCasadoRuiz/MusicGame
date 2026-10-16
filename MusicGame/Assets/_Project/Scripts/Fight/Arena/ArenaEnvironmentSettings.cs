using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Optional GLOBAL ambience of an arena prefab (skybox, ambient light, fog). Put it on the arena root
/// (or its Lighting node) and tick what the arena overrides. ArenaEnvironmentLoader applies it when the
/// arena loads and restores the previous values when the arena is unloaded — the Fight scene's own
/// settings are never permanently changed. Without this component an arena leaves them untouched.
/// </summary>
public class ArenaEnvironmentSettings : MonoBehaviour
{
    [Header("Skybox")]
    public bool overrideSkybox;
    public Material skybox;

    [Header("Ambient light")]
    public bool overrideAmbient;
    public AmbientMode ambientMode = AmbientMode.Flat;
    public Color ambientColor = new(0.35f, 0.35f, 0.4f);

    [Header("Fog")]
    public bool overrideFog;
    public bool fog;
    public Color fogColor = new(0.5f, 0.5f, 0.55f);
    public float fogDensity = 0.01f;

    public struct Snapshot
    {
        public Material Skybox; public AmbientMode AmbientMode; public Color AmbientColor;
        public bool Fog; public Color FogColor; public float FogDensity;
    }

    public Snapshot Apply()
    {
        var before = new Snapshot
        {
            Skybox = RenderSettings.skybox, AmbientMode = RenderSettings.ambientMode, AmbientColor = RenderSettings.ambientLight,
            Fog = RenderSettings.fog, FogColor = RenderSettings.fogColor, FogDensity = RenderSettings.fogDensity,
        };
        if (overrideSkybox) RenderSettings.skybox = skybox;
        if (overrideAmbient) { RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambientColor; }
        if (overrideFog) { RenderSettings.fog = fog; RenderSettings.fogColor = fogColor; RenderSettings.fogDensity = fogDensity; }
        if (overrideSkybox || overrideAmbient) DynamicGI.UpdateEnvironment();
        return before;
    }

    public static void Restore(Snapshot s)
    {
        RenderSettings.skybox = s.Skybox;
        RenderSettings.ambientMode = s.AmbientMode;
        RenderSettings.ambientLight = s.AmbientColor;
        RenderSettings.fog = s.Fog;
        RenderSettings.fogColor = s.FogColor;
        RenderSettings.fogDensity = s.FogDensity;
        DynamicGI.UpdateEnvironment();
    }
}
